using System.Text.RegularExpressions;

namespace OpenUI.Lang.Core.Tests.Conformance;

/// <summary>
/// The OpenUI 1.0 conformance fixtures run against the library through <see cref="ConformanceAdapter"/>.
/// Each case folder is its own test, so a failure names the case and prints a path-level diff.
/// </summary>
public sealed class ConformanceTests
{
    [FixtureTest(FixtureSuite.Program)]
    public void Program(string caseId) => Verify(caseId, FixtureRunner.Program);

    [FixtureTest(FixtureSuite.Interactions)]
    public void Interactions(string caseId) => Verify(caseId, FixtureRunner.Interactions);

    [FixtureTest(FixtureSuite.CharByChar)]
    public void StreamingEqualsParsing(string caseId) => Verify(caseId, FixtureRunner.CharByChar);

    private static void Verify(string caseId, Func<FixtureCase, IReadOnlyList<Difference>> run)
    {
        var differences = run(FixtureCatalog.Get(caseId));
        if (differences.Count > 0)
        {
            Assert.Fail(Difference.Format(caseId, differences));
        }
    }
}

/// <summary>
/// Guards against a fixture case that exists but never becomes a test. What is expected comes from the
/// source tree's area READMEs, which list every case; what is discovered comes from the discoverer's own
/// per-suite filter, reading the copy beside the test assembly. A case missing from either side fails.
/// </summary>
public sealed partial class FixtureDiscoveryTests
{
    // Where the fixtures were authored: OPENUI_FIXTURES if set, else found by walking up to the solution.
    private static readonly string SourceRoot = Environment.GetEnvironmentVariable("OPENUI_FIXTURES") is { } configured
        ? Path.GetFullPath(configured)
        : FindSourceRoot();

    private static string FindSourceRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "OpenUI.Lang.sln")))
            {
                return Path.Combine(folder.FullName, "tests", "fixtures", "1.0");
            }
        }

        throw new DirectoryNotFoundException("No OpenUI.Lang.sln above " + AppContext.BaseDirectory);
    }

    // A case row starts with its folder name; a suffix such as "(+ interactions)" may follow it.
    [GeneratedRegex(@"^\|\s*(\d{3}-[^\s|]+)", RegexOptions.Multiline)]
    private static partial Regex CaseRow();

    private static SortedSet<string> ListedInReadmes() =>
        new(
            Directory.EnumerateDirectories(SourceRoot).SelectMany(area =>
                CaseRow().Matches(File.ReadAllText(Path.Combine(area, "README.md")))
                    .Select(row => $"{Path.GetFileName(area)}/{row.Groups[1].Value}")),
            StringComparer.Ordinal);

    private static List<string> ListedHaving(string file) =>
        ListedInReadmes().Where(id => File.Exists(Path.Combine(SourceRoot, id, file))).ToList();

    private static SortedSet<string> Discovered(FixtureSuite suite) =>
        new(FixtureTestDiscoverer.CasesOf(suite).Select(c => c.Id), StringComparer.Ordinal);

    [Fact]
    public void Every_case_folder_in_the_source_tree_is_listed_in_its_area_readme()
    {
        var folders = new SortedSet<string>(
            Directory.EnumerateDirectories(SourceRoot).SelectMany(area => Directory.EnumerateDirectories(area)
                .Select(folder => $"{Path.GetFileName(area)}/{Path.GetFileName(folder)}")),
            StringComparer.Ordinal);

        Assert.NotEmpty(folders);
        Assert.Equal(ListedInReadmes(), folders);
    }

    [Fact]
    public void The_program_suite_runs_every_listed_case()
    {
        Assert.NotEmpty(ListedInReadmes());
        Assert.Equal(ListedInReadmes(), Discovered(FixtureSuite.Program));
    }

    [Fact]
    public void The_interactions_suite_runs_every_listed_case_with_an_interactions_file()
    {
        var expected = ListedHaving("interactions.json");

        Assert.NotEmpty(expected);
        Assert.Equal(expected, Discovered(FixtureSuite.Interactions));
    }

    [Fact]
    public void The_character_by_character_suite_runs_every_listed_case_that_has_a_program()
    {
        var validation = ListedHaving("validation.json");
        var expected = ListedInReadmes().Except(validation).ToList();

        Assert.NotEmpty(validation);
        Assert.Equal(expected, Discovered(FixtureSuite.CharByChar));
    }

    [Fact]
    public void Every_interactions_file_belongs_to_a_discovered_case()
    {
        var files = Directory.EnumerateFiles(FixtureCatalog.Root, "interactions.json", SearchOption.AllDirectories).Count();

        Assert.Equal(files, FixtureCatalog.Cases.Count(c => c.HasInteractions));
    }

    [Fact]
    public void Every_case_is_a_program_or_a_validation_case()
    {
        var unrunnable = FixtureCatalog.Cases
            .Where(c => !c.IsValidation && !c.Has("expected.json"))
            .Select(c => c.Id)
            .ToList();

        Assert.Empty(unrunnable);
    }
}
