using System.Collections.Immutable;
using System.Text.Json;

namespace OpenUI.Lang.Prompting;

/// <summary>
/// What the system prompt teaches (prompt.md, section 4.2). Unset switches take the defaults that
/// <see cref="SystemPrompt.Resolve"/> applies.
/// </summary>
public sealed partial record PromptOptions
{
    /// <summary>
    /// Teaches <c>Query</c>, <c>Mutation</c> and <c>@Run</c> (prompt.md, section 4.2). Null means true when
    /// <see cref="Tools"/> is not empty.
    /// </summary>
    public bool? ToolCalls { get; init; }

    /// <summary>
    /// Teaches <c>$variables</c>, <c>@Set</c>, <c>@Reset</c> and <c>$binding&lt;type&gt;</c> props
    /// (prompt.md, section 4.2). Null means the resolved <see cref="ToolCalls"/>.
    /// </summary>
    public bool? Bindings { get; init; }

    /// <summary>
    /// Lists the built-in functions (prompt.md, section 4.2). Null means on when tool calls or bindings
    /// are on. An explicit false hides them even then, as the reference generator does.
    /// </summary>
    public bool? BuiltinFunctions { get; init; }

    /// <summary>Teaches editing by changed statements (prompt.md, sections 4.2 and 6.4).</summary>
    public bool EditMode { get; init; }

    /// <summary>Teaches prose plus fenced code (prompt.md, section 4.2).</summary>
    public bool InlineMode { get; init; }

    /// <summary>The tools the model may call, listed in the prompt (prompt.md, section 4.2).</summary>
    public ImmutableArray<ToolDescriptor> Tools { get; init; } = [];

    /// <summary>
    /// Replaces the opening paragraph (prompt.md, section 4.2). Null keeps the default; an empty string
    /// leaves the paragraph empty.
    /// </summary>
    public string? Preamble { get; init; }

    /// <summary>Host examples, printed under "Examples" (prompt.md, section 4.2).</summary>
    public ImmutableArray<string> Examples { get; init; } = [];

    /// <summary>Host examples of tool use, printed after <see cref="Examples"/> (prompt.md, section 4.2).</summary>
    public ImmutableArray<string> ToolExamples { get; init; } = [];

    /// <summary>Host rules, printed as a list at the end of the prompt (prompt.md, section 4.2).</summary>
    public ImmutableArray<string> AdditionalRules { get; init; } = [];
}

/// <summary>
/// A tool the model may call (prompt.md, section 4.2): a bare name, or a name with JSON Schemas for its
/// input and output. The prompt prints the schemas as a signature and uses the output schema for
/// default Query data.
/// </summary>
/// <param name="Name">The tool name passed to <c>Query</c> and <c>Mutation</c>.</param>
/// <param name="Description">One line about the tool, or null.</param>
/// <param name="InputSchema">The JSON Schema of the arguments object, or null.</param>
/// <param name="OutputSchema">The JSON Schema of the result, or null.</param>
/// <param name="Annotations">Host hints such as <c>readOnlyHint</c>; carried, not printed.</param>
public sealed record ToolDescriptor(
    string Name,
    string? Description = null,
    JsonElement? InputSchema = null,
    JsonElement? OutputSchema = null,
    JsonElement? Annotations = null)
{
    /// <summary>A tool given by name only (prompt.md, section 4.2).</summary>
    public static implicit operator ToolDescriptor(string name) => new(name);
}

/// <summary>
/// <see cref="PromptOptions"/> with every default applied (prompt.md, section 4.2). This is what the
/// prompt is generated from.
/// </summary>
public sealed record ResolvedPromptOptions
{
    /// <summary>Whether the prompt teaches <c>Query</c>, <c>Mutation</c> and <c>@Run</c> (prompt.md, section 4.2).</summary>
    public required bool ToolCalls { get; init; }

    /// <summary>Whether the prompt teaches <c>$variables</c>, <c>@Set</c> and <c>@Reset</c> (prompt.md, section 4.2).</summary>
    public required bool Bindings { get; init; }

    /// <summary>Whether the built-in functions are listed (prompt.md, section 4.2).</summary>
    public required bool BuiltinFunctions { get; init; }

    /// <summary>Whether the edit mode section is printed (prompt.md, section 6.4).</summary>
    public required bool EditMode { get; init; }

    /// <summary>Whether the inline mode section is printed (prompt.md, section 4.2).</summary>
    public required bool InlineMode { get; init; }

    /// <summary>The tools, never default (prompt.md, section 4.2).</summary>
    public required ImmutableArray<ToolDescriptor> Tools { get; init; }

    /// <summary>The opening paragraph: the host's preamble or the default one (prompt.md, section 4.2).</summary>
    public required string Preamble { get; init; }

    /// <summary>The host examples, never default (prompt.md, section 4.2).</summary>
    public required ImmutableArray<string> Examples { get; init; }

    /// <summary>The host tool examples, never default (prompt.md, section 4.2).</summary>
    public required ImmutableArray<string> ToolExamples { get; init; }

    /// <summary>The host rules, never default (prompt.md, section 4.2).</summary>
    public required ImmutableArray<string> AdditionalRules { get; init; }
}
