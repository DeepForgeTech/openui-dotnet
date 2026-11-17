using System.Text;
using BenchmarkDotNet.Attributes;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using OpenUI.Lang.Blazor.Mud;
using OpenUI.Lang.Runtime;

namespace OpenUI.Lang.Blazor.Benchmarks;

/// <summary>
/// A page drawn with the MudBlazor package, under bUnit's renderer: a reply drawn after every chunk as it streams in,
/// and a state write on a large page. What is measured is the session's evaluation plus every view's
/// <c>SetParametersAsync</c> and render, not the browser.
/// </summary>
[MemoryDiagnoser]
public class RenderBenchmarks
{
    /// <summary>Characters per chunk when a program is streamed.</summary>
    private const int ChunkChars = 20;

    private BunitContext _streamContext = null!;
    private BunitContext _pageContext = null!;
    private string[] _prefixes = null!;
    private IRenderedComponent<OpenUIProgram> _page = null!;
    private OpenUISession _session = null!;
    private int _writes;

    /// <summary>A gallery program from the Blazor sample, or <c>dashboard</c>: a table of 100 rows by 6 columns and 100 texts beside it.</summary>
    [Params("cards", "data-display", "forms", "dashboard")]
    public string Name { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _streamContext = NewContext();
        _pageContext = NewContext();
        var text = Name == "dashboard" ? Dashboard(100, 6, 100) : Resource($"gallery/{Name}.oui");
        _prefixes = [.. Enumerable.Range(1, (text.Length + ChunkChars - 1) / ChunkChars).Select(i => text[..Math.Min(i * ChunkChars, text.Length)])];

        var streamed = Draw();
        Check(streamed.Instance.Session.Current.Root is not null, "the streamed program renders nothing");
        Check(streamed.Markup.Length > 1000, "the streamed program draws too little");
        _streamContext.DisposeComponentsAsync().GetAwaiter().GetResult();

        _page = _pageContext.Render<OpenUIProgram>(p => p.Add(x => x.Program, text));
        _session = _page.Instance.Session;
        Check(_session.Current.Root is not null, "the program renders nothing");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _streamContext.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _pageContext.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>A reply drawn after every chunk, then once more when it ends.</summary>
    [Benchmark]
    public void Stream()
    {
        Draw();
        _streamContext.DisposeComponentsAsync().GetAwaiter().GetResult();
    }

    private IRenderedComponent<OpenUIProgram> Draw()
    {
        var cut = _streamContext.Render<OpenUIProgram>(p => p.Add(x => x.IsStreaming, true).Add(x => x.Program, ""));
        foreach (var prefix in _prefixes)
        {
            cut.Render(p => p.Add(x => x.IsStreaming, true).Add(x => x.Program, prefix));
        }

        cut.Render(p => p.Add(x => x.IsStreaming, false).Add(x => x.Program, _prefixes[^1]));
        return cut;
    }

    /// <summary>One write to <c>$n</c>, which only the dashboard's first text reads, and the render it queues.</summary>
    [Benchmark]
    public async Task Write()
    {
        await _page.InvokeAsync(() => _session.State.Set("$n", ++_writes));
        await _page.InvokeAsync(() => { });
    }

    private static BunitContext NewContext()
    {
        var context = new BunitContext();
        context.Services.AddMudServices().AddOpenUIComponents(new MudComponents());
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.SetRendererInfo(new RendererInfo("Server", true));
        return context;
    }

    // A statement per column and per text, so the write changes one small view of many.
    private static string Dashboard(int rows, int columns, int texts)
    {
        var text = new StringBuilder("$n = 0\nroot = Stack([counter, table, ");
        text.Append(string.Join(", ", Enumerable.Range(0, texts).Select(i => $"t{i}"))).Append("])\n");
        text.Append("counter = TextContent(\"n=\" + $n)\n");
        text.Append("table = Table([").Append(string.Join(", ", Enumerable.Range(0, columns).Select(c => $"c{c}"))).Append("])\n");
        for (var c = 0; c < columns; c++)
        {
            text.Append($"c{c} = Col(\"Column {c}\", [").Append(string.Join(", ", Enumerable.Range(0, rows).Select(r => $"\"r{r}c{c}\""))).Append("])\n");
        }

        for (var i = 0; i < texts; i++)
        {
            text.Append($"t{i} = TextContent(\"Sibling text number {i}\")\n");
        }

        return text.ToString();
    }

    private static string Resource(string name)
    {
        using var stream = typeof(RenderBenchmarks).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The resource {name} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void Check(bool condition, string problem)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Setup failed: {problem}.");
        }
    }
}
