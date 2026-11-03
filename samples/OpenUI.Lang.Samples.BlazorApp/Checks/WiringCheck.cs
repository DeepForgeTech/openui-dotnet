using System.Collections.Immutable;
using System.Text.Json;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;
using OpenUI.Lang.Syntax;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Samples.BlazorApp.Checks;

/// <summary>
/// Finds programs that parse and render but whose state isn't wired to do what it looks like: a Query
/// that fetches with a required arg still null or empty, a Query that won't fetch again when its choice
/// changes or when another Query it reads arrives (spec §6.1), and a <c>$variable</c> something writes
/// but nothing reads. Each finding is an
/// <see cref="OpenUIError"/> with an <c>x-</c> code, so it shows and goes back to the model the same way
/// as the parser's own errors. It reads only the syntax, the library and the tool schemas, so nothing
/// here depends on MudBlazor.
/// </summary>
public static class WiringCheck
{
    /// <summary>A required Query arg is a <c>$variable</c> that starts as null.</summary>
    public static readonly OpenUIErrorCode NullQueryArg = new("x-query-null-arg");

    /// <summary>A <c>$variable</c> reaches a Query's args only through another statement, so changing it doesn't fetch again.</summary>
    public static readonly OpenUIErrorCode IndirectQueryState = new("x-query-indirect-state");

    /// <summary>A Query's args read another Query's result, which it never fetches again for (spec §6.1).</summary>
    public static readonly OpenUIErrorCode QueryReadsQuery = new("x-query-reads-query");

    /// <summary>A <c>$variable</c> is written but nothing reads it, so writing it changes nothing.</summary>
    public static readonly OpenUIErrorCode UnreadState = new("x-state-unread");

    /// <summary>The findings for a finished program, in statement order; empty for one that doesn't parse.</summary>
    public static OpenUIErrorList Check(OpenUILibrary library, IEnumerable<ToolDescriptor> tools, string program)
    {
        var document = new OpenUIParser(library).Parse(program);
        var statements = document.Statements.Select(statement => statement.Syntax).ToList();
        if (statements.Count == 0)
        {
            return OpenUIErrorList.Empty;
        }

        var facts = new ProgramFacts(library, statements);
        var required = tools.ToDictionary(tool => tool.Name, RequiredArgs, StringComparer.Ordinal);
        var errors = new List<OpenUIError>();
        foreach (var statement in statements)
        {
            if (statement.Value is CallSyntax { Head: CallHeadKind.Component, Name: "Query" } query)
            {
                errors.AddRange(CheckQuery(facts, required, statement.Name, query));
            }
        }

        errors.AddRange(CheckUnread(facts));
        return OpenUIErrorList.Create(errors);
    }

    /// <summary>The runtime's errors followed by the wiring findings, as one list to show and to send back.</summary>
    public static OpenUIErrorList Join(OpenUIErrorList errors, OpenUIErrorList findings) =>
        findings.Count == 0 ? errors : errors.Count == 0 ? findings : OpenUIErrorList.Create(errors.Concat(findings));

