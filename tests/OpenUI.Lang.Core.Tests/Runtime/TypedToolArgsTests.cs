using System.Text.Json.Serialization;
using OpenUI.Lang.Core.Tests.Conformance;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Runtime;

namespace OpenUI.Lang.Core.Tests.Runtime;

public sealed record StationFilter(double MinElevation);

public sealed record StationArgs(double RadiusMiles, string? Station = null, StationFilter? Filter = null, List<double>? Ids = null, Dictionary<string, double>? Extras = null);

public sealed record StrictArgs
{
    public required string Station { get; init; }
}

public sealed record StationResult(int Count);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StationArgs))]
[JsonSerializable(typeof(StrictArgs))]
[JsonSerializable(typeof(StationResult))]
internal sealed partial class StationJson : JsonSerializerContext;

/// <summary>What a program reads when it sends a typed tool an argument of the wrong type (language.md, section 8.2).</summary>
public sealed class TypedToolArgsTests
{
    private static OpenUILibrary Library { get; } = ConformanceAdapter.CreateLibrary(FixtureCatalog.LoadLibrary());

    private static OpenUITools Tools { get; } = new OpenUITools()
        .Add("findStations", OpenUIToolEffect.Read, (StationArgs args, CancellationToken _) => ValueTask.FromResult(new StationResult(1)), StationJson.Default.StationArgs, StationJson.Default.StationResult)
        .Add("strictStations", OpenUIToolEffect.Read, (StrictArgs args, CancellationToken _) => ValueTask.FromResult(new StationResult(1)), StationJson.Default.StrictArgs, StationJson.Default.StationResult);

    private static async Task<OpenUIError> QueryError(string tool, string args)
    {
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = Tools });
        session.Load($"q = Query(\"{tool}\", {args}, {{count: 0}})\nroot = Stack([])\n");
        await session.WhenIdleAsync();
        return Assert.Single(session.GetQueryStatus("q").Errors);
    }

    [Fact]
    public async Task A_string_for_a_number_names_the_argument_and_the_type()
    {
        var error = await QueryError("findStations", "{radiusMiles: \"25\"}");

        Assert.Equal(OpenUIErrorCode.ToolError, error.Code);
        Assert.Equal("findStations", error.ToolName);
        Assert.Equal("The tool \"findStations\" takes radiusMiles as a number, and the program sent another type.", error.Message);
    }

    [Fact]
    public async Task A_number_for_a_string_says_it_takes_a_string()
    {
        var error = await QueryError("findStations", "{radiusMiles: 25, station: 7}");

        Assert.Equal("The tool \"findStations\" takes station as a string, and the program sent another type.", error.Message);
    }

    [Fact]
    public async Task A_nested_argument_is_named_by_its_path()
    {
        var error = await QueryError("findStations", "{radiusMiles: 25, filter: {minElevation: \"high\"}}");

        Assert.Equal("The tool \"findStations\" takes filter.minElevation as a number, and the program sent another type.", error.Message);
    }

    [Fact]
    public async Task An_array_item_is_named_by_its_list()
    {
        var error = await QueryError("findStations", "{radiusMiles: 25, ids: [1, \"two\"]}");

        Assert.Equal("The tool \"findStations\" takes ids[] as a number, and the program sent another type.", error.Message);
    }

    [Fact]
    public async Task A_dictionary_key_the_program_wrote_never_reaches_the_message()
    {
        var error = await QueryError("findStations", "{radiusMiles: 25, extras: {S3cret: \"x\"}}");

        Assert.Equal("The tool \"findStations\" failed.", error.Message);
    }

    [Fact]
    public async Task A_failure_that_is_not_a_wrong_type_keeps_the_generic_message()
    {
        var error = await QueryError("strictStations", "{}");

        Assert.Equal(OpenUIErrorCode.ToolError, error.Code);
        Assert.Equal("The tool \"strictStations\" failed.", error.Message);
    }
}
