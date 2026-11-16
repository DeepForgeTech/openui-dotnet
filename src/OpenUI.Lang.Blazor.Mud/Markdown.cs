using System.Diagnostics;
using System.Net;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.Components;

namespace OpenUI.Lang.Blazor.Mud;

/// <summary>What the views add to the markup <see cref="Markdown"/> draws. Each comes from the view, never from the text.</summary>
internal sealed record MarkdownStyle
{
    /// <summary>CSS classes for a link.</summary>
    public string? LinkClass { get; init; }

    /// <summary>CSS classes for a paragraph.</summary>
    public string? ParagraphClass { get; init; }

    /// <summary>The style of a table.</summary>
    public string? TableStyle { get; init; }

    /// <summary>The style of a table cell.</summary>
    public string? CellStyle { get; init; }
}

/// <summary>
/// Markdown from a program as markup. The text is the model's, so nothing in it may add markup of its own: raw HTML
/// is off, only http, https and mailto links survive, and images show as their alt text. Parsing is not linear in the
/// worst case, so text over <see cref="MaxChars"/> shows as plain text, and text already drawn is not parsed again.
/// The views draw it through <c>MarkdownText</c>, which keeps a session to <see cref="MarkdownAllowance.Total"/> characters of it and
/// <see cref="ParseBudget"/> of parsing.
/// </summary>
internal static class Markdown
{
    /// <summary>
    /// The longest text parsed as markdown. Crafted text such as <c>[a](</c> repeated costs time with the square of its
    /// length, under a second at this size and about 25 s at the 256 KB a statement may hold.
    /// </summary>
    public const int MaxChars = 32 * 1024;

    // A page draws few strings, but a stream adds one per chunk for each that grows. The budget is what the cache can hold on to.
    private static readonly MarkdownCache Cache = new(maxEntries: 256, maxChars: 4 * 1024 * 1024);

    // No GenericAttributes: it would let the text set attributes such as onclick.
    private static readonly MarkdownPipeline Pipeline = NewPipeline().Build();

    // TextContent breaks a line where the text does; MarkDownRenderer and a callout's description do not.
    private static readonly MarkdownPipeline LineBreakPipeline = NewPipeline().UseSoftlineBreakAsHardlineBreak().Build();

    /// <summary>The blocks of the markdown: paragraphs, lists, tables, code.</summary>
    /// <param name="text">The markdown, which may be cut off mid-way while a program streams.</param>
    /// <param name="style">What the view adds to the markup.</param>
    /// <param name="lineBreaks">True to break a line wherever the text does.</param>
    public static MarkupString Block(string? text, MarkdownStyle? style = null, bool lineBreaks = false) =>
        Cached(new MarkdownCache.Key(text ?? "", style, lineBreaks, Inline: false));

    /// <summary>The markdown as one line of inline markup, for a heading: block structure is dropped and its text kept.</summary>
    /// <param name="text">The markdown, which may be cut off mid-way while a program streams.</param>
    /// <param name="style">What the view adds to the markup.</param>
    public static MarkupString Inline(string? text, MarkdownStyle? style = null) =>
        Cached(new MarkdownCache.Key(text ?? "", style, LineBreaks: false, Inline: true));

    /// <summary><see cref="Block"/>, unless the budget is spent and the text is not in the cache.</summary>
    /// <param name="text">The markdown.</param>
    /// <param name="style">What the view adds to the markup.</param>
    /// <param name="lineBreaks">True to break a line wherever the text does.</param>
    /// <param name="budget">The time to parse with. A text that is already drawn costs none.</param>
    /// <param name="markup">The blocks, when this returns true.</param>
    /// <returns>False when the budget is spent and the text would need parsing.</returns>
    public static bool TryBlock(string? text, MarkdownStyle? style, bool lineBreaks, ParseBudget budget, out MarkupString markup) =>
        TryCached(new MarkdownCache.Key(text ?? "", style, lineBreaks, Inline: false), budget, out markup);

    /// <summary><see cref="Inline"/>, unless the budget is spent and the text is not in the cache.</summary>
    /// <param name="text">The markdown.</param>
    /// <param name="style">What the view adds to the markup.</param>
    /// <param name="budget">The time to parse with. A text that is already drawn costs none.</param>
    /// <param name="markup">The line, when this returns true.</param>
    /// <returns>False when the budget is spent and the text would need parsing.</returns>
    public static bool TryInline(string? text, MarkdownStyle? style, ParseBudget budget, out MarkupString markup) =>
        TryCached(new MarkdownCache.Key(text ?? "", style, LineBreaks: false, Inline: true), budget, out markup);

