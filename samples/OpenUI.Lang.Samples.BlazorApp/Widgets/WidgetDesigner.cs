using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using OpenUI.Lang.Blazor;
using OpenUI.Lang.Prompting;
using OpenUI.Lang.Samples.BlazorApp.Chat;
using OpenUI.Lang.Samples.BlazorApp.Weather;

namespace OpenUI.Lang.Samples.BlazorApp.Widgets;

/// <summary>
/// Asks the model for a whole widget, or, in edit mode (spec §7), for a patch to the current one. Each
/// refine is one request on its own: the program travels with it as it is now, hand edits included.
/// </summary>
public sealed class WidgetDesigner(IChatClient model, OpenUIComponents components)
{
    /// <summary>Starts the user message of every refine.</summary>
    public const string RefineLead = "The current widget:";

    /// <summary>Comes before the person's request in a refine message, after the program.</summary>
    public const string RequestLead = "Change it:";

    /// <summary>Comes before the program's current errors in a refine message, when it has any.</summary>
    public const string ErrorsLead = "It has these errors now:";

    private static readonly ImmutableArray<string> Rules =
    [
        "Reply only with a program.",
        "Build one widget for a dashboard: a single Card that fits a tile, with a short title that says what it shows.",
        "Nobody reads replies once a widget is saved, so never use @ToAssistant.",
        ChatTurns.QueryRule,
    ];

    /// <summary>The system prompt for a new widget.</summary>
    public string GeneratePrompt => field ??= components.SystemPrompt(Options(editMode: false));

    /// <summary>The system prompt for a change to the current widget.</summary>
    public string EditPrompt => field ??= components.SystemPrompt(Options(editMode: true));

    /// <summary>Streams the reply's text. A refine's reply is a patch, to merge once it has finished.</summary>
    /// <param name="program">The widget's program now, or blank for a new one.</param>
    /// <param name="request">What the person asked for.</param>
    /// <param name="errors">The program's errors as model feedback JSON, or null when it has none.</param>
    /// <param name="cancellationToken">Ends the model call.</param>
    public IAsyncEnumerable<string> StreamAsync(string program, string request, string? errors, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(program)
            ? TextAsync(GeneratePrompt, request, cancellationToken)
            : TextAsync(EditPrompt, RefineMessage(program, request, errors), cancellationToken);

    /// <summary>The user message of a refine.</summary>
    public static string RefineMessage(string program, string request, string? errors) => $"""
        {RefineLead}

        {program.Trim()}

        {(errors is null ? "" : $"{ErrorsLead} {errors}\n\n")}{RequestLead} {request}
        """;

    private static PromptOptions Options(bool editMode) => new()
    {
        Tools = WeatherTools.Descriptors,
        AdditionalRules = Rules,
        EditMode = editMode,
    };

    private async IAsyncEnumerable<string> TextAsync(string systemPrompt, string message, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ChatMessage[] messages = [new(ChatRole.System, systemPrompt), new(ChatRole.User, message)];
        await foreach (var update in model.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken))
        {
            if (update.Text is { Length: > 0 } text)
            {
                yield return text;
            }
        }
    }
}
