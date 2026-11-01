namespace OpenUI.Lang.Core.Tests.Hostile;

public partial class HostileInputTests
{
    [Theory]
    [InlineData("parse")]
    [InlineData("session")]
    [InlineData("merge")]
    [InlineData("edit")]
    public async Task A_long_binding_chain_does_not_overflow_the_stack(string scenario)
    {
        var report = await HostileProbe.SurvivesAsync(scenario, "root = " + Repeat("$a=", 80_000) + "0");
        if (scenario is "parse" or "session")
        {
            Assert.Contains(LimitCode, HostileProbe.Codes(report));
        }
    }
}
