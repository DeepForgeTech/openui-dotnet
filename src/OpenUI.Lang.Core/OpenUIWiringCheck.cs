using System.Collections.Immutable;
using System.Text.Json;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;
using OpenUI.Lang.Semantics;
using OpenUI.Lang.Syntax;

namespace OpenUI.Lang;

/// <summary>
/// Finds programs that parse and render but whose state is not wired to do what it looks like: a Query
/// that fetches with a required arg still null or <c>""</c>, a Query that will not fetch again when a
/// choice changes or when another Query it reads arrives (language.md, section 6.1), and a
/// <c>$variable</c> something writes and nothing reads. Opt-in: nothing in the parser or the session
/// calls it. Each finding is an <see cref="OpenUIError"/> with an <c>x-</c> code, so a host can show it
/// and send it back to the model with the parser's own errors. It reads only the program, the library
/// and the tools' input schemas, and changes nothing about how a program runs. Stateless and thread-safe.
/// </summary>
public static class OpenUIWiringCheck
{
    /// <summary>The findings for a finished program, in statement order; empty for one that has no statements (language.md, sections 5 and 6.1).</summary>
    /// <param name="library">The component library the program is written against.</param>
    /// <param name="tools">The tools the program's queries call; a tool's <c>required</c> input properties are the args checked for a blank start.</param>
    /// <param name="program">The program text.</param>
    /// <param name="options">Parser limits; null for the defaults.</param>
    public static OpenUIErrorList Check(OpenUILibrary library, IEnumerable<ToolDescriptor> tools, string program, OpenUIParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(program);

        var document = new OpenUIParser(library, options).Parse(program);
        var statements = document.Statements.Select(statement => statement.Syntax).ToList();
        if (statements.Count == 0)
        {
            return OpenUIErrorList.Empty;
        }

        var facts = new WiringFacts(library, statements);
        var required = new Dictionary<string, ImmutableHashSet<string>>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            required[tool.Name] = RequiredArgs(tool);
        }

        var errors = new List<OpenUIError>();
        foreach (var statement in statements)
        {
            if (statement.Value is CallSyntax { Head: CallHeadKind.Component, Name: "Query" } query)
            {
                errors.AddRange(CheckQuery(facts, required, statement.Name, query));
            }
        }

        errors.AddRange(CheckUnread(facts));
        return OpenUIErrorList.Create(errors);
    }

    private static IEnumerable<OpenUIError> CheckQuery(WiringFacts program, Dictionary<string, ImmutableHashSet<string>> required, string name, CallSyntax query)
    {
        if (query.Arguments.Length < 2)
        {
            yield break;
        }

        var tool = query.Arguments[0] is LiteralSyntax literal && literal.Value.TryGetString(out var text) ? text : null;
        var args = query.Arguments[1];
        var entries = args is ObjectSyntax keyed ? keyed.Entries : [];
        if (tool is not null && required.TryGetValue(tool, out var needed))
        {
            foreach (var entry in entries)
            {
                if (entry.Value is StateReferenceSyntax state && needed.Contains(entry.Key) && program.StartsBlank(state.Name) is { } start)
                {
                    yield return new OpenUIError
                    {
                        Source = OpenUIErrorSource.Query,
                        Code = OpenUIErrorCode.QueryNullArg,
                        StatementId = name,
                        ToolName = tool,
                        Path = $"/args/{entry.Key}",
                        Message = $"{name} sends {entry.Key}: {state.Name}, and {state.Name} starts as {start}, so the first fetch has no {entry.Key} and fails. Declare {state.Name} with a real default.",
                        Hint = $"{state.Name} = <a real {entry.Key}>",
                    };
                }
            }
        }

        foreach (var entry in entries)
        {
            if (program.QueryReachedBy(entry.Value, name) is var (source, through))
            {
                var via = through == source ? "" : $" through {through}";
                yield return new OpenUIError
                {
                    Source = OpenUIErrorSource.Query,
                    Code = OpenUIErrorCode.QueryReadsQuery,
                    StatementId = name,
                    ToolName = tool,
                    Path = $"/args/{entry.Key}",
                    Message = $"{name}'s {entry.Key} reads {source}{via}, another Query's result. A Query runs when the program loads and again only when a $variable in its args changes, so {name} fetches with the defaults of {source} and never again once {source} arrives. Put the value in a $variable with a real default, and let a control set it from {source}.",
                    Hint = $"$picked = <a real {entry.Key}>",
                };
            }
        }

        // The runtime refetches for exactly these names (section 6.1), so anything else is indirect.
        var written = StatementFacts.StateNamesIn(args).ToHashSet(StringComparer.Ordinal);
        foreach (var (state, through) in program.StatesThroughReferences(args))
        {
            if (!written.Contains(state))
            {
                yield return new OpenUIError
                {
                    Source = OpenUIErrorSource.Query,
                    Code = OpenUIErrorCode.QueryIndirectState,
                    StatementId = name,
                    ToolName = tool,
                    Path = "/args",
                    Message = $"{name}'s args read {state} only through {through}, so changing {state} won't fetch again (a Query re-runs only for a $variable written in its args). Write {state} in the args themselves.",
                    Hint = $"{{...: {state}}}",
                };
            }
        }
    }

    private static IEnumerable<OpenUIError> CheckUnread(WiringFacts program)
    {
        foreach (var (state, writer) in program.Unread)
        {
            yield return new OpenUIError
            {
                Source = OpenUIErrorSource.Runtime,
                Code = OpenUIErrorCode.StateUnread,
                StatementId = writer,
                Message = $"{state} is set in {writer} but nothing reads it, so setting it changes nothing on the page. Use {state} where it should take effect, such as a Query's args or an expression, or leave it out.",
            };
        }
    }

    private static ImmutableHashSet<string> RequiredArgs(ToolDescriptor tool) =>
        tool.InputSchema is { ValueKind: JsonValueKind.Object } schema && schema.TryGetProperty("required", out var list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)]
            : [];
}
