using System.Text;

namespace OpenUI.Lang.Core.Tests.Hostile;

public partial class HostileInputTests
{
    private static string ErrorFlood()
    {
        var program = new StringBuilder("root = Stack([Text(\"page\")])\n");
        for (var i = 0; i < 15; i++)
        {
            program.Append('r').Append(i).Append(" = Select(\"n\", ").Append(BigArray(125_000)).Append(")\n");
        }

        return program.ToString();
    }

    [Fact]
    public async Task A_flood_of_literal_errors_is_cut_at_the_error_limit_with_a_marker()
    {
        var report = await HostileProbe.SurvivesAsync("parse", ErrorFlood());
        Assert.True(report["errorCount"]!.GetValue<int>() <= OpenUILimits.Default.MaxErrors + 1, report["errorCount"]!.ToJsonString());
        Assert.Contains(LimitCode, HostileProbe.Codes(report));
    }

    [Fact]
    public void One_bad_list_is_cut_at_the_error_limit_with_a_marker()
    {
        var document = new OpenUIParser(Library).Parse("root = Select(\"n\", " + BigArray(5_000) + ")\n");
        Assert.True(document.Errors.Count <= OpenUILimits.Default.MaxErrors + 1, document.Errors.Count.ToString());
        Assert.Equal(LimitCode, document.Errors[^1].Code.Value);
    }
}
