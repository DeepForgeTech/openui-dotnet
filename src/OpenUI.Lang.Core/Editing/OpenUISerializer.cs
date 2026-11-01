using System.Text;
using OpenUI.Lang.Prompting;
using OpenUI.Lang.Syntax;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Editing;

/// <summary>Writes a rendered tree back to OpenUI Lang source (language.md, section 9.2).</summary>
public static class OpenUISerializer
{
    /// <summary>
    /// Serializes <paramref name="entry"/> as a program (language.md, section 9.2): the entry as <c>root</c>
    /// first, then every component that came from another statement as its own statement, children
    /// before parents, then <paramref name="hostState"/> as state declarations. Props are written by
    /// position in schema order, trailing optional nulls trimmed. Queries and mutations do not round-trip.
    /// </summary>
    /// <param name="entry">The rendered entry component.</param>
    /// <param name="hostState">State to declare, keyed by name with its <c>$</c>; null for none.</param>
    public static string Serialize(OuiComponent entry, IReadOnlyDictionary<string, OuiValue>? hostState = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var writer = new Writer(entry.StatementId);
        writer.FindSharedStatements(entry, entry.StatementId, []);
        var output = new StringBuilder("root = ");
        writer.WriteCall(output, entry);
        foreach (var statement in writer.Statements)
        {
            output.Append('\n').Append(statement);
        }

        foreach (var (name, value) in hostState ?? new Dictionary<string, OuiValue>())
        {
            output.Append('\n').Append(name).Append(" = ");
            writer.WriteValue(output, value);
        }

        return output.ToString();
    }

    private sealed class Writer(string? entryId)
    {
        private readonly HashSet<string> _named = new(StringComparer.Ordinal);
        private readonly HashSet<string> _inlined = new(StringComparer.Ordinal);
        private string? _current = entryId;

        // Named components in the order they finished, so children come before parents.
        public List<string> Statements { get; } = [];

        // A statement that gave more than one component, a list or a loop, cannot be written as
        // "name = Component(...)", or the reference would read back as one component repeated. Those
        // components are written where they are used. Components inside a statement carry its name.
        public void FindSharedStatements(OuiValue value, string? current, Dictionary<string, OuiComponent> first)
        {
            if (value.TryGetArray(out var items))
            {
                foreach (var item in items)
                {
                    FindSharedStatements(item, current, first);
                }
            }
            else if (value.TryGetObject(out var entries))
            {
                foreach (var (_, entry) in entries)
                {
                    FindSharedStatements(entry, current, first);
                }
            }
            else if (value.TryGetComponent(out var component))
            {
                var id = component.StatementId;
                if (id is not null && id != current && !first.TryAdd(id, component) && !ReferenceEquals(first[id], component))
                {
                    _inlined.Add(id);
                }

                foreach (var prop in component.Props)
                {
                    FindSharedStatements(prop.Value, id ?? current, first);
                }
            }
        }

        public void WriteValue(StringBuilder output, OuiValue value)
        {
            if (value.TryGetString(out var text))
            {
                output.Append(ToolSchemaText.Quote(text));
            }
            else if (value.TryGetNumber(out var number))
            {
                output.Append(double.IsFinite(number) ? Ecma.NumberToString(number) : "null");
            }
            else if (value.TryGetBoolean(out var flag))
            {
                output.Append(flag ? "true" : "false");
            }
            else if (value.TryGetArray(out var array))
            {
                WriteList(output, '[', array.Items, ']');
            }
            else if (value.TryGetObject(out var obj))
            {
                output.Append('{');
                for (var i = 0; i < obj.Count; i++)
                {
                    output.Append(i == 0 ? "" : ", ");
                    var key = obj[i].Key;
                    output.Append(IsName(key) ? key : ToolSchemaText.Quote(key)).Append(": ");
                    WriteValue(output, obj[i].Value);
                }

                output.Append('}');
            }
            else if (value.TryGetComponent(out var component))
            {
                WriteComponent(output, component);
            }
            else if (value.TryGetAction(out var action))
            {
                WriteAction(output, action);
            }
            else
            {
                output.Append("null");
            }
        }

        public void WriteCall(StringBuilder output, OuiComponent component)
        {
            output.Append(component.Type).Append('(');
            var props = component.Spec?.Props ?? [];
            var count = props.Length;
            while (count > 0 && !props[count - 1].Required && component[props[count - 1].Name] is { Binding: null, Value.IsNull: true })
            {
                count--;
            }

            for (var i = 0; i < count; i++)
            {
                output.Append(i == 0 ? "" : ", ");
                var prop = component[props[i].Name];
                if (prop.Binding is { } binding)
                {
                    output.Append(binding.Name);
                }
                else
                {
                    WriteValue(output, prop.Value);
                }
            }

            output.Append(')');
        }

        // A component from another statement is written as a reference to it, defined once.
        private void WriteComponent(StringBuilder output, OuiComponent component)
        {
            var id = component.StatementId;
            if (id is null || id == _current || id == "root" || _inlined.Contains(id))
            {
                WriteCall(output, component);
                return;
            }

            output.Append(id);
            if (!_named.Add(id))
            {
                return;
            }

            var outer = _current;
            _current = id;
            var statement = new StringBuilder(id).Append(" = ");
            WriteCall(statement, component);
            _current = outer;
            Statements.Add(statement.ToString());
        }

        private void WriteAction(StringBuilder output, OuiAction action)
        {
            if (action.Steps.Length == 1)
            {
                WriteStep(output, action.Steps[0]);
                return;
            }

            output.Append("Action([");
            for (var i = 0; i < action.Steps.Length; i++)
            {
                output.Append(i == 0 ? "" : ", ");
                WriteStep(output, action.Steps[i]);
            }

            output.Append("])");
        }

        private void WriteStep(StringBuilder output, ActionStep step)
        {
            switch (step)
            {
                case SetStep set:
                    output.Append("@Set(").Append(set.Target.Name).Append(", ");
                    WriteValue(output, set.Value);
                    output.Append(')');
                    break;
                case ResetStep reset:
                    output.Append("@Reset(").AppendJoin(", ", reset.Targets.Select(t => t.Name)).Append(')');
                    break;
                case RunStep run:
                    output.Append("@Run(").Append(run.StatementId).Append(')');
                    break;
                case ToAssistantStep say:
                    output.Append("@ToAssistant(");
                    WriteValue(output, say.Message);
                    if (say.Context is { } context)
                    {
                        output.Append(", ");
                        WriteValue(output, context);
                    }

                    output.Append(')');
                    break;
                case OpenUrlStep open:
                    output.Append("@OpenUrl(");
                    WriteValue(output, open.Url);
                    output.Append(')');
                    break;
                case CustomActionStep custom:
                    output.Append('@').Append(custom.Name);
                    WriteList(output, '(', [.. custom.Action.Params.Select(p => custom.Parameters[p.Name])], ')');
                    break;
            }
        }

        private void WriteList(StringBuilder output, char open, IReadOnlyList<OuiValue> items, char close)
        {
            output.Append(open);
            for (var i = 0; i < items.Count; i++)
            {
                output.Append(i == 0 ? "" : ", ");
                WriteValue(output, items[i]);
            }

            output.Append(close);
        }

        private static bool IsName(string key) =>
            key.Length > 0 && Lexer.IsIdentifierStart(key[0]) && key.All(Lexer.IsIdentifierPart);
    }
}
