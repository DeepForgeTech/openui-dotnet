using OpenUI.Lang.AspNetCore;

namespace OpenUI.Lang.Samples.ChatApi;

/// <summary>One error from the final parse, as served by <c>GET /api/debug/last</c>.</summary>
internal sealed record ParseErrorReport(string Code, string Message, string? StatementId, string? Component, string? Path);

/// <summary>What OpenUI.Lang.Core made of a whole reply.</summary>
internal sealed record ParseReport(
    DateTimeOffset At,
    string? Root,
    int Statements,
    bool Incomplete,
    IReadOnlyList<ParseErrorReport> Errors,
    IReadOnlyList<string> Unresolved,
    IReadOnlyList<string> Orphans,
    string Reply)
{
    public static ParseReport From(OpenUIReplyCompletedContext reply)
    {
        var document = reply.Report.Document;
        return new ParseReport(
            DateTimeOffset.UtcNow,
            reply.Report.Render.Root?.Type,
            document.Statements.Length,
            document.Incomplete,
            [.. reply.Report.Errors.Select(error => new ParseErrorReport(error.Code.Value, error.Message, error.StatementId, error.Component, error.Path))],
            [.. document.Unresolved.Distinct()],
            document.Orphans,
            reply.Reply);
    }
}

/// <summary>Holds the report of the most recent reply.</summary>
internal sealed class LastReply
{
    private ParseReport? _report;

    public ParseReport? Report
    {
        get => Volatile.Read(ref _report);
        set => Volatile.Write(ref _report, value);
    }

    /// <summary>The package's reply hook: keeps the report for <c>/api/debug/last</c> and logs one line.</summary>
    public static Task OnReplyCompletedAsync(OpenUIReplyCompletedContext reply)
    {
        var services = reply.HttpContext.RequestServices;
        var report = ParseReport.From(reply);
        services.GetRequiredService<LastReply>().Report = report;
        services.GetRequiredService<ILoggerFactory>().CreateLogger<LastReply>().LogInformation(
            "openui_parse root={Root} statements={Statements} incomplete={Incomplete} errors=[{Errors}] unresolved=[{Unresolved}]",
            report.Root ?? "(none)",
            report.Statements,
            report.Incomplete,
            string.Join(",", report.Errors.Select(error => error.Code)),
            string.Join(",", report.Unresolved));
        return Task.CompletedTask;
    }
}
