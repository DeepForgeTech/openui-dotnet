using System.Runtime.CompilerServices;
using OpenUI.Lang.Library;
using OpenUI.Lang.Syntax;

namespace OpenUI.Lang.Semantics;

/// <summary>What a program writes and reads, worked out once from its statements, for <see cref="OpenUIWiringCheck"/>.</summary>
internal sealed class WiringFacts
{
    private readonly Dictionary<string, StatementSyntax> _byName;
    private readonly Dictionary<string, Reach> _reach;

    public WiringFacts(OpenUILibrary library, IReadOnlyList<StatementSyntax> statements)
    {
        _byName = new Dictionary<string, StatementSyntax>(StringComparer.Ordinal);
        foreach (var statement in statements)
        {
            // A later statement of the same name wins, as in the program itself (section 2.4).
            _byName[statement.Name] = statement;
        }

        _reach = Reachability(_byName);
        var inForm = InsideForms(_byName);
        var walker = new UseWalker(library);
        foreach (var statement in _byName.Values)
        {
            walker.Walk(statement.Name, statement.Value, inForm.Contains(statement.Name));
        }

        // A bound control shows its value, so it reads what anything else writes: a Modal opens on what a button sets.
        // Only a control that is the one writer and nothing else reads, or setters nobody reads, are unused.
        // A binding in a Form fills a field the form sends, so it counts as used. An event outside a form
        // carries every page $variable to the host, with what an earlier @Set wrote (section 6.3), so with
        // one of those nothing written outside a form goes unread.
        Unread = walker.SendsPageState ? [] : [.. walker.Writes.GroupBy(write => write.State, StringComparer.Ordinal)
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
        foreach (var reference in StatementFacts.ReferencesIn(expression))
        {
            if (_reach.TryGetValue(reference, out var reach) && reach.Queries.FirstOrDefault(query => query != self) is { } source)
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
        foreach (var reference in StatementFacts.ReferencesIn(expression))
        {
            if (_reach.TryGetValue(reference, out var reach))
            {
                foreach (var state in reach.States)
                {
                    found.TryAdd(state, reference);
                }
            }
        }

        return found.Select(pair => (pair.Key, pair.Value));
    }

    /// <summary>
    /// What each statement reaches through statement references, worked out once so a program of many
    /// queries over one long chain costs one walk, not one per query. The walk is iterative (Tarjan's
    /// strongly connected components), so a chain as long as the statement limit can't exhaust the stack.
    /// A Query or Mutation ends a path: a Query's result changes when it fetches, not when a $variable does,
    /// and a Mutation's only when something runs it, which is a choice, not a load order.
    /// </summary>
    private static Dictionary<string, Reach> Reachability(Dictionary<string, StatementSyntax> byName)
    {
        var names = byName.Keys.ToArray();
        var ids = new Dictionary<string, int>(names.Length, StringComparer.Ordinal);
        for (var i = 0; i < names.Length; i++)
        {
            ids[names[i]] = i;
        }

        var edges = new int[names.Length][];
        for (var i = 0; i < names.Length; i++)
        {
            edges[i] = byName[names[i]].Value is CallSyntax { Head: CallHeadKind.Component, Name: "Query" or "Mutation" }
                ? []
                : [.. StatementFacts.ReferencesIn(byName[names[i]].Value).Where(ids.ContainsKey).Select(name => ids[name])];
        }

        var index = new int[names.Length];
        var low = new int[names.Length];
        var component = new int[names.Length];
        Array.Fill(index, -1);
        Array.Fill(component, -1);
        var reaches = new Reach[names.Length];
        var open = new Stack<int>();
        var work = new Stack<(int Node, int Next)>();
        var counter = 0;
        var components = 0;
        for (var root = 0; root < names.Length; root++)
        {
            if (index[root] >= 0)
            {
                continue;
            }

            index[root] = low[root] = counter++;
            open.Push(root);
            work.Push((root, 0));
            while (work.TryPop(out var frame))
            {
                var (node, next) = frame;
                if (next < edges[node].Length)
                {
                    work.Push((node, next + 1));
                    var target = edges[node][next];
                    if (index[target] < 0)
                    {
                        index[target] = low[target] = counter++;
                        open.Push(target);
                        work.Push((target, 0));
                    }
                    else if (component[target] < 0)
                    {
                        low[node] = Math.Min(low[node], index[target]);
                    }

                    continue;
                }

                if (work.TryPeek(out var parent))
                {
                    low[parent.Node] = Math.Min(low[parent.Node], low[node]);
                }

                if (low[node] == index[node])
                {
                    var members = new List<int>();
                    int member;
                    do
                    {
                        member = open.Pop();
                        component[member] = components;
                        members.Add(member);
                    }
                    while (member != node);

                    members.Reverse();
                    var reach = ReachOf(members, components++);
                    foreach (var done in members)
                    {
                        reaches[done] = reach;
                    }
                }
            }
        }

        var result = new Dictionary<string, Reach>(names.Length, StringComparer.Ordinal);
        for (var i = 0; i < names.Length; i++)
        {
            result[names[i]] = reaches[i];
        }

        return result;

        // Every statement reachable from a component reaches the component's own, and its successors were finished first.
        Reach ReachOf(List<int> members, int self)
        {
            var own = new List<string>();
            var queries = new List<string>(2);
            var outside = new List<Reach>();
            foreach (var member in members)
            {
                switch (byName[names[member]].Value)
                {
                    case CallSyntax { Head: CallHeadKind.Component, Name: "Query" }:
                        queries.Add(names[member]);
                        break;
                    case CallSyntax { Head: CallHeadKind.Component, Name: "Mutation" }:
                        break;
                    case var value:
                        own.AddRange(StatementFacts.StateNamesIn(value));
                        break;
                }

                outside.AddRange(edges[member].Where(target => component[target] != self).Select(target => reaches[target]));
            }

            // A plain link in a chain reaches exactly what the next statement does, so it shares that answer.
            if (own.Count == 0 && queries.Count == 0 && outside.Distinct().Count() == 1)
            {
                return outside[0];
            }

            var states = new List<string>(own);
            var seen = new HashSet<string>(own, StringComparer.Ordinal);
            foreach (var next in outside)
            {
                states.AddRange(next.States.Where(seen.Add));

                // Two distinct queries are enough: a query may be told about any but itself.
                queries.AddRange(next.Queries.Where(query => queries.Count < 2 && !queries.Contains(query)));
            }

            return new Reach([.. queries.Take(2)], [.. states]);
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

    /// <summary>
    /// What a statement reaches through references: the first two distinct Query statements, and the
    /// <c>$variables</c> written in the value statements on the way, in the order they are found.
    /// </summary>
    private sealed record Reach(string[] Queries, string[] States);

    /// <summary>Sorts every <c>$variable</c> use into a read or a write, and notes an event that carries the page's state.</summary>
    private sealed class UseWalker(OpenUILibrary library)
    {
        public HashSet<string> Reads { get; } = new(StringComparer.Ordinal);

        public List<(string State, string Statement, bool InForm, bool IsBinding)> Writes { get; } = [];

        /// <summary>
        /// True when something outside a form can send an event: @ToAssistant, @OpenUrl, a custom action, or
        /// a component left without its action, which a renderer may give a default one. Each event carries
        /// the page's $variables to the host (section 6.3).
        /// </summary>
        public bool SendsPageState { get; private set; }

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
                    if (!inForm && call.Head == CallHeadKind.At && (call.Name is "ToAssistant" or "OpenUrl" || library.TryGetAction(call.Name, out _)))
                    {
                        SendsPageState = true;
                    }

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
            if (!nowInForm && spec is not null && LeavesOutItsAction(spec, call))
            {
                SendsPageState = true;
            }

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

        // The component's first action prop, missing or null: the reference Button then sends continue_conversation
        // with its label (section 6.3), and other renderers may give their own components a default the same way.
        private static bool LeavesOutItsAction(ComponentSpec spec, CallSyntax call)
        {
            for (var slot = 0; slot < spec.Props.Length; slot++)
            {
                if (spec.Props[slot].Type.Kind == TypeShapeKind.ActionSlot)
                {
                    return slot >= call.Arguments.Length || call.Arguments[slot] is LiteralSyntax { Value.IsNull: true };
                }
            }

            return false;
        }
    }
}