    private static IEnumerable<OpenUIError> CheckQuery(ProgramFacts program, Dictionary<string, ImmutableHashSet<string>> required, string name, CallSyntax query)
    {
        if (query.Arguments.Length < 2)
        {
            yield break;
        }

        var tool = query.Arguments[0] is LiteralSyntax literal && literal.Value.TryGetString(out var text) ? text : null;
        var args = query.Arguments[1];

        if (tool is not null && required.TryGetValue(tool, out var needed) && args is ObjectSyntax entries)
        {
            foreach (var entry in entries.Entries)
            {
                if (entry.Value is StateReferenceSyntax state && needed.Contains(entry.Key) && program.StartsBlank(state.Name) is { } start)
                {
                    yield return new OpenUIError
                    {
                        Source = OpenUIErrorSource.Query,
                        Code = NullQueryArg,
                        StatementId = name,
                        ToolName = tool,
                        Path = $"/args/{entry.Key}",
                        Message = $"{name} sends {entry.Key}: {state.Name}, and {state.Name} starts as {start}, so the first fetch has no {entry.Key} and fails. Declare {state.Name} with a real default.",
                        Hint = $"{state.Name} = <a real {entry.Key}>",
                    };
                }
            }
        }

        if (args is ObjectSyntax keyed)
        {
            foreach (var entry in keyed.Entries)
            {
                if (program.QueryReachedBy(entry.Value, name) is var (source, through))
                {
                    var via = through == source ? "" : $" through {through}";
                    yield return new OpenUIError
                    {
                        Source = OpenUIErrorSource.Query,
                        Code = QueryReadsQuery,
                        StatementId = name,
                        ToolName = tool,
                        Path = $"/args/{entry.Key}",
                        Message = $"{name}'s {entry.Key} reads {source}{via}, another Query's result. A Query runs when the program loads and again only when a $variable in its args changes, so {name} fetches with the defaults of {source} and never again once {source} arrives. Put the value in a $variable with a real default, and let a control set it from {source}.",
                        Hint = $"$picked = <a real {entry.Key}>",
                    };
                }
            }
        }

        var written = new HashSet<string>(StringComparer.Ordinal);
        Expressions.States(args, written);
        foreach (var (state, through) in program.StatesThroughReferences(args))
        {
            if (!written.Contains(state))
            {
                yield return new OpenUIError
                {
                    Source = OpenUIErrorSource.Query,
                    Code = IndirectQueryState,
                    StatementId = name,
                    ToolName = tool,
                    Path = "/args",
                    Message = $"{name}'s args read {state} only through {through}, so changing {state} won't fetch again (a Query re-runs only for a $variable written in its args). Write {state} in the args themselves.",
                    Hint = $"{{...: {state}}}",
                };
            }
        }
    }

    private static IEnumerable<OpenUIError> CheckUnread(ProgramFacts program)
    {
        foreach (var (state, writer) in program.Unread)
        {
            yield return new OpenUIError
            {
                Source = OpenUIErrorSource.Runtime,
                Code = UnreadState,
                StatementId = writer,
                Message = $"{state} is set in {writer} but nothing reads it, so setting it changes nothing on the page. Use {state} where it should take effect, such as a Query's args or an expression, or leave it out.",
            };
        }
    }

