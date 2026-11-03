using System.Runtime.CompilerServices;
using OpenUI.Lang.Library;
using OpenUI.Lang.Syntax;

namespace OpenUI.Lang.Semantics;

/// <summary>What a program writes and reads, worked out once from its statements, for <see cref="OpenUIWiringCheck"/>.</summary>
internal sealed class WiringFacts
{
    private readonly Dictionary<string, StatementSyntax> _byName;

    public WiringFacts(OpenUILibrary library, IReadOnlyList<StatementSyntax> statements)
    {
        _byName = new Dictionary<string, StatementSyntax>(StringComparer.Ordinal);
        foreach (var statement in statements)
        {
            // A later statement of the same name wins, as in the program itself (section 2.4).
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
    /// "null" when the variable is undeclared or declared null (section 5.1), "empty" when declared as "",
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
        foreach (var reference in StatementFacts.ReferencesIn(expression))
        {
            if (FindQuery(reference, seen) is { } source)
            {
                return (source, reference);
            }
        }

        return null;
    }

    /// <summary>The <c>$variables</c> an expression reaches through statement references, each with the first reference that led to it.</summary>
    public IEnumerable<(string State, string Through)> StatesThroughReferences(ExpressionSyntax expression)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in StatementFacts.ReferencesIn(expression))
        {
            Follow(reference, reference, found, seen);
        }

        return found.Select(pair => (pair.Key, pair.Value));
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

        foreach (var next in StatementFacts.ReferencesIn(statement.Value))
        {
            if (FindQuery(next, seen) is { } source)
            {
                return source;
            }
        }

        return null;
    }

    private void Follow(string name, string through, Dictionary<string, string> found, HashSet<string> seen)
    {
        // A Query or Mutation result isn't state: what it holds changes when it fetches, not when a $variable does.
        if (!seen.Add(name) || !_byName.TryGetValue(name, out var statement) || statement.Value is CallSyntax { Head: CallHeadKind.Component, Name: "Query" or "Mutation" })
        {
            return;
        }

        foreach (var state in StatementFacts.StateNamesIn(statement.Value))
        {
            found.TryAdd(state, through);
        }

        foreach (var next in StatementFacts.ReferencesIn(statement.Value))
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
            foreach (var form in Calls(statement.Value).Where(call => call is { Head: CallHeadKind.Component, Name: "Form" }))
            {
                foreach (var reference in form.Arguments.SelectMany(argument => StatementFacts.ReferencesIn(argument)))
                {
                    queue.Enqueue(reference);
                }
            }
        }

        while (queue.TryDequeue(out var name))
        {
            if (inside.Add(name) && byName.TryGetValue(name, out var statement))
            {
                foreach (var reference in StatementFacts.ReferencesIn(statement.Value))
                {
                    queue.Enqueue(reference);
                }
            }
        }

        return inside;
    }

    private static List<CallSyntax> Calls(ExpressionSyntax expression)
    {
        var calls = new List<CallSyntax>();
        var pending = new Stack<ExpressionSyntax>();
        pending.Push(expression);
        while (pending.TryPop(out var next))
        {
            if (next is CallSyntax call)
            {
                calls.Add(call);
            }

            foreach (var child in Children(next))
            {
                pending.Push(child);
            }
        }

        return calls;
    }

    private static IEnumerable<ExpressionSyntax> Children(ExpressionSyntax expression) => expression switch
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

    /// <summary>Sorts every <c>$variable</c> use into a read or a write.</summary>
    private sealed class UseWalker(OpenUILibrary library)
    {
        public HashSet<string> Reads { get; } = new(StringComparer.Ordinal);

        public List<(string State, string Statement, bool InForm, bool IsBinding)> Writes { get; } = [];

        public void Walk(string statement, ExpressionSyntax expression, bool inForm)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();
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
                    WalkCall(statement, call, inForm);
                    break;
                default:
                    foreach (var child in Children(expression))
                    {
                        Walk(statement, child, inForm);
                    }

                    break;
            }
        }

        private void WalkCall(string statement, CallSyntax call, bool inForm)
        {
            var spec = call.Head == CallHeadKind.Component && library.TryGetComponent(call.Name, out var component) ? component : null;
            var nowInForm = inForm || call is { Head: CallHeadKind.Component, Name: "Form" };
            for (var i = 0; i < call.Arguments.Length; i++)
            {
                // A $variable in a bindable prop is the binding (section 5.2): the control writes it, and showing it isn't a use.
                if (spec is not null && i < spec.Props.Length && spec.Props[i].Bindable && call.Arguments[i] is StateReferenceSyntax bound)
                {
                    Writes.Add((bound.Name, statement, nowInForm, true));
                }
                else
                {
                    Walk(statement, call.Arguments[i], nowInForm);
                }
            }
        }
    }
}
