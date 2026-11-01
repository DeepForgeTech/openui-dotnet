using OpenUI.Lang.Blazor;
using OpenUI.Lang.Library;
using OpenUI.Lang.Runtime;

namespace OpenUI.Lang.Samples.WidgetPlayground.Widgets;

/// <summary>
/// One custom widget's session, with what the side panel shows of it: the host events it sent and
/// whether a query is loading.
/// </summary>
/// <remarks>
/// The session raises <see cref="OpenUISession.Invalidated"/> from whichever thread committed a
/// change, such as a query that finished, so <see cref="Changed"/> can fire off the render thread.
/// Subscribers marshal with <c>InvokeAsync</c>.
/// </remarks>
public sealed class ProgramSession : IOpenUIHostEventHandler, IAsyncDisposable
{
    private readonly List<OpenUIHostEvent> _events = [];

    /// <summary>Creates a session for programs of <paramref name="library"/>.</summary>
    /// <param name="library">The component library programs are parsed against.</param>
    /// <param name="tools">The tools <c>Query</c> and <c>Mutation</c> call, by name.</param>
    public ProgramSession(OpenUILibrary library, IOpenUIToolProvider? tools)
    {
        var limits = OpenUILimits.Default with { MaxValueNodes = OpenUIProgram.DefaultMaxValueNodes };
        Session = OpenUISession.Create(library, new OpenUISessionOptions { Tools = tools, Events = this, Limits = limits });
        Feed = new ProgramFeed(Session);
        Session.Invalidated += (_, _) => Changed?.Invoke();
    }

    /// <summary>Raised after anything a render shows may have changed: a parse, state, query results or an event.</summary>
    public event Action? Changed;

    /// <summary>The runtime: state, queries and the evaluated view.</summary>
    public OpenUISession Session { get; }

    /// <summary>Takes the program text as it is written.</summary>
    public ProgramFeed Feed { get; }

    /// <summary>True while a query fetch is in flight.</summary>
    public bool Loading => Session.Current.Queries.Values.Any(query => query.IsLoading || query.IsRefetching);

    /// <summary>Host events the program's actions sent, oldest first.</summary>
    public IReadOnlyList<OpenUIHostEvent> Events
    {
        get
        {
            lock (_events)
            {
                return [.. _events];
            }
        }
    }

    /// <inheritdoc />
    public ValueTask OnEventAsync(OpenUIHostEvent e, CancellationToken ct)
    {
        lock (_events)
        {
            _events.Add(e);
        }

        Changed?.Invoke();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Changed = null;
        return Session.DisposeAsync();
    }
}
