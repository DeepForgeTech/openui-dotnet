using System.Text.Json;

namespace OpenUI.Lang.Diagnostics;

/// <summary>Where an error was detected; the wire field <c>source</c> (language.md, section 8.3).</summary>
public enum OpenUIErrorSource
{
    /// <summary><c>parser</c>: found while parsing and checking the program (language.md, section 8.3).</summary>
    Parser,

    /// <summary><c>runtime</c>: found while evaluating or rendering (language.md, section 8.3).</summary>
    Runtime,

    /// <summary><c>query</c>: a query's tool call failed (language.md, section 8.3).</summary>
    Query,

    /// <summary><c>mutation</c>: a mutation's tool call failed (language.md, section 8.3).</summary>
    Mutation,
}

/// <summary>A range of prepared program text: the text after fence extraction, comment stripping and trimming (language.md, section 1.2).</summary>
/// <param name="Offset">Zero-based offset of the first character.</param>
/// <param name="Length">Number of characters.</param>
public readonly record struct SourceSpan(int Offset, int Length);

/// <summary>One error, in the wire shape (language.md, section 8.3).</summary>
public sealed record OpenUIError
{
    /// <summary>The wire field <c>source</c> (language.md, section 8.3).</summary>
    public required OpenUIErrorSource Source { get; init; }

    /// <summary>The wire field <c>code</c> (language.md, section 8.2).</summary>
    public required OpenUIErrorCode Code { get; init; }

    /// <summary>Text for the model; never compared by conformance tests (language.md, section 8.3).</summary>
    public required string Message { get; init; }

    /// <summary>The statement the error belongs to, or null (language.md, section 8.3).</summary>
    public string? StatementId { get; init; }

    /// <summary>The component type the error belongs to, or null (language.md, section 8.3).</summary>
    public string? Component { get; init; }

    /// <summary>An RFC 6901 JSON Pointer to the prop, such as <c>/labels</c> or <c>/children/2</c> (language.md, section 8.3).</summary>
    public string? Path { get; init; }

    /// <summary>A short signature, or the available names for an unknown name (language.md, section 8.3).</summary>
    public string? Hint { get; init; }

    /// <summary>The tool of a query or mutation error (language.md, section 8.3).</summary>
    public string? ToolName { get; init; }

    /// <summary>True for an error the page renders through, today only the non-fatal <c>no-root</c>; written as <c>"severity": "warning"</c> (language.md, section 8.3).</summary>
    public bool IsWarning { get; init; }

    /// <summary>True when nothing renders (language.md, section 8.2): a fatal <c>no-root</c>, <c>parse-failed</c> or <c>parse-exception</c>.</summary>
    public bool IsFatal =>
        (Code == OpenUIErrorCode.NoRoot && !IsWarning) || Code == OpenUIErrorCode.ParseFailed || Code == OpenUIErrorCode.ParseException;

    /// <summary>Where in the prepared text the error points, when known. Not part of the wire shape (language.md, section 8.3).</summary>
    public SourceSpan? Span { get; init; }

    /// <summary>Writes the wire shape of language.md, section 8.3, leaving out null fields.</summary>
    public void WriteTo(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStartObject();
        writer.WriteString("source", SourceText(Source));
        writer.WriteString("code", Code.Value);
        WriteIfPresent(writer, "statementId", StatementId);
        WriteIfPresent(writer, "component", Component);
        WriteIfPresent(writer, "path", Path);
        writer.WriteString("message", Message);
        WriteIfPresent(writer, "hint", Hint);
        WriteIfPresent(writer, "toolName", ToolName);
        if (IsWarning)
        {
            writer.WriteString("severity", "warning");
        }

        writer.WriteEndObject();
    }

    /// <summary>The wire text of <paramref name="source"/>, such as <c>parser</c> (language.md, section 8.3).</summary>
    public static string SourceText(OpenUIErrorSource source) => source switch
    {
        OpenUIErrorSource.Parser => "parser",
        OpenUIErrorSource.Runtime => "runtime",
        OpenUIErrorSource.Query => "query",
        OpenUIErrorSource.Mutation => "mutation",
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    internal bool WireEquals(OpenUIError other) =>
        Source == other.Source
        && Code == other.Code
        && IsWarning == other.IsWarning
        && string.Equals(Message, other.Message, StringComparison.Ordinal)
        && string.Equals(StatementId, other.StatementId, StringComparison.Ordinal)
        && string.Equals(Component, other.Component, StringComparison.Ordinal)
        && string.Equals(Path, other.Path, StringComparison.Ordinal)
        && string.Equals(Hint, other.Hint, StringComparison.Ordinal)
        && string.Equals(ToolName, other.ToolName, StringComparison.Ordinal);

    private static void WriteIfPresent(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }
}
