using System.Text.Json;
using OpenUI.Lang.Prompting;

namespace OpenUI.Lang.Core.Tests.Prompt;

/// <summary>
/// The JavaScript behavior the tool sections rely on. Expected values were produced by running the
/// same input through Bun (<c>Object.keys</c>, <c>JSON.stringify</c>, template literals).
/// </summary>
public sealed class ToolSchemaTextTests
{
    [Fact]
    public void Properties_enumerate_array_indices_first_like_javascript()
    {
        var properties = Json("""{"b":1,"10":2,"2":3,"01":4,"4294967295":5,"4294967294":6,"a":7}""");

        Assert.Equal(
            ["2", "10", "4294967294", "b", "01", "4294967295", "a"],
            ToolSchemaText.JsOrder(properties).Select(property => property.Name));
    }

    [Fact]
    public void Default_keys_are_escaped_like_JSON_stringify()
    {
        var keys = new[] { "hi😀", "c\u001f\u0000", "q\"\\\b\f\n\r\t", "\u007f\u2028" };
        var properties = string.Join(",", keys.Select(key => $"{JsonSerializer.Serialize(key)}:{{\"type\":\"string\"}}"));

        var json = ToolSchemaText.DefaultJson(Json("""{"type":"object","properties":{""" + properties + "}}"));

        Assert.Equal(
            "{\"hi😀\":\"\",\"c\\u001f\\u0000\":\"\",\"q\\\"\\\\\\b\\f\\n\\r\\t\":\"\",\"\u007f\u2028\":\"\"}",
            json);
    }

    // System.Text.Json cannot hold a lone surrogate, so the escaping is checked on the string itself.
    [Fact]
    public void Lone_surrogates_are_escaped_like_JSON_stringify() =>
        Assert.Equal("\"lo\\ud800ne\\udfff😀\"", ToolSchemaText.Quote("lo\ud800ne\udfff😀"));

    [Fact]
    public void Defaults_follow_the_schema_type()
    {
        var schema = Json("""
            {"type":"object","properties":{
              "s":{"type":"string"},"n":{"type":"integer"},"b":{"type":"boolean"},"a":{"type":"array","items":{"type":"string"}},
              "o":{"type":"object"},"p":{"type":"object","properties":{"x":{"type":"number"}}},"u":{},"t":{"type":["string","null"]}}}
            """);

        Assert.Equal("""{"s":"","n":0,"b":false,"a":[],"o":{},"p":{"x":0},"u":null,"t":null}""", ToolSchemaText.DefaultJson(schema));
    }

    [Fact]
    public void The_proto_key_is_not_part_of_the_defaults() =>
        Assert.Equal(
            """{"id":""}""",
            ToolSchemaText.DefaultJson(Json("""{"type":"object","properties":{"__proto__":{"type":"string"},"id":{"type":"string"}}}""")));

    [Theory]
    [InlineData("""{"type":"string"}""", "string")]
    [InlineData("""{"type":"string","enum":["a","b"]}""", "\"a\" | \"b\"")]
    [InlineData("""{"type":"string","enum":[]}""", "")]
    [InlineData("""{"type":"string","enum":[1,true,null,"x",1.5]}""", "\"1\" | \"true\" | \"null\" | \"x\" | \"1.5\"")]
    [InlineData("""{"type":"integer"}""", "number")]
    [InlineData("""{"type":"array"}""", "any[]")]
    [InlineData("""{"type":"array","items":{"type":"array","items":{"type":"boolean"}}}""", "boolean[][]")]
    [InlineData("""{"type":"object"}""", "object")]
    [InlineData("""{"type":"object","properties":{}}""", "object")]
    [InlineData("""{"type":"object","properties":{"a":{"type":"string"},"b":{"type":"number"}},"required":["b"]}""", "{a?: string, b: number}")]
    [InlineData("""{"type":["string","null"]}""", "any")]
    [InlineData("""{"anyOf":[{"type":"string"}]}""", "any")]
    [InlineData("true", "any")]
    public void Types_print_the_way_the_prompt_shows_them(string schema, string expected) =>
        Assert.Equal(expected, ToolSchemaText.TypeString(Json(schema)));

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
