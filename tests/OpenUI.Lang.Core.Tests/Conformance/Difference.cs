namespace OpenUI.Lang.Core.Tests.Conformance;

/// <summary>One place where an actual result departs from what a fixture expects.</summary>
/// <param name="Path">Where it differs, such as <c>root.props.children[1].props.text</c>.</param>
/// <param name="Problem">What kind of departure it is.</param>
/// <param name="Expected">Compact JSON of the expected side, or null when nothing was expected there.</param>
/// <param name="Actual">Compact JSON of the actual side, or null when nothing was produced there.</param>
internal sealed record Difference(string Path, string Problem, string? Expected, string? Actual)
{
    public Difference Under(string label) => this with { Path = $"{label}: {Path}" };

    public override string ToString() =>
        $"{Path}: {Problem}\n      expected: {Expected ?? "(absent)"}\n      actual:   {Actual ?? "(absent)"}";

    public static string Format(string title, IReadOnlyCollection<Difference> differences) =>
        $"{title}: {differences.Count} difference(s)\n" + string.Join("\n", differences.Select(d => $"  {d}"));
}
