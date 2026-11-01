namespace OpenUI.Lang.Diagnostics;

/// <summary>
/// An error code, written in kebab case on the wire (language.md, section 8.2). The set is open:
/// codes this library adds beyond the spec start with <c>x-</c>.
/// </summary>
/// <param name="Value">The wire value, such as <c>missing-required</c>.</param>
public readonly record struct OpenUIErrorCode(string Value)
{
    /// <summary>Component not in the library (language.md, section 8.2).</summary>
    public static readonly OpenUIErrorCode UnknownComponent = new("unknown-component");

    /// <summary>Required prop absent (language.md, section 8.2).</summary>
    public static readonly OpenUIErrorCode MissingRequired = new("missing-required");

    /// <summary>Required prop is null (language.md, section 8.2).</summary>
    public static readonly OpenUIErrorCode NullRequired = new("null-required");

    /// <summary>Argument does not match the prop's type or enum (language.md, section 8.2).</summary>
    public static readonly OpenUIErrorCode TypeMismatch = new("type-mismatch");

    /// <summary>More arguments than props (language.md, section 8.2).</summary>
    public static readonly OpenUIErrorCode ExcessArgs = new("excess-args");

    /// <summary><c>Query</c> or <c>Mutation</c> in a value position (language.md, section 8.2).</summary>
    public static readonly OpenUIErrorCode InlineReserved = new("inline-reserved");

    /// <summary><c>@Name</c> is not a built-in, an action, or a custom function (language.md, section 8.2).</summary>
    public static readonly OpenUIErrorCode UnknownFunction = new("unknown-function");

    /// <summary>No usable entry at stream end (language.md, sections 2.2 and 8.2).</summary>
    public static readonly OpenUIErrorCode NoRoot = new("no-root");

    /// <summary>The response has text but yields no statements (language.md, section 8.2).</summary>
    public static readonly OpenUIErrorCode ParseFailed = new("parse-failed");

    /// <summary>The parser itself failed (language.md, section 8.2).</summary>
    public static readonly OpenUIErrorCode ParseException = new("parse-exception");

    /// <summary>Evaluating an expression threw (language.md, section 8.2).</summary>
    public static readonly OpenUIErrorCode RuntimeError = new("runtime-error");

    /// <summary>A component renderer threw (language.md, section 8.2).</summary>
    public static readonly OpenUIErrorCode RenderError = new("render-error");

    /// <summary>Unknown tool name (language.md, sections 6.4 and 8.2).</summary>
    public static readonly OpenUIErrorCode ToolNotFound = new("tool-not-found");

    /// <summary>The tool call failed (language.md, section 8.2).</summary>
    public static readonly OpenUIErrorCode ToolError = new("tool-error");

    /// <summary>The MCP tool call failed (language.md, section 8.2).</summary>
    public static readonly OpenUIErrorCode McpError = new("mcp-error");

    /// <summary>
    /// Extension: a budget of <see cref="OpenUILimits"/> was exceeded and the smallest unit was dropped
    /// (language.md, section 8.1 says every removal is reported; 1.0 has no code for it).
    /// </summary>
    public static readonly OpenUIErrorCode LimitExceeded = new("x-limit-exceeded");

    /// <summary>Extension: locale collation was unavailable and a fallback order was used (language.md, section 3.4).</summary>
    public static readonly OpenUIErrorCode CollationDegraded = new("x-collation-degraded");

    /// <summary>True for the codes of language.md, section 8.2; false for <c>x-</c> extension codes.</summary>
    public bool IsSpecDefined => Value is not null && !Value.StartsWith("x-", StringComparison.Ordinal);

    /// <summary>The wire value (language.md, section 8.2).</summary>
    public override string ToString() => Value;
}
