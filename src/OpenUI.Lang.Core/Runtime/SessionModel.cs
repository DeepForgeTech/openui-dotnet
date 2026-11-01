using System.Collections.Immutable;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Runtime;

/// <summary>Options for <see cref="OpenUISession"/> (language.md, sections 5 and 6).</summary>
public sealed record OpenUISessionOptions
{
    /// <summary>The tools queries and mutations call (language.md, section 6.4).</summary>
    public IOpenUIToolProvider? Tools { get; init; }

    /// <summary>Receives action events (section 6.3).</summary>
    public IOpenUIHostEventHandler? Events { get; init; }

    /// <summary>The URL schemes <c>@OpenUrl</c> may send to the host; anything else gets a diagnostic and no event (section 6.3). A valid web address can still carry state off-site, so a host should confirm before opening one.</summary>
    public IReadOnlyCollection<string> AllowedUrlSchemes { get; init; } = ["http", "https"];

    /// <summary>State applied over the defaults at initialization (section 5.1).</summary>
    public IReadOnlyDictionary<string, OuiValue>? PersistedState { get; init; }

    /// <summary>Form state restored when rendering a stored program again (section 5.3).</summary>
    public FormStateSnapshot? PersistedForms { get; init; }

    /// <summary>Budgets (language.md, sections 5 and 6).</summary>
    public OpenUILimits Limits { get; init; } = OpenUILimits.Default;

    /// <summary>The clock behind query refresh timers (section 6.1).</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>The collation <c>@Sort</c> uses (section 3.4).</summary>
    public IOuiCollator Collator { get; init; } = OuiCollator.Default;

    /// <summary>True to end the stream when <see cref="OpenUISession.ConsumeAsync"/> is cancelled or its source fails, so a cut-off reply still renders (section 4, rule 7).</summary>
    public bool FinalizeOnCancel { get; init; } = true;
}

/// <summary>Says that <see cref="OpenUISession.Current"/> is stale (language.md, section 5.1).</summary>
/// <param name="revision">The session revision that made it stale.</param>
public sealed class SessionInvalidatedEventArgs(long revision) : EventArgs
{
    /// <summary>The session revision that made <see cref="OpenUISession.Current"/> stale (language.md, section 5.1).</summary>
    public long Revision { get; } = revision;
}

/// <summary>One immutable evaluated view of a session (language.md, sections 3 to 6).</summary>
public sealed class RenderSnapshot
{
    internal RenderSnapshot(
        long revision,
        bool isStreaming,
        OpenUIDocument document,
        RenderResult render,
        OpenUIErrorList errors,
        ImmutableDictionary<string, QueryStatus> queries)
    {
        Revision = revision;
        IsStreaming = isStreaming;
        Document = document;
        Render = render;
        Errors = errors;
        Queries = queries;
    }

    /// <summary>The session revision this view was evaluated at (language.md, sections 3 to 6).</summary>
    public long Revision { get; }

    /// <summary>True between the start and the end of a stream (section 4).</summary>
    public bool IsStreaming { get; }

    /// <summary>True when an error is fatal: nothing renders (section 8.2).</summary>
    public bool IsFatal => Errors.HasFatal;

    /// <summary>The tree, or null when nothing renders (section 2.2).</summary>
    public OuiComponent? Root => Render.Root;

    /// <summary>The parsed document (language.md, sections 3 to 6).</summary>
    public OpenUIDocument Document { get; }

    /// <summary>Parse, evaluation, query, mutation and render errors, each once (section 8).</summary>
    public OpenUIErrorList Errors { get; }

    /// <summary>The status of each query that has been started, by statement name (section 6.1).</summary>
    public IReadOnlyDictionary<string, QueryStatus> Queries { get; }

    internal RenderResult Render { get; }
}
