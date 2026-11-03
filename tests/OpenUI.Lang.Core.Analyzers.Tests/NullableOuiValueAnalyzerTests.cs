using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using OpenUI.Lang.Analyzers;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Analyzers.Tests;

/// <summary>OUI0001: a reference that may be null reaching <c>OuiValue?</c> through an implicit conversion.</summary>
public sealed class NullableOuiValueAnalyzerTests
{
    private static readonly ImmutableArray<MetadataReference> References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)),
        MetadataReference.CreateFromFile(typeof(OuiValue).Assembly.Location),
    ];

    // Compiles one class body and returns OUI0001's findings, failing on any compile error so a broken snippet can't pass.
    private static async Task<ImmutableArray<Diagnostic>> Analyze(string members)
    {
        var source = $$"""
            #nullable enable
            using System.Collections.Immutable;
            using OpenUI.Lang.Values;

            public static class Host
            {
            {{members}}
            }
            """;
        var compilation = CSharpCompilation.Create(
            "Host",
            [CSharpSyntaxTree.ParseText(source)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var diagnostics = await compilation.WithAnalyzers([new NullableOuiValueAnalyzer()]).GetAnalyzerDiagnosticsAsync();
        return diagnostics;
    }

    private static string Flagged(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    [Fact]
    public async Task A_conditional_of_null_and_an_array_returned_as_OuiValue_nullable_is_flagged()
    {
        var diagnostic = Assert.Single(await Analyze("""
                public static OuiValue? SeedPicks(ImmutableArray<OuiValue> kept) => kept.IsEmpty ? null : OuiArray.Create(kept);
            """));

        Assert.Equal("OUI0001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("kept.IsEmpty ? null : OuiArray.Create(kept)", Flagged(diagnostic));
        Assert.StartsWith("This OuiArray may be null, and a null OuiArray becomes OuiValue.Null", diagnostic.GetMessage());
    }

    [Fact]
    public async Task A_nullable_string_returned_as_OuiValue_nullable_is_flagged()
    {
        var diagnostic = Assert.Single(await Analyze("""
                public static OuiValue? FromText(string? text) => text;
            """));

        Assert.Equal("text", Flagged(diagnostic));
        Assert.StartsWith("This string may be null", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Assigning_passing_and_casting_a_maybe_null_reference_are_flagged()
    {
        var diagnostics = await Analyze("""
                public static void Take(OuiValue? value) { }

                public static void Use(OuiObject? row, OuiComponent? card, OuiAction? action)
                {
                    OuiValue? local = row;
                    Take(card);
                    var cast = (OuiValue?)action;
                }
            """);

        Assert.Equal(["row", "card", "action"], diagnostics.Select(Flagged));
    }

    [Fact]
    public async Task Code_without_nullable_annotations_is_flagged_because_it_can_t_say_the_value_is_set()
    {
        var diagnostic = Assert.Single(await Analyze("""
            #nullable disable
                public static OuiValue? FromText(string text) => text;
            #nullable enable
            """));

        Assert.Equal("text", Flagged(diagnostic));
    }

    [Fact]
    public async Task Values_that_can_t_be_null_or_targets_that_aren_t_nullable_are_not_flagged()
    {
        Assert.Empty(await Analyze("""
                public static OuiValue? Nothing() => null;
                public static OuiValue? Literal() => "north";
                public static OuiValue? Known(string text) => text;
                public static OuiValue? Checked(string? text) => text is null ? default(OuiValue?) : text;
                public static OuiValue? Number(double value) => value;
                public static OuiValue? Already(OuiValue value) => value;
                public static OuiValue Plain(string? text) => text;
            """));
    }
}