    private static MarkupString Cached(MarkdownCache.Key key)
    {
        TryCached(key, null, out var markup);
        return markup;
    }

    private static bool TryCached(MarkdownCache.Key key, ParseBudget? budget, out MarkupString markup)
    {
        if (key.Text.Length > MaxChars)
        {
            markup = new MarkupString(Plain(key.Text));
            return true;
        }

        if (!Cache.TryGet(key, out var html))
        {
            if (budget?.IsSpent == true)
            {
                markup = default;
                return false;
            }

            var started = Stopwatch.GetTimestamp();
            html = Draw(key);
            budget?.Spend(Stopwatch.GetElapsedTime(started));
            Cache.Add(key, html);
        }

        markup = new MarkupString(html);
        return true;
    }

    private static string Draw(MarkdownCache.Key key)
    {
        try
        {
            return key.Inline ? DrawInline(key.Text, key.Style) : Markdig.Markdown.ToHtml(Parse(key.Text, key.Style, key.LineBreaks ? LineBreakPipeline : Pipeline), key.LineBreaks ? LineBreakPipeline : Pipeline);
        }
        catch (ArgumentException)
        {
            // Markdig refuses text nested too deep to be sensible, such as a hundred quote marks in a row, by throwing. It shows as it is.
            return Plain(key.Text);
        }
    }

    private static string DrawInline(string text, MarkdownStyle? style)
    {
        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer) { ImplicitParagraph = true };
        Pipeline.Setup(renderer);
        foreach (var leaf in Parse(text, style, Pipeline).Descendants<LeafBlock>())
        {
            renderer.WriteLeafInline(leaf);
            writer.Write(' ');
        }

        return writer.ToString().TrimEnd();
    }

    /// <summary>The text as it is, encoded, for text that is not parsed.</summary>
    /// <param name="text">The text.</param>
    public static MarkupString PlainText(string? text) => new(Plain(text ?? ""));

    private static string Plain(string text) => $"<div style=\"white-space: pre-wrap\">{WebUtility.HtmlEncode(text)}</div>";

    private static MarkdownPipelineBuilder NewPipeline() =>
        new MarkdownPipelineBuilder()
            .DisableHtml()
            .UsePipeTables()
            .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
            .UseTaskLists()
            .UseAutoLinks();

    private static MarkdownDocument Parse(string text, MarkdownStyle? style, MarkdownPipeline pipeline)
    {
        var document = Markdig.Markdown.Parse(text, pipeline);
        foreach (var autolink in document.Descendants<AutolinkInline>().ToList())
        {
            if (!IsAllowed(autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url))
            {
                autolink.InsertAfter(new LiteralInline(autolink.Url));
                autolink.Remove();
            }
        }

        foreach (var link in document.Descendants<LinkInline>().ToList())
        {
            if (link.IsImage || !IsAllowed(link.Url))
            {
                Unwrap(link);
                continue;
            }

            var attributes = link.GetAttributes();
            AddClass(attributes, style?.LinkClass);
            attributes.AddProperty("target", "_blank");
            attributes.AddProperty("rel", "noopener noreferrer");
        }

        foreach (var paragraph in document.Descendants<ParagraphBlock>())
        {
            AddClass(paragraph.GetAttributes(), style?.ParagraphClass);
        }

        foreach (var table in document.Descendants<Table>())
        {
            AddStyle(table.GetAttributes(), style?.TableStyle);
        }

        foreach (var cell in document.Descendants<TableCell>())
        {
            AddStyle(cell.GetAttributes(), style?.CellStyle);
        }

        return document;
    }

    private static void AddClass(HtmlAttributes attributes, string? classes)
    {
        if (classes is not null)
        {
            attributes.AddClass(classes);
        }
    }

    private static void AddStyle(HtmlAttributes attributes, string? style)
    {
        if (style is not null)
        {
            attributes.AddProperty("style", style);
        }
    }

    // A link keeps its text when its target is not allowed; so does an image, as its alt text.
    private static void Unwrap(LinkInline link)
    {
        foreach (var child in link.ToList())
        {
            child.Remove();
            link.InsertBefore(child);
        }

        link.Remove();
    }

    private static bool IsAllowed(string? url) =>
        url is { Length: > 0 }
        && !url.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" or "mailto";
}
