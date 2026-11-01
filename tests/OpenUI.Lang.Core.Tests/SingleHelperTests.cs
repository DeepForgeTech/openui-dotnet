using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests;

public class SingleHelperTests
{
    [Fact]
    public void White_space_has_one_definition()
    {
        Assert.Null(typeof(OuiValue).Assembly.GetType("OpenUI.Lang.Syntax.TextScanner"));
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("10", true)]
    [InlineData("4294967294", true)]
    [InlineData("4294967295", false)]
    [InlineData("01", false)]
    [InlineData("", false)]
    [InlineData("1a", false)]
    public void Tool_schema_properties_and_objects_agree_on_what_an_array_index_is(string key, bool expected)
    {
        Assert.Equal(expected, OuiObject.IsArrayIndex(key, out _));
        using var schema = System.Text.Json.JsonDocument.Parse($$"""{"z":1,"{{key}}":2}""");
        var first = OpenUI.Lang.Prompting.ToolSchemaText.JsOrder(schema.RootElement).First().Name;
        Assert.Equal(expected ? key : "z", first);
    }
}
