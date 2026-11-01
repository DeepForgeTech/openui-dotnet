using System.Reflection;
using OpenUI.Lang.Core.Tests.Conformance;
using OpenUI.Lang.Editing;
using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Editing;

public class SerializerQuotingTests
{
    private static OpenUILibrary Library { get; } = ConformanceAdapter.CreateLibrary(FixtureCatalog.LoadLibrary());

    [Fact]
    public void The_library_declares_itself_trimmable_and_ahead_of_time_compatible()
    {
        var metadata = typeof(OuiValue).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a => a.Key, a => a.Value);

        Assert.Equal("True", metadata["IsTrimmable"]);
        Assert.Equal("True", metadata["IsAotCompatible"]);
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("quote \" and back\\slash")]
    [InlineData("line\nbreak\ttab")]
    [InlineData("<html> & 'quotes'")]
    [InlineData("control \u0001 char")]
    public void A_string_written_by_the_serializer_reads_back_unchanged_with_the_shared_quoter(string text)
    {
        var quoted = ToolSchemaText.Quote(text);
        var root = OpenUIEvaluator.Evaluate(new OpenUIParser(Library).Parse("root = Text(" + quoted + ")\n")).Root!;

        var written = OpenUISerializer.Serialize(root);

        Assert.Equal("root = Text(" + quoted + ")", written);
        var again = OpenUIEvaluator.Evaluate(new OpenUIParser(Library).Parse(written)).Root!;
        Assert.Equal(text, again["text"].Value.ToString());
    }
}
