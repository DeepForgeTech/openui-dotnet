using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using OpenUI.Lang.Library;

namespace OpenUI.Lang.Prompting;

/// <summary>
/// Generates the system prompt from a LibrarySpec and options (prompt.md, section 4). Output is
/// deterministic: the same inputs give the same bytes on every platform and culture (section 4.1).
/// Pure and thread-safe.
/// </summary>
public static class SystemPrompt
{
    /// <summary>
    /// The system prompt for <paramref name="library"/> (prompt.md, section 4.4): lines joined by
    /// <c>"\n"</c>, with no trailing newline. Null options mean all defaults.
    /// </summary>
    public static string Generate(LibrarySpec library, PromptOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        return new PromptBuilder(library, Resolve(options ?? library.ReadPromptOptions() ?? new PromptOptions())).Build();
    }

    /// <summary>Writes the system prompt (prompt.md, section 4) to <paramref name="utf8"/> as UTF-8.</summary>
    public static void Generate(IBufferWriter<byte> utf8, LibrarySpec library, PromptOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        Encoding.UTF8.GetBytes(Generate(library, options), utf8);
    }

    /// <summary>
    /// Applies the option defaults (prompt.md, section 4.2): tool calls default to on when there are
    /// tools, bindings follow tool calls, and built-in functions follow either of them.
    /// </summary>
    public static ResolvedPromptOptions Resolve(PromptOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var tools = OrEmpty(options.Tools);
        var toolCalls = options.ToolCalls ?? !tools.IsEmpty;
        var bindings = options.Bindings ?? toolCalls;
        return new ResolvedPromptOptions
        {
            ToolCalls = toolCalls,
            Bindings = bindings,
            BuiltinFunctions = options.BuiltinFunctions ?? (toolCalls || bindings),
            EditMode = options.EditMode,
            InlineMode = options.InlineMode,
            Tools = tools,
            Preamble = options.Preamble ?? PromptText.DefaultPreamble,
            Examples = OrEmpty(options.Examples),
            ToolExamples = OrEmpty(options.ToolExamples),
            AdditionalRules = OrEmpty(options.AdditionalRules),
        };
    }

    private static ImmutableArray<T> OrEmpty<T>(ImmutableArray<T> items) => items.IsDefault ? [] : items;
}
