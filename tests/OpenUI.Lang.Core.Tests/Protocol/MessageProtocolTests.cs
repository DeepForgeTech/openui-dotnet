using System.Text;
using OpenUI.Lang.Protocol;
using Xunit;

namespace OpenUI.Lang.Core.Tests.Protocol;

/// <summary>prompt.md, section 7, which has no conformance fixtures.</summary>
public class MessageProtocolTests
{
    private const string Stored = "]]>openui:content?library=support%401.2.0\nroot = Card([])\n]]>openui:context\n{\"a\":1}\n]]>openui:end\n";

    [Fact]
    public void A_stored_message_splits_into_its_sections()
    {
        var message = OpenUIMessage.Parse(Stored);
        Assert.Equal("root = Card([])", message.Content);
        Assert.Equal("{\"a\":1}", message.Context);
        Assert.True(message.Ended);
        Assert.Equal("support@1.2.0", Assert.Single(message.Attributes).Value);
        Assert.Equal("root = Card([])", message.ForModel());
    }

    [Fact]
    public void The_last_content_wins_even_cut_off_mid_line()
    {
        var message = OpenUIMessage.Parse("]]>openui:content\nroot = Car]]>openui:content\nroot = Stack([])\n]]>openui:end");
        Assert.Equal("root = Stack([])", message.Content);
    }

    [Fact]
    public void Text_before_the_first_marker_is_content_only_without_a_content_marker()
    {
        Assert.Equal("just text", OpenUIMessage.Parse("just text").Content);
        Assert.Equal("x", OpenUIMessage.Parse("ignored\n]]>openui:content\nx").Content);
    }

    [Fact]
    public void An_unknown_kind_is_stripped_with_its_text()
    {
        Assert.Equal("a", OpenUIMessage.Parse("]]>openui:content\na\n]]>openui:mystery\nhidden\n").Content);
    }

    [Fact]
    public void A_marker_in_the_middle_of_a_line_is_ordinary_text_unless_it_is_content()
    {
        Assert.Equal("a ]]>openui:end b", OpenUIMessage.Parse("]]>openui:content\na ]]>openui:end b").Content);
    }

    [Fact]
    public void A_missing_end_still_renders()
    {
        var message = OpenUIMessage.Parse("]]>openui:content\nroot = Card([])");
        Assert.False(message.Ended);
        Assert.Equal("root = Card([])", message.Content);
    }

    [Fact]
    public void Reading_character_by_character_equals_reading_whole()
    {
        var reader = new OpenUIMessageStreamReader();
        var content = new StringBuilder();
        foreach (var c in Stored)
        {
            var update = reader.Push([c]);
            Assert.False(update.Text.Contains("]]>", StringComparison.Ordinal));
            content.Append(update.Text);
        }

        content.Append(reader.Complete().Text);
        Assert.Equal("root = Card([])", content.ToString());
        Assert.Equal("{\"a\":1}", reader.Context);
        Assert.True(reader.Ended);
    }

    [Fact]
    public void A_regenerated_response_resets_the_stream()
    {
        var reader = new OpenUIMessageStreamReader();
        Assert.Equal("first", reader.Push("]]>openui:content\nfirst").Text);
        var update = reader.Push("\n]]>openui:content\nsecond");
        Assert.True(update.IsReset);
        Assert.Equal("second", update.Text);
    }

    [Fact]
    public void Writing_then_reading_round_trips()
    {
        using var writer = new StringWriter();
        OpenUIMessageWriter.WriteContentMarker(writer, "support", "1.2.0");
        writer.Write("root = Card([])");
        OpenUIMessageWriter.WriteContext(writer, "{\"a\":1}"u8);
        OpenUIMessageWriter.WriteEnd(writer);

        var message = OpenUIMessage.Parse(writer.ToString());
        Assert.Equal("root = Card([])", message.Content);
        Assert.Equal("{\"a\":1}", message.Context);
        Assert.True(message.Ended);
        Assert.Equal("support@1.2.0", Assert.Single(message.Attributes).Value);
    }
}
