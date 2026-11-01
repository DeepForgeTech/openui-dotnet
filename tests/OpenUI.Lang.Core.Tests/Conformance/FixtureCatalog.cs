using System.Text.Json.Nodes;

namespace OpenUI.Lang.Core.Tests.Conformance;

/// <summary>One <c>&lt;area&gt;/&lt;NNN-name&gt;</c> folder of fixtures.</summary>
internal sealed record FixtureCase(string Area, string Name, string Folder)
{
    public string Id => $"{Area}/{Name}";

    public string PathOf(string file) => Path.Combine(Folder, file);

    public bool Has(string file) => File.Exists(PathOf(file));

    public bool IsValidation => Has("validation.json");

    public bool IsStreaming => Has("chunks.json");

    public bool IsEditing => Has("patch.oui");

    public bool HasInteractions => Has("interactions.json");
}

internal static class FixtureCatalog
{
    /// <summary>Where the cases live: OPENUI_FIXTURES if set, else the copy beside the test assembly.</summary>
    public static string Root { get; } = Path.GetFullPath(
        Environment.GetEnvironmentVariable("OPENUI_FIXTURES")
        ?? Path.Combine(AppContext.BaseDirectory, "fixtures", "1.0"));

    public static IReadOnlyList<FixtureCase> Cases { get; } = Discover(Root);

    public static FixtureCase Get(string id) => Cases.Single(c => c.Id == id);

    /// <summary>A fresh copy each call, so no test can be affected by another's use of the nodes.</summary>
    public static JsonObject LoadLibrary() => ReadObject(Path.Combine(Root, "library.json"));

    public static JsonObject ReadObject(string path) =>
        JsonNode.Parse(File.ReadAllText(path)) as JsonObject
        ?? throw new InvalidDataException($"{path} is not a JSON object.");

    private static List<FixtureCase> Discover(string root)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Fixture folder not found: {root}");
        }

        return Directory.EnumerateDirectories(root)
            .SelectMany(area => Directory.EnumerateDirectories(area)
                .Select(folder => new FixtureCase(Path.GetFileName(area), Path.GetFileName(folder), folder)))
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .ToList();
    }
}
