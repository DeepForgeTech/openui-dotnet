using System.Text.Json;
using System.Text.Json.Nodes;
using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;

namespace OpenUI.Lang.Core.Tests.Prompt;

/// <summary>One golden: a library, the options handed to upstream, and the prompt upstream produced.</summary>
internal sealed record GoldenCase(string Name, JsonObject Library, JsonObject Options, byte[] ExpectedPrompt);

/// <summary>Finds the prompt goldens and the fixture library on disk.</summary>
internal static class GoldenFiles
{
    /// <summary>A golden's library as a <see cref="LibrarySpec"/>.</summary>
    public static LibrarySpec Spec(JsonObject library) => LibrarySpec.FromJson(Element(library));

    /// <summary>A golden's options, read by the code that ships.</summary>
    public static PromptOptions Options(JsonObject options) => PromptOptions.FromJson(Element(options));

    private static JsonElement Element(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }

    private static readonly Lazy<string> RepoRoot = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "OpenUI.Lang.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("OpenUI.Lang.sln was not found above the test output directory.");
    });

    public static string Directory { get; } =
        Path.Combine(RepoRoot.Value, "tests", "OpenUI.Lang.Core.Tests", "Prompt", "Goldens");

    public static string FixtureLibraryPath { get; } =
        Path.Combine(RepoRoot.Value, "tests", "fixtures", "1.0", "library.json");

    /// <summary>
    /// The react-ui library spec and prompt that <c>bun run generate</c> in samples/react-chat writes
    /// with upstream's openui CLI.
    /// </summary>
    public static string ReactChatGeneratedDirectory { get; } =
        Path.Combine(RepoRoot.Value, "samples", "react-chat", "src", "generated");

    public static JsonObject ReadJsonObject(string path) =>
        JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new InvalidDataException($"{path} is not a JSON object.");

    public static IReadOnlyList<string> CaseNames() =>
        ((JsonArray)JsonNode.Parse(File.ReadAllText(Path.Combine(Directory, "cases.json")))!)
        .Select(entry => (string)entry!["name"]!)
        .ToList();

    public static GoldenCase Load(string name)
    {
        var cases = (JsonArray)JsonNode.Parse(File.ReadAllText(Path.Combine(Directory, "cases.json")))!;
        var entry = cases.Single(candidate => (string)candidate!["name"]! == name)!;
        var libraryName = (string)entry["library"]!;
        var libraryPath = libraryName == "fixtures"
            ? FixtureLibraryPath
            : Path.Combine(Directory, "libraries", $"{libraryName}.json");

        return new GoldenCase(
            name,
            ReadJsonObject(libraryPath),
            entry["options"]!.AsObject(),
            File.ReadAllBytes(Path.Combine(Directory, $"{name}.prompt.txt")));
    }
}
