namespace OpenUI.Lang.Core.Tests.Hostile;

public partial class HostileInputTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    public async Task A_self_referencing_schema_does_not_expand_exponentially(int branches)
    {
        var options = string.Join(",", Enumerable.Repeat("{\"$ref\":\"#/$defs/A\"}", branches));
        var spec = "{\"components\":{\"Box\":{\"signature\":\"Box(a: any)\"}},\"schema\":{\"properties\":{\"Box\":{\"$ref\":\"#/$defs/Box\"}},\"$defs\":{\"Box\":{\"properties\":{\"a\":{\"$ref\":\"#/$defs/A\"}}},\"A\":{\"anyOf\":[" + options + ",{\"type\":\"string\"}]}}}}";
        var report = await HostileProbe.SurvivesAsync("schema", spec, TimeSpan.FromSeconds(15));
        Assert.Equal(1, report["components"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_wide_schema_without_a_cycle_stops_at_the_node_budget()
    {
        // Two branches per level, 28 levels: no cycle to detect, 2^28 nodes when expanded.
        var defs = string.Concat(Enumerable.Range(0, 28).Select(i => $"\"D{i}\":{{\"anyOf\":[{{\"$ref\":\"#/$defs/D{i + 1}\"}},{{\"$ref\":\"#/$defs/D{i + 1}\"}}]}},"));
        var spec = "{\"components\":{\"Box\":{\"signature\":\"Box(a: any)\"}},\"schema\":{\"properties\":{\"Box\":{\"$ref\":\"#/$defs/Box\"}},\"$defs\":{" + defs + "\"D28\":{\"type\":\"string\"},\"Box\":{\"properties\":{\"a\":{\"$ref\":\"#/$defs/D0\"}}}}}}";
        var report = await HostileProbe.SurvivesAsync("schema", spec, TimeSpan.FromSeconds(15));
        Assert.Equal(1, report["components"]!.GetValue<int>());
    }
}
