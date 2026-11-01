using System.Collections.Immutable;
using System.Text;

namespace OpenUI.Lang.Library;

/// <summary>
/// Reads and writes signature lines (prompt.md, section 5.1), such as
/// <c>Input(name: string, value?: $binding&lt;string&gt;)</c>.
/// </summary>
internal static class SignatureText
{
    private const string BindingPrefix = "$binding<";

    /// <summary>The names of the params a signature prints as <c>$binding&lt;type&gt;</c> (prompt.md, section 2.3).</summary>
    public static IReadOnlySet<string> BindableProps(string signature) =>
        Params(signature).Where(p => p.Type.StartsWith(BindingPrefix, StringComparison.Ordinal)).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>The printed type of each param, such as <c>@CopyToClipboard | @OpenUrl</c>, in order.</summary>
    public static IEnumerable<string> ParamTypes(string signature) => Params(signature).Select(p => p.Type);

    /// <summary>
    /// Props read from the signature alone, for a LibrarySpec with no schema: names, optionality and
    /// bindings. Types are not reconstructed, so every prop takes any value.
    /// </summary>
    public static ImmutableArray<PropSpec> ReadProps(string signature) =>
        [.. Params(signature).Select(p => new PropSpec(p.Name, TypeShape.Any, !p.Optional, null, p.Type.StartsWith(BindingPrefix, StringComparison.Ordinal)))];

    /// <summary>Writes a signature line from a declaration (prompt.md, section 5.1).</summary>
    public static string Write(string name, ImmutableArray<PropSpec> props, TypeShape? returns = null)
    {
        var text = new StringBuilder(name).Append('(');
        for (var i = 0; i < props.Length; i++)
        {
            var prop = props[i];
            text.Append(i == 0 ? "" : ", ").Append(prop.Name).Append(prop.IsEffectivelyRequired ? "" : "?").Append(": ");
            var type = TypeText(prop.Type);
            text.Append(prop.Bindable ? $"{BindingPrefix}{type}>" : type);
        }

        text.Append(')');
        if (returns is not null)
        {
            text.Append(" → ").Append(TypeText(returns));
        }

        return text.ToString();
    }

    /// <summary>The printed form of a shape (prompt.md, section 5.1).</summary>
    public static string TypeText(TypeShape shape) => shape switch
    {
        EnumShape e => string.Join(" | ", e.Values.Select(v => $"\"{v}\"")),
        ArrayShape a => (HasAlternatives(a.Item) ? $"({TypeText(a.Item)})" : TypeText(a.Item)) + "[]",
        ObjectShape o => "{" + string.Join(", ", o.Fields.Select(f => $"{f.Name}{(f.IsEffectivelyRequired ? "" : "?")}: {TypeText(f.Type)}")) + "}",
        ComponentRefShape { Components.IsEmpty: true } => "any",
        ComponentRefShape r => string.Join(" | ", r.Components),
        ActionSlotShape { Actions.IsEmpty: true } => "ActionExpression",
        ActionSlotShape s => string.Join(" | ", s.Actions.Select(a => "@" + a)),
        UnionShape u => string.Join(" | ", u.Options.Select(TypeText)),
        _ => shape.Kind switch
        {
            TypeShapeKind.String => "string",
            TypeShapeKind.Number => "number",
            TypeShapeKind.Boolean => "boolean",
            _ => "any",
        },
    };

    // "a" | "b"[] would read as a list of only "b".
    private static bool HasAlternatives(TypeShape shape) => shape switch
    {
        EnumShape e => e.Values.Length > 1,
        ComponentRefShape r => r.Components.Length > 1,
        ActionSlotShape s => s.Actions.Length > 1,
        UnionShape u => u.Options.Length > 1,
        _ => false,
    };

    private static List<(string Name, bool Optional, string Type)> Params(string signature)
    {
        var result = new List<(string, bool, string)>();
        var open = signature.IndexOf('(');
        if (open < 0)
        {
            return result;
        }

        var depth = 0;
        var inString = false;
        var start = open + 1;
        for (var i = start; i < signature.Length; i++)
        {
            var c = signature[i];
            if (inString)
            {
                inString = c != '"';
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '(' or '[' or '{' or '<':
                    depth++;
                    break;
                case ')' when depth == 0:
                    Add(signature[start..i]);
                    return result;
                case ')' or ']' or '}' or '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    Add(signature[start..i]);
                    start = i + 1;
                    break;
            }
        }

        return result;

        void Add(string part)
        {
            var colon = part.IndexOf(':');
            var name = (colon < 0 ? part : part[..colon]).Trim();
            if (name.Length == 0)
            {
                return;
            }

            var optional = name.EndsWith('?');
            result.Add((optional ? name[..^1] : name, optional, colon < 0 ? "any" : part[(colon + 1)..].Trim()));
        }
    }
}
