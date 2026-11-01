using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Samples.WidgetPlayground.Catalog;

namespace OpenUI.Lang.Samples.WidgetPlayground.Generation;

/// <summary>Whether a prompt asks for a new program or a change to the current one.</summary>
public enum PromptKind
{
    Generate,
    Refine,
}

/// <summary>
/// Asks the model for a whole program, or, in edit mode (spec §7), for a patch to the current one.
/// Each refine is one self-contained request: the current program travels with it, so whatever the
/// author changed by hand since the last reply is what the model edits.
/// </summary>
public sealed class ProgramDesigner(IChatClient model, ILogger<ProgramDesigner> logger, IConfiguration configuration)
{
    /// <summary>When set, every finished prompt is appended to this file as one JSON line, for reviewing a session afterwards.</summary>
    private readonly string? _transcriptPath = configuration["OPENUI_PLAYGROUND_TRANSCRIPT"] is { Length: > 0 } path ? path : null;

    private readonly Lock _transcriptLock = new();

    /// <summary>Starts the user message of every refine; the fake provider keys on it.</summary>
    public const string RefineLead = "The current program:";

    /// <summary>Comes before the author's request in a refine message, after the program.</summary>
    public const string RequestLead = "Change it:";

    /// <summary>Streams the reply's text. A refine's reply is a patch, merged only once it has finished.</summary>
    public IAsyncEnumerable<string> StreamAsync(LibraryBundle bundle, PromptKind kind, string program, string request, CancellationToken cancellationToken) =>
        kind == PromptKind.Generate
            ? TextAsync(bundle.Prompt, request, cancellationToken)
            : TextAsync(bundle.EditPrompt, RefineMessage(program, request), cancellationToken);

    /// <summary>Logs what the parser made of a finished reply, in the same shape as the chat API's line.</summary>
    /// <returns>A short summary for the prompt history.</returns>
    public string Report(LibraryBundle bundle, PromptKind kind, string request, string before, string program, string reply)
    {
        var report = new OpenUIValidator(bundle.Library).Validate(program);
        var document = report.Document;
        var patchStatements = kind == PromptKind.Refine ? new OpenUIParser(bundle.Library).Parse(reply).Statements.Length : document.Statements.Length;
        var errors = report.Errors.Select(error => error.Code.Value).ToList();
        logger.LogInformation(
            "openui_parse kind={Kind} library={Library} root={Root} statements={Statements} reply_statements={ReplyStatements} incomplete={Incomplete} errors=[{Errors}] unresolved=[{Unresolved}]",
            kind,
            bundle.Title,
            report.Render.Root?.Type ?? "(none)",
            document.Statements.Length,
            patchStatements,
            document.Incomplete,
            string.Join(",", errors),
            string.Join(",", document.Unresolved.Distinct()));

        if (_transcriptPath is not null)
        {
            var entry = JsonSerializer.Serialize(new
            {
                at = DateTimeOffset.Now,
                kind = kind.ToString(),
                library = bundle.Title,
                request,
                before = kind == PromptKind.Refine ? before : null,
                reply,
                program,
                statements = document.Statements.Length,
                errors = report.Errors.Select(error => new { source = OpenUIError.SourceText(error.Source), code = error.Code.Value, error.StatementId, error.Message }),
                unresolved = document.Unresolved.Distinct(),
            });
            lock (_transcriptLock) File.AppendAllText(_transcriptPath, entry + "\n");
        }

        var changed = kind == PromptKind.Refine ? $"{patchStatements} changed, " : "";
        return $"{changed}{document.Statements.Length} statements, {errors.Count} errors";
    }

    private static string RefineMessage(string program, string request) => $"""
        {RefineLead}

        ```
        {program}
        ```

        {RequestLead} {request}
        """;

    private async IAsyncEnumerable<string> TextAsync(string systemPrompt, string message, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ChatMessage[] messages = [new(ChatRole.System, systemPrompt), new(ChatRole.User, message)];
        await foreach (var update in model.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken))
        {
            if (update.Text is { Length: > 0 } text) yield return text;
        }
    }
}