    private static ImmutableHashSet<string> RequiredArgs(ToolDescriptor tool) =>
        tool.InputSchema is { ValueKind: JsonValueKind.Object } schema && schema.TryGetProperty("required", out var list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)]
            : [];

    /// <summary>What the program writes and reads, worked out once from its statements.</summary>
    private sealed class ProgramFacts
    {
        private readonly Dictionary<string, StatementSyntax> _byName;

        public ProgramFacts(OpenUILibrary library, IReadOnlyList<StatementSyntax> statements)
        {
            _byName = new Dictionary<string, StatementSyntax>(StringComparer.Ordinal);
            foreach (var statement in statements)
            {
                // A later statement of the same name wins, as in the program itself (spec §2.4).
                _byName[statement.Name] = statement;
            }

            var inForm = InsideForms(_byName);
            var walker = new UseWalker(library);
            foreach (var statement in _byName.Values)
            {
                walker.Walk(statement.Name, statement.Value, inForm.Contains(statement.Name));
            }

            // A bound control shows its value, so it reads what anything else writes: a Modal opens on what a button sets.
            // Only a control that is the one writer and nothing else reads, or setters nobody reads, are unused.
            // A binding in a Form fills a field the form sends, so it counts as used.
            Unread = [.. walker.Writes.GroupBy(write => write.State, StringComparer.Ordinal)
                .Where(group => !walker.Reads.Contains(group.Key) && !group.Any(write => write.InForm))
                .Where(group => group.Count(write => write.IsBinding) switch
                {
                    0 => true,
                    1 => group.All(write => write.IsBinding),
                    _ => false,
                })
                .Select(group => (group.Key, group.First().Statement))];
        }

        /// <summary>Each <c>$variable</c> something writes and nothing reads, with the first statement that writes it.</summary>
        public IReadOnlyList<(string State, string Statement)> Unread { get; }

        /// <summary>
        /// "null" when the variable is undeclared or declared null (spec §5.1), "empty" when declared as "",
        /// and null when it starts with a value.
        /// </summary>
        public string? StartsBlank(string state) =>
            !_byName.TryGetValue(state, out var declaration) ? "null"
            : declaration.Value is not LiteralSyntax literal ? null
            : literal.Value.IsNull ? "null"
            : literal.Value.TryGetString(out var text) && text.Length == 0 ? "empty"
            : null;

        /// <summary>
        /// The first Query statement other than <paramref name="self"/> that an expression reads, directly or
        /// through value statements, with the reference that led to it.
        /// </summary>
        public (string Source, string Through)? QueryReachedBy(ExpressionSyntax expression, string self)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { self };
            foreach (var reference in Expressions.References(expression))
            {
                if (FindQuery(reference, seen) is { } source)
                {
                    return (source, reference);
                }
            }

            return null;
        }

        private string? FindQuery(string name, HashSet<string> seen)
        {
            if (!seen.Add(name) || !_byName.TryGetValue(name, out var statement))
            {
                return null;
            }

            switch (statement.Value)
            {
                case CallSyntax { Head: CallHeadKind.Component, Name: "Query" }:
                    return name;
                // A Mutation's result changes only when something runs it, which is a choice, not a load order.
                case CallSyntax { Head: CallHeadKind.Component, Name: "Mutation" }:
                    return null;
            }

            foreach (var next in Expressions.References(statement.Value))
            {
                if (FindQuery(next, seen) is { } source)
                {
                    return source;
                }
            }

            return null;
        }

        /// <summary>The <c>$variables</c> an expression reaches through statement references, each with the first reference that led to it.</summary>
        public IEnumerable<(string State, string Through)> StatesThroughReferences(ExpressionSyntax expression)
        {
            var found = new Dictionary<string, string>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reference in Expressions.References(expression))
            {
                Follow(reference, reference, found, seen);
            }

            return found.Select(pair => (pair.Key, pair.Value));
        }

        private void Follow(string name, string through, Dictionary<string, string> found, HashSet<string> seen)
        {
            // A Query or Mutation result isn't state: what it holds changes when it fetches, not when a $variable does.
            if (!seen.Add(name) || !_byName.TryGetValue(name, out var statement) || statement.Value is CallSyntax { Head: CallHeadKind.Component, Name: "Query" or "Mutation" })
            {
                return;
            }

            var states = new HashSet<string>(StringComparer.Ordinal);
            Expressions.States(statement.Value, states);
            foreach (var state in states)
            {
                found.TryAdd(state, through);
            }

            foreach (var next in Expressions.References(statement.Value))
            {
                Follow(next, through, found, seen);
            }
        }

        // The statements a Form's arguments lead to: a binding there fills a form field, which the form sends, so it isn't unused.
        private static HashSet<string> InsideForms(Dictionary<string, StatementSyntax> byName)
        {
            var inside = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            foreach (var statement in byName.Values)
            {
                foreach (var form in Expressions.Calls(statement.Value).Where(call => call is { Head: CallHeadKind.Component, Name: "Form" }))
                {
                    foreach (var reference in form.Arguments.SelectMany(Expressions.References))
                    {
                        queue.Enqueue(reference);
                    }
                }
            }

            while (queue.TryDequeue(out var name))
            {
                if (inside.Add(name) && byName.TryGetValue(name, out var statement))
                {
                    foreach (var reference in Expressions.References(statement.Value))
                    {
                        queue.Enqueue(reference);
                    }
                }
            }

            return inside;
        }
    }

    /// <summary>Sorts every <c>$variable</c> use into a read or a write.</summary>
    private sealed class UseWalker(OpenUILibrary library)
    {
        public HashSet<string> Reads { get; } = new(StringComparer.Ordinal);

        public List<(string State, string Statement, bool InForm, bool IsBinding)> Writes { get; } = [];

        public void Walk(string statement, ExpressionSyntax expression, bool inForm)
        {
            switch (expression)
            {
                case StateReferenceSyntax state:
                    Reads.Add(state.Name);
                    break;
                case BindingAssignmentSyntax assignment:
                    Writes.Add((assignment.Name, statement, inForm, false));
                    Walk(statement, assignment.Value, inForm);
                    break;
                case CallSyntax { Head: CallHeadKind.At, Name: "Set" } set when set.Arguments.Length > 0 && set.Arguments[0] is StateReferenceSyntax target:
                    Writes.Add((target.Name, statement, false, false));
                    foreach (var argument in set.Arguments.Skip(1))
                    {
                        Walk(statement, argument, inForm);
                    }

                    break;
                case CallSyntax { Head: CallHeadKind.At, Name: "Reset" } reset:
                    foreach (var argument in reset.Arguments)
                    {
                        if (argument is StateReferenceSyntax cleared)
                        {
                            Writes.Add((cleared.Name, statement, false, false));
                        }
                        else
                        {
                            Walk(statement, argument, inForm);
                        }
                    }

                    break;
                case CallSyntax call:
                    var spec = call.Head == CallHeadKind.Component && library.TryGetComponent(call.Name, out var component) ? component : null;
                    var nowInForm = inForm || call is { Head: CallHeadKind.Component, Name: "Form" };
                    for (var i = 0; i < call.Arguments.Length; i++)
                    {
                        // A $variable in a bindable prop is the binding (spec §5.2): the control writes it, and showing it isn't a use.
                        if (spec is not null && i < spec.Props.Length && spec.Props[i].Bindable && call.Arguments[i] is StateReferenceSyntax bound)
                        {
                            Writes.Add((bound.Name, statement, nowInForm, true));
                        }
                        else
                        {
                            Walk(statement, call.Arguments[i], nowInForm);
                        }
                    }

                    break;
                default:
                    foreach (var child in Expressions.Children(expression))
                    {
                        Walk(statement, child, inForm);
                    }

                    break;
            }
        }
    }

    /// <summary>Plain walks over the syntax tree.</summary>
    private static class Expressions
    {
        public static IEnumerable<ExpressionSyntax> Children(ExpressionSyntax expression) => expression switch
        {
            ArraySyntax array => array.Items,
            ObjectSyntax entries => entries.Entries.Select(entry => entry.Value),
            CallSyntax call => call.Arguments,
            MemberSyntax member => [member.Target],
            IndexSyntax index => [index.Target, index.Index],
            UnarySyntax unary => [unary.Operand],
            BinarySyntax binary => [binary.Left, binary.Right],
            TernarySyntax ternary => [ternary.Condition, ternary.WhenTrue, ternary.WhenFalse],
            BindingAssignmentSyntax assignment => [assignment.Value],
            _ => [],
        };

        public static IEnumerable<ExpressionSyntax> All(ExpressionSyntax expression) =>
            Children(expression).SelectMany(All).Prepend(expression);

        public static IEnumerable<string> References(ExpressionSyntax expression) =>
            All(expression).OfType<ReferenceSyntax>().Select(reference => reference.Name).Distinct(StringComparer.Ordinal);

        public static IEnumerable<CallSyntax> Calls(ExpressionSyntax expression) => All(expression).OfType<CallSyntax>();

        public static void States(ExpressionSyntax expression, HashSet<string> into)
        {
            foreach (var state in All(expression).OfType<StateReferenceSyntax>())
            {
                into.Add(state.Name);
            }
        }
    }
}
