using System.Text.Json.Serialization.Metadata;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Runtime;

/// <summary>What calling a tool does. <c>Query</c> runs on load with no click, so it may call only tools that declare <see cref="Read"/> (language.md, sections 6.1, 6.2 and 6.4).</summary>
public enum OpenUIToolEffect
{
    /// <summary>It may change something; only <c>Mutation</c> calls it. The default for a tool that does not say (language.md, section 6.2).</summary>
    Mutating,

    /// <summary>It only reads; <c>Query</c> and <c>Mutation</c> may both call it (language.md, section 6.1).</summary>
    Read,
}

/// <summary>The tools a host offers to queries and mutations (language.md, section 6.4).</summary>
public interface IOpenUIToolProvider
{
    /// <summary>The tool names, listed in a <c>tool-not-found</c> hint (section 6.4).</summary>
    IReadOnlyCollection<string> ToolNames { get; }

    /// <summary>True when a tool of this exact, case-sensitive name exists (section 6.4).</summary>
    bool Contains(string name);

    /// <summary>What the tool does; a tool that does not say counts as mutating (sections 6.2 and 6.4).</summary>
    OpenUIToolEffect GetEffect(string name) => OpenUIToolEffect.Mutating;

    /// <summary>
    /// Calls the tool. An exception becomes <c>tool-error</c> with a generic message and its text goes
    /// only to the trace; an <see cref="OpenUIToolException"/> keeps its code and message, which the model and the page can read (section 8.2).
    /// </summary>
    ValueTask<OuiValue> InvokeAsync(string name, OuiValue args, CancellationToken ct);
}

/// <summary>A tool failure with a chosen error code, <c>tool-error</c> or <c>mcp-error</c> (language.md, section 8.2).</summary>
/// <param name="message">The error message.</param>
/// <param name="code">The code reported for the failure.</param>
public class OpenUIToolException(string message, OpenUIErrorCode code) : Exception(message)
{
    /// <summary>The code reported for the failure (section 8.2).</summary>
    public OpenUIErrorCode Code { get; } = code;
}

/// <summary>Tools as a map of async functions keyed by name (language.md, section 6.4). Add every tool before sharing the instance.</summary>
public sealed class OpenUITools : IOpenUIToolProvider
{
    private readonly Dictionary<string, (OpenUIToolEffect Effect, Func<OuiValue, CancellationToken, ValueTask<OuiValue>> Handler)> _tools = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public IReadOnlyCollection<string> ToolNames => _tools.Keys;

    /// <summary>Adds or replaces the tool <paramref name="name"/>, which counts as mutating (section 6.4).</summary>
    public OpenUITools Add(string name, Func<OuiValue, CancellationToken, ValueTask<OuiValue>> handler) =>
        Add(name, OpenUIToolEffect.Mutating, handler);

    /// <summary>Adds or replaces the tool <paramref name="name"/> with what it declares it does (section 6.4).</summary>
    public OpenUITools Add(string name, OpenUIToolEffect effect, Func<OuiValue, CancellationToken, ValueTask<OuiValue>> handler)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(handler);
        _tools[name] = (effect, handler);
        return this;
    }

    /// <summary>Adds a mutating tool with typed arguments and result, converted through JSON with no reflection (section 6.4).</summary>
    public OpenUITools Add<TArgs, TResult>(
        string name,
        Func<TArgs, CancellationToken, ValueTask<TResult>> handler,
        JsonTypeInfo<TArgs> args,
        JsonTypeInfo<TResult> result) =>
        Add(name, OpenUIToolEffect.Mutating, handler, args, result);

    /// <summary>
    /// Adds a tool with typed arguments and result and what it declares it does (section 6.4). An argument
    /// of the wrong type fails the call with <c>tool-error</c> naming the argument and the type it takes.
    /// </summary>
    public OpenUITools Add<TArgs, TResult>(
        string name,
        OpenUIToolEffect effect,
        Func<TArgs, CancellationToken, ValueTask<TResult>> handler,
        JsonTypeInfo<TArgs> args,
        JsonTypeInfo<TResult> result)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(result);
        return Add(name, effect, async (value, ct) => OuiValue.From(await handler(TypedToolArgs.Read(name, value, args)!, ct).ConfigureAwait(false), result));
    }

    /// <inheritdoc/>
    public bool Contains(string name) => _tools.ContainsKey(name);

    /// <inheritdoc/>
    public OpenUIToolEffect GetEffect(string name) => _tools[name].Effect;

    /// <inheritdoc/>
    public ValueTask<OuiValue> InvokeAsync(string name, OuiValue args, CancellationToken ct) => _tools[name].Handler(args, ct);
}
