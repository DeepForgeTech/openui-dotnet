using Microsoft.AspNetCore.Components;

namespace OpenUI.Lang.Blazor.Mud;

/// <summary>The core package's safe markdown, with MudBlazor's classes and palette on its links and tables.</summary>
internal static class MudMarkdown
{
    private static readonly MarkdownStyle Style = new()
    {
        LinkClass = "mud-link mud-primary-text",
        ParagraphClass = "mt-0 mb-2",
        TableStyle = "border-collapse:collapse",
        CellStyle = "border:1px solid var(--mud-palette-lines-default);padding:4px 8px",
    };

    public static bool TryBlock(string? markdown, bool lineBreaks, ParseBudget budget, out MarkupString markup) =>
        Markdown.TryBlock(markdown, Style, lineBreaks, budget, out markup);

    public static bool TryInline(string? markdown, ParseBudget budget, out MarkupString markup) =>
        Markdown.TryInline(markdown, Style, budget, out markup);
}
