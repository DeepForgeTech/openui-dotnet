using System.Reflection;
using System.Text.Json;
using Bunit;
using OpenUI.Lang.Blazor.Tests.TestComponents;

using OpenUI.Lang.Blazor.Mud;

namespace OpenUI.Lang.Blazor.Tests.Mud;

/// <summary>The safe markdown any kit gets, with no kit involved.</summary>
public sealed class MarkdownTests
{
    private static readonly MarkdownStyle Style = new() { LinkClass = "a-link", ParagraphClass = "a-para", TableStyle = "color:red", CellStyle = "color:blue" };

    [Fact]
    public void The_kit_s_style_lands_on_the_markup_and_the_text_s_does_not()
    {
        var html = Markdown.Block("hi [ok](https://example.com) {.evil onclick=x}\n\n| A |\n| - |\n| 1 |", Style).Value;

        Assert.Contains("class=\"a-para\"", html);
        Assert.Contains("class=\"a-link\"", html);
        Assert.Contains("style=\"color:red\"", html);
        Assert.Contains("style=\"color:blue\"", html);
        Assert.DoesNotContain("class=\"evil\"", html);
    }

    [Fact]
    public void With_no_style_it_draws_bare_markup()
    {
        Assert.Equal("<p><strong>hi</strong></p>\n", Markdown.Block("**hi**").Value);
        Assert.Equal("<strong>hi</strong>", Markdown.Inline("**hi**").Value);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>", "<script")]
    [InlineData("<img src=x onerror=alert(1)>", "<img")]
    [InlineData("[x](javascript:alert(1))", "<a ")]
    [InlineData("[x](data:text/html,hi)", "<a ")]
    [InlineData("![x](https://example.org/pixel.png)", "<img")]
    [InlineData("*x*{onclick=\"alert(1)\"}", " onclick=")]
    public void Nothing_in_the_text_adds_markup_of_its_own(string text, string forbidden)
    {
        Assert.DoesNotContain(forbidden, Markdown.Block(text).Value);
        Assert.DoesNotContain(forbidden, Markdown.Inline(text).Value);
    }

    [Fact]
    public void A_link_keeps_only_a_safe_scheme_and_opens_without_the_opener()
    {
        var html = Markdown.Block("[ok](https://example.com) [mail](mailto:a@example.com) [no](ftp://example.com)").Value;

        Assert.Contains("href=\"https://example.com\" target=\"_blank\" rel=\"noopener noreferrer\"", html);
        Assert.Contains("href=\"mailto:a@example.com\"", html);
        Assert.DoesNotContain("ftp:", html);
    }

    [Fact]
    public void Text_over_the_limit_shows_as_plain_text_and_text_at_it_is_parsed()
    {
        var at = "**b**" + new string('x', Markdown.MaxChars - 5);
        var over = at + "x";

        Assert.Contains("<strong>", Markdown.Block(at).Value);
        Assert.DoesNotContain("<strong>", Markdown.Block(over).Value);
        Assert.Contains("**b**", Markdown.Block(over).Value);
        Assert.DoesNotContain("<strong>", Markdown.Inline(over).Value);
    }

    [Fact]
    public void Plain_text_over_the_limit_is_still_encoded()
    {
        var html = Markdown.Block("<script>alert(1)</script>" + new string('x', Markdown.MaxChars)).Value;

        Assert.DoesNotContain("<script", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Text_already_drawn_is_not_parsed_again()
    {
        var text = "**cached** " + Guid.NewGuid();

        Assert.Same(Markdown.Block(text).Value, Markdown.Block(text).Value);
        Assert.Same(Markdown.Inline(text).Value, Markdown.Inline(text).Value);
        Assert.Same(Markdown.Block(text, Style, lineBreaks: true).Value, Markdown.Block(text, Style with { }, lineBreaks: true).Value);
    }

    [Fact]
    public void The_same_text_with_another_style_or_mode_is_drawn_for_that()
    {
        var text = "one **two**\nthree " + Guid.NewGuid();

        Assert.DoesNotContain("<br", Markdown.Block(text).Value);
        Assert.Contains("<br", Markdown.Block(text, lineBreaks: true).Value);
        Assert.Contains("a-para", Markdown.Block(text, Style).Value);
        Assert.DoesNotContain("<p", Markdown.Inline(text).Value);
    }

    [Fact]
    public void The_cache_drops_the_least_recently_used_past_its_entries()
    {
        var cache = new MarkdownCache(maxEntries: 2, maxChars: 1000);
        var (a, b, c) = (Key("a"), Key("b"), Key("c"));
        cache.Add(a, "A");
        cache.Add(b, "B");
        Assert.True(cache.TryGet(a, out _));

        cache.Add(c, "C");

        Assert.True(cache.TryGet(a, out _));
        Assert.False(cache.TryGet(b, out _));
        Assert.True(cache.TryGet(c, out var html));
        Assert.Equal("C", html);
    }

    [Fact]
    public void The_cache_holds_no_more_characters_than_it_may()
    {
        var cache = new MarkdownCache(maxEntries: 100, maxChars: 10);
        cache.Add(Key("aa"), "AAA");
        cache.Add(Key("bb"), "BBB");

        cache.Add(Key("cc"), "CCCC");

        Assert.False(cache.TryGet(Key("aa"), out _));
        Assert.True(cache.TryGet(Key("cc"), out _));
    }

    private static MarkdownCache.Key Key(string text) => new(text, null, false, false);

    [Fact]
    public void The_public_api_names_no_markdown_library_type()
    {
        var types = typeof(Markdown).Assembly.GetExportedTypes();
        var methods = types.SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => method.IsPublic || method.IsFamily);
        var named = methods.SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType));

        Assert.DoesNotContain(types.Concat(named), type => type.Namespace?.StartsWith("Markdig", StringComparison.Ordinal) == true);
    }

