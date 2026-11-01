using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenUI.Lang.Prompting;

namespace OpenUI.Lang.Core.Tests.Prompt;

/// <summary>
/// Compares generated prompts with the text the reference generator produced for the same library and
/// options (prompt.md, section 4.1: prompts are byte-identical on every platform).
/// </summary>
public sealed class PromptGoldenTests
{
    private static readonly JsonSerializerOptions Visible = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static TheoryData<string> CaseNames() => new(GoldenFiles.CaseNames());

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Prompt_matches_the_reference_byte_for_byte(string name)
    {
        var golden = GoldenFiles.Load(name);

        AssertSameBytes(golden.Name, golden.ExpectedPrompt, Generate(golden));
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Buffer_writer_output_matches_the_reference(string name)
    {
        var golden = GoldenFiles.Load(name);
        var buffer = new ArrayBufferWriter<byte>();

        SystemPrompt.Generate(buffer, GoldenFiles.Spec(golden.Library), GoldenFiles.Options(golden.Options));

        AssertSameBytes(golden.Name, golden.ExpectedPrompt, buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void React_ui_library_prompt_matches_the_openui_cli_byte_for_byte()
    {
        var spec = GoldenFiles.ReadJsonObject(Path.Combine(GoldenFiles.ReactChatGeneratedDirectory, "spec.json"));
        var expected = File.ReadAllBytes(Path.Combine(GoldenFiles.ReactChatGeneratedDirectory, "prompt.txt"));

        // `openui generate --out` writes the prompt followed by one newline, and the file carries its options.
        var actual = Encoding.UTF8.GetBytes(SystemPrompt.Generate(GoldenFiles.Spec(spec)) + "\n");

        AssertSameBytes("react-ui openuiLibrary", expected, actual);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("de-DE")]
    public void Prompt_does_not_depend_on_the_current_culture(string culture)
    {
        var previous = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            foreach (var name in GoldenFiles.CaseNames())
            {
                var golden = GoldenFiles.Load(name);
                AssertSameBytes($"{golden.Name} under {culture}", golden.ExpectedPrompt, Generate(golden));
            }
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = previous;
        }
    }

    [Fact]
    public void Every_golden_file_has_a_case_and_the_other_way_round()
    {
        var onDisk = Directory.EnumerateFiles(GoldenFiles.Directory, "*.prompt.txt")
            .Select(path => Path.GetFileName(path)[..^".prompt.txt".Length])
            .Order(StringComparer.Ordinal);

        Assert.Equal(onDisk, GoldenFiles.CaseNames().Order(StringComparer.Ordinal));
    }

    private static byte[] Generate(GoldenCase golden) =>
        Encoding.UTF8.GetBytes(SystemPrompt.Generate(GoldenFiles.Spec(golden.Library), GoldenFiles.Options(golden.Options)));

    private static void AssertSameBytes(string name, byte[] expected, byte[] actual)
    {
        if (expected.AsSpan().SequenceEqual(actual))
        {
            return;
        }

        Assert.Fail(DescribeFirstDifference(name, Encoding.UTF8.GetString(expected), Encoding.UTF8.GetString(actual)));
    }

    private static string DescribeFirstDifference(string name, string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');

        var line = 0;
        while (line < expectedLines.Length && line < actualLines.Length && expectedLines[line] == actualLines[line])
        {
            line++;
        }

        var message = new StringBuilder($"Golden '{name}' differs at line {line + 1} ({expectedLines.Length} expected lines, {actualLines.Length} actual).");
        message.AppendLine();
        message.AppendLine($"  expected: {Show(expectedLines, line)}");
        message.Append($"  actual:   {Show(actualLines, line)}");
        return message.ToString();
    }

    // JSON quoting makes trailing spaces, tabs and control characters visible.
    private static string Show(string[] lines, int index) =>
        index < lines.Length ? JsonSerializer.Serialize(lines[index], Visible) : "<end of prompt>";
}
