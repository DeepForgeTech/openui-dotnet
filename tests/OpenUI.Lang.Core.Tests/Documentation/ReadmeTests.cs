using OpenUI.Lang.Core.Tests.Prompt;
using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Documentation;

/// <summary>The root README's code is this file's code, so it compiles and runs against the real API.</summary>
public sealed class ReadmeTests
{
    private static string RepoFile(params string[] parts) =>
        Path.Combine([Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(GoldenFiles.FixtureLibraryPath))))!, .. parts]);

    private static async Task<(string Prompt, RenderSnapshot Page)> Sample(IAsyncEnumerable<string> modelReply)
    {
        // README begin
        var library = OpenUILibrary.CreateBuilder("tickets")
            .Root("Stack")
            .Add(new ComponentSpec("Stack", "Lays out its children", [new PropSpec("children", new ArrayShape(new ComponentRefShape([])), true)]))
            .Add(new ComponentSpec("Metric", "One headline number", [new PropSpec("label", TypeShape.String, true), new PropSpec("value", TypeShape.Number, true)]))
            .Build();

        // Send this as the system prompt.
        var systemPrompt = SystemPrompt.Generate(library.ToSpec());

        // The tools a program's queries and mutations can call. Query only calls tools that declare Read.
        var tools = new OpenUITools().Add(
            "countOpenTickets",
            OpenUIToolEffect.Read,
            (args, ct) => new ValueTask<OuiValue>(OuiValue.Number(3)));

        await using var session = OpenUISession.Create(library, new OpenUISessionOptions { Tools = tools });

        // Feed in the model's reply as it streams. Render session.Current on each session.Invalidated.
        await session.ConsumeAsync(modelReply);
        await session.WhenIdleAsync();

        var page = session.Current;   // page.Root is the tree to render
        // README end
        return (systemPrompt, page);
    }

    private static async IAsyncEnumerable<string> Reply()
    {
        yield return "open = Query(\"countOpenTickets\", {}, 0)\nroot = Stack([Met";
        await Task.Yield();
        yield return "ric(\"Open tickets\", open)])\n";
    }

    [Fact]
    public async Task The_readme_sample_runs()
    {
        var (prompt, page) = await Sample(Reply());

        Assert.Contains("Metric(label: string, value: number) — One headline number", prompt, StringComparison.Ordinal);
        Assert.Empty(page.Errors);
        Assert.True(page.Root!["children"].Value.TryGetArray(out var children));
        Assert.True(children.Items[0].TryGetComponent(out var metric));
        Assert.True(metric["value"].Value.TryGetNumber(out var value));
        Assert.Equal(3, value);
    }

    [Fact]
    public void The_readme_shows_the_code_this_file_runs()
    {
        var readme = File.ReadAllText(RepoFile("README.md"));
        var block = readme[(readme.IndexOf("## Using it", StringComparison.Ordinal))..];
        block = block[(block.IndexOf("```csharp\n", StringComparison.Ordinal) + "```csharp\n".Length)..];
        block = block[..block.IndexOf("```", StringComparison.Ordinal)];
        var shown = block.Split('\n').Where(line => !line.StartsWith("using OpenUI", StringComparison.Ordinal)).ToArray();

        var source = File.ReadAllLines(RepoFile("tests", "OpenUI.Lang.Core.Tests", "Documentation", "ReadmeTests.cs"));
        var start = Array.FindIndex(source, line => line.Trim() == "// README begin") + 1;
        var end = Array.FindIndex(source, line => line.Trim() == "// README end");
        var run = source[start..end].Select(line => line.Length >= 8 ? line[8..] : line);

        Assert.Equal(string.Join('\n', run).Trim(), string.Join('\n', shown).Trim());
    }

    [Fact]
    public void The_readme_describes_the_library_by_its_spec()
    {
        var readme = File.ReadAllText(RepoFile("README.md"));

        Assert.DoesNotContain("TypeScript", readme, StringComparison.Ordinal);
        Assert.Contains("spec/language.md", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_docs_cite_only_what_ships()
    {
        var offenders = Directory.EnumerateFiles(RepoFile("src", "OpenUI.Lang.Core"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains("architecture section", StringComparison.OrdinalIgnoreCase));

        Assert.Empty(offenders);
    }
}