    // Each of these takes tens of seconds at this size if it is parsed, and it would be on every chunk.
    [Theory]
    [InlineData("[a](")]
    [InlineData("[](")]
    [InlineData("- ")]
    [InlineData("www.a.com ")]
    public async Task Costly_text_stays_fast_through_a_view_on_every_chunk(string unit)
    {
        var text = JsonSerializer.Serialize(string.Concat(Enumerable.Repeat(unit, 200 * 1024 / unit.Length)) + "x");
        var library = TestLibrary.MudWith("markdown-view", views => views.Add<AnyView>("Stack").Add<PlainMarkdownView>("TextContent"));
        await using var context = TestLibrary.NewContext(library);
        var cut = context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, $"root = Stack([TextContent({text}), TextContent(\"\")])\n")
            .Add(component => component.IsStreaming, true));
        var started = System.Diagnostics.Stopwatch.StartNew();

        for (var chunk = 0; chunk < 10; chunk++)
        {
            var sibling = new string('s', chunk + 1);
            cut.Render(parameters => parameters
                .Add(component => component.Program, $"root = Stack([TextContent({text}), TextContent(\"{sibling}\")])\n")
                .Add(component => component.IsStreaming, true));
        }

        Assert.InRange(started.Elapsed.TotalSeconds, 0, 10);
        Assert.Contains("pre-wrap", cut.Markup);
    }

    [Fact]
    public async Task Costly_text_under_the_limit_is_parsed_once_while_a_sibling_grows()
    {
        var text = JsonSerializer.Serialize(string.Concat(Enumerable.Repeat("[a](", (Markdown.MaxChars - 10) / 4)) + Guid.NewGuid().ToString()[..8]);
        var library = TestLibrary.MudWith("markdown-view", views => views.Add<AnyView>("Stack").Add<PlainMarkdownView>("TextContent"));
        await using var context = TestLibrary.NewContext(library);
        var cut = context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, $"root = Stack([TextContent({text}), TextContent(\"\")])\n")
            .Add(component => component.IsStreaming, true));
        var started = System.Diagnostics.Stopwatch.StartNew();

        for (var chunk = 0; chunk < 40; chunk++)
        {
            var sibling = new string('s', chunk + 1);
            cut.Render(parameters => parameters
                .Add(component => component.Program, $"root = Stack([TextContent({text}), TextContent(\"{sibling}\")])\n")
                .Add(component => component.IsStreaming, true));
        }

        Assert.InRange(started.Elapsed.TotalSeconds, 0, 8);
    }
}
