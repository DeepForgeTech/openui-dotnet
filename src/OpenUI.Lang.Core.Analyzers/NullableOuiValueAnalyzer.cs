using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace OpenUI.Lang.Analyzers;

/// <summary>
/// Flags a reference that may be null turning into an <c>OuiValue?</c> through one of <c>OuiValue</c>'s
/// implicit conversions. A null <c>string</c>, <c>OuiArray</c>, <c>OuiObject</c>, <c>OuiComponent</c> or
/// <c>OuiAction</c> converts to <c>OuiValue.Null</c>, and that is then wrapped, so the result has
/// <c>HasValue</c> true where the code reads as if it were a null <c>OuiValue?</c>. The usual shape is
/// <c>return picks.IsEmpty ? null : OuiArray.Create(picks);</c> in a method returning <c>OuiValue?</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NullableOuiValueAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The rule's ID.</summary>
    public const string DiagnosticId = "OUI0001";

    private const string OuiValueName = "OpenUI.Lang.Values.OuiValue";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "A null reference becomes OuiValue.Null, not a null OuiValue?",
        messageFormat: "This {0} may be null, and a null {0} becomes OuiValue.Null, so the OuiValue? has a value (HasValue is true). Return or assign null to the OuiValue? directly, or write OuiValue.Null if that is what you mean.",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "OuiValue converts implicitly from string?, OuiArray?, OuiObject?, OuiComponent? and OuiAction?, and a null one becomes OuiValue.Null. Converting such a value to OuiValue? goes through that conversion first, so null never reaches the Nullable<OuiValue>.");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (start.Compilation.GetTypeByMetadataName(OuiValueName) is { } ouiValue)
            {
                start.RegisterOperationAction(operation => Analyze(operation, ouiValue), OperationKind.Conversion);
            }
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol ouiValue)
    {
        var conversion = (IConversionOperation)context.Operation;
        if (!IsNullableOf(conversion.Type, ouiValue) || FromReference(conversion, ouiValue) is not { } source)
        {
            return;
        }

        var flowState = source.SemanticModel?.GetTypeInfo(source.Syntax, context.CancellationToken).Nullability.FlowState;
        if (flowState == NullableFlowState.NotNull)
        {
            return;
        }

        var type = source.Type!.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        context.ReportDiagnostic(Diagnostic.Create(Rule, source.Syntax.GetLocation(), type));
    }

    // Roslyn may give the user-defined conversion and the wrap into Nullable<T> as one conversion or as
    // two nested ones, so both shapes lead to the operand that went through OuiValue's operator.
    private static IOperation? FromReference(IConversionOperation conversion, INamedTypeSymbol ouiValue)
    {
        if (ThroughOuiValueOperator(conversion, ouiValue))
        {
            return conversion.Operand;
        }

        return conversion.Operand is IConversionOperation inner && SymbolEqualityComparer.Default.Equals(inner.Type, ouiValue) && ThroughOuiValueOperator(inner, ouiValue)
            ? inner.Operand
            : null;
    }

    private static bool ThroughOuiValueOperator(IConversionOperation conversion, INamedTypeSymbol ouiValue) =>
        conversion.Conversion.IsUserDefined
        && conversion.Conversion.MethodSymbol is { Parameters.Length: 1 } method
        && SymbolEqualityComparer.Default.Equals(method.ContainingType, ouiValue)
        && method.Parameters[0].Type.IsReferenceType
        && conversion.Operand.Type is { IsReferenceType: true };

    private static bool IsNullableOf(ITypeSymbol? type, INamedTypeSymbol ouiValue) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
        && SymbolEqualityComparer.Default.Equals(nullable.TypeArguments[0], ouiValue);
}
