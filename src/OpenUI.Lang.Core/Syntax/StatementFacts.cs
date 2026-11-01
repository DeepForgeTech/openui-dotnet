using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace OpenUI.Lang.Syntax;

/// <summary>
/// What can be read from a statement's syntax alone, with no library: its kind, the statements it
/// references and the state names it uses. The edit merge (language.md, section 7) relies on this.
/// </summary>
internal sealed record StatementFacts(StatementKind Kind, ImmutableArray<string> References, ImmutableArray<string> StateNames)
{
    public static StatementFacts Of(StatementSyntax statement)
    {
        var collector = new Collector();
        collector.Visit(statement.Value, ImmutableHashSet<string>.Empty);
        return new StatementFacts(KindOf(statement), [.. collector.References], [.. collector.StateNames]);
    }

    /// <summary>Classifies in the order of section 2.1.</summary>
    public static StatementKind KindOf(StatementSyntax statement) => statement.Value switch
    {
        CallSyntax { Head: CallHeadKind.Component, Name: "Query" } => StatementKind.Query,
        CallSyntax { Head: CallHeadKind.Component, Name: "Mutation" } => StatementKind.Mutation,
        _ when statement.Name.StartsWith('$') => StatementKind.State,
        _ => StatementKind.Value,
    };

    /// <summary>The <c>$names</c> written literally in <paramref name="expression"/>, the ones that retrigger a query (section 6.1).</summary>
    public static ImmutableArray<string> StateNamesIn(ExpressionSyntax expression)
    {
        var collector = new Collector();
        collector.Visit(expression, ImmutableHashSet<string>.Empty);
        return [.. collector.StateNames];
    }

    // References in source order, each once. An @Each loop variable shadows a statement of the same
    // name inside its template only (section 3.5).
    private sealed class Collector
    {
        private readonly HashSet<string> _seenReferences = new(StringComparer.Ordinal);
        private readonly HashSet<string> _seenStates = new(StringComparer.Ordinal);

        public List<string> References { get; } = [];

        public List<string> StateNames { get; } = [];

        public void Visit(ExpressionSyntax expression, ImmutableHashSet<string> loopNames)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();
            switch (expression)
            {
                case LiteralSyntax:
                    break;
                case ReferenceSyntax reference:
                    if (!loopNames.Contains(reference.Name) && _seenReferences.Add(reference.Name))
                    {
                        References.Add(reference.Name);
                    }

                    break;
                case StateReferenceSyntax state:
                    AddState(state.Name);
                    break;
                case BindingAssignmentSyntax binding:
                    AddState(binding.Name);
                    Visit(binding.Value, loopNames);
                    break;
                case ArraySyntax array:
                    VisitAll(array.Items, loopNames);
                    break;
                case ObjectSyntax obj:
                    foreach (var entry in obj.Entries)
                    {
                        Visit(entry.Value, loopNames);
                    }

                    break;
                case CallSyntax { Head: CallHeadKind.At, Name: "Each", Arguments: [var items, LiteralSyntax loopName, var template, ..] }
                    when loopName.Value.TryGetString(out var name):
                    Visit(items, loopNames);
                    Visit(template, loopNames.Add(name));
                    break;
                case CallSyntax call:
                    VisitAll(call.Arguments, loopNames);
                    break;
                case MemberSyntax member:
                    Visit(member.Target, loopNames);
                    break;
                case IndexSyntax index:
                    Visit(index.Target, loopNames);
                    Visit(index.Index, loopNames);
                    break;
                case UnarySyntax unary:
                    Visit(unary.Operand, loopNames);
                    break;
                case BinarySyntax binary:
                    Visit(binary.Left, loopNames);
                    Visit(binary.Right, loopNames);
                    break;
                case TernarySyntax ternary:
                    Visit(ternary.Condition, loopNames);
                    Visit(ternary.WhenTrue, loopNames);
                    Visit(ternary.WhenFalse, loopNames);
                    break;
                default:
                    throw new InvalidOperationException($"Unhandled expression kind {expression.Kind}.");
            }
        }

        private void VisitAll(ImmutableArray<ExpressionSyntax> expressions, ImmutableHashSet<string> loopNames)
        {
            foreach (var expression in expressions)
            {
                Visit(expression, loopNames);
            }
        }

        private void AddState(string name)
        {
            if (_seenStates.Add(name))
            {
                StateNames.Add(name);
            }
        }
    }
}
