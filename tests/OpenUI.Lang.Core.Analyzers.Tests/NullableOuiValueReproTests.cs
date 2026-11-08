using System.Collections.Immutable;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Analyzers.Tests;

/// <summary>
/// What the C# compiler does with a null reference on its way to <c>OuiValue?</c>, which the analyzer
/// exists to flag. These run the shapes; the analyzer tests compile the same ones.
/// </summary>
public sealed class NullableOuiValueReproTests
{
    // A host's seeding helper in the shape that caused the bug: "no picks" was meant to be a null OuiValue?.
    // The conditional's type is OuiArray, so the null is an OuiArray, which converts to OuiValue.Null.
    private static OuiValue? SeedPicks(ImmutableArray<OuiValue> kept) => kept.IsEmpty ? null : OuiArray.Create(kept);

    private static OuiValue? FromText(string? text) => text;

    private static OuiValue? Nothing() => null;

    [Fact]
    public void A_conditional_whose_other_branch_is_an_array_returns_OuiValue_Null_with_a_value()
    {
        var seeded = SeedPicks([]);

        Assert.True(seeded.HasValue);
        Assert.True(seeded.Value.IsNull);
    }

    [Fact]
    public void A_null_string_returns_OuiValue_Null_with_a_value()
    {
        var value = FromText(null);

        Assert.True(value.HasValue);
        Assert.True(value.Value.IsNull);
    }

    [Fact]
    public void A_bare_null_literal_is_a_null_OuiValue()
    {
        Assert.False(Nothing().HasValue);
    }
}
