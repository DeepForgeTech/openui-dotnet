using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Library;

internal sealed record HostRow(string Name, double Count, OuiValue Extra);

[JsonSerializable(typeof(HostRow))]
[JsonSerializable(typeof(OuiValue))]
internal sealed partial class HostJson : JsonSerializerContext;

/// <summary>An <see cref="OuiValue"/> crossing into and out of host JSON (language.md, section 3.1).</summary>
public sealed class OuiValueJsonTests
{
    private const string Shape = """{"a":[1,"x",null,true],"b":{"c":2}}""";

    private static string Write(OuiValue value)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            value.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    [Fact]
    public void From_converts_a_host_value_through_its_json_form()
    {
        var value = OuiValue.From(new HostRow("n", 2, OuiValue.Null), HostJson.Default.HostRow);

        Assert.True(value.TryGetObject(out var row));
        Assert.Equal((OuiValue)"n", row["Name"]);
        Assert.Equal((OuiValue)2.0, row["Count"]);
        Assert.True(row["Extra"].IsNull);
    }

    [Fact]
    public void As_converts_back_to_the_host_type()
    {
        using var document = JsonDocument.Parse("""{"Name":"n","Count":2,"Extra":[1]}""");

        var row = OuiValue.FromJson(document.RootElement).As(HostJson.Default.HostRow);

        Assert.NotNull(row);
        Assert.Equal("n", row.Name);
        Assert.Equal(2, row.Count);
        Assert.Equal("[1]", Write(row.Extra));
    }

    [Fact]
    public void ReadFrom_reads_one_value_and_leaves_the_reader_on_its_last_token()
    {
        var reader = new Utf8JsonReader("""[{"a":1},2]"""u8);
        Assert.True(reader.Read());
        Assert.True(reader.Read());

        var value = OuiValue.ReadFrom(ref reader);

        Assert.Equal("""{"a":1}""", Write(value));
        Assert.Equal(JsonTokenType.EndObject, reader.TokenType);
        Assert.True(reader.Read());
        Assert.Equal(2, reader.GetInt32());
    }

    [Fact]
    public void Serializing_a_value_writes_its_json_rather_than_its_members()
    {
        var value = JsonSerializer.Deserialize<OuiValue>(Shape);

        Assert.Equal(Shape, Write(value));
        Assert.Equal(Shape, JsonSerializer.Serialize(value));
    }

    [Fact]
    public void A_value_inside_a_host_type_survives_a_round_trip_through_generated_serialization()
    {
        var json = """{"Name":"n","Count":1,"Extra":""" + Shape + "}";

        var row = JsonSerializer.Deserialize(json, HostJson.Default.HostRow);

        Assert.NotNull(row);
        Assert.Equal(Shape, Write(row.Extra));
        Assert.Equal(json, JsonSerializer.Serialize(row, HostJson.Default.HostRow));
    }

    [Fact]
    public void A_json_null_inside_a_host_type_is_the_null_value()
    {
        var row = JsonSerializer.Deserialize("""{"Name":"n","Count":1,"Extra":null}""", HostJson.Default.HostRow);

        Assert.NotNull(row);
        Assert.True(row.Extra.IsNull);
        Assert.Contains("\"Extra\":null", JsonSerializer.Serialize(row, HostJson.Default.HostRow), StringComparison.Ordinal);
    }

    [Fact]
    public void An_explicit_converter_still_reads_and_writes_the_same_form()
    {
        var options = new JsonSerializerOptions { Converters = { new OuiValueJsonConverter() } };

        var value = JsonSerializer.Deserialize<OuiValue>(Shape, options);

        Assert.Equal(Shape, JsonSerializer.Serialize(value, options));
    }

    [Fact]
    public void Reading_past_the_depth_limit_fails_as_a_json_error()
    {
        var deep = new string('[', OuiValue.MaxJsonDepth + 2) + new string(']', OuiValue.MaxJsonDepth + 2);

        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<OuiValue>(deep));
    }
}
