using System.Collections.Immutable;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;

namespace OpenUI.Lang.Benchmarks;

/// <summary>
/// Generating the system prompt: <see cref="SystemPrompt.Generate(LibrarySpec, PromptOptions?)"/>. Small is
/// the fixture library (12 components); Medium and Large are generated libraries of 60 and 300.
/// </summary>
[MemoryDiagnoser]
public class PromptBenchmarks
{
    private LibrarySpec _spec = null!;
    private PromptOptions _full = null!;

    [ParamsAllValues]
    public Size Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _spec = Size switch
        {
            Size.Small => LibrarySpec.Parse(Corpus.LibraryJson),
            Size.Medium => Generated(60),
            _ => Generated(300),
        };

        _full = new PromptOptions
        {
            EditMode = true,
            Tools =
            [
                Tool("listTickets", "Lists tickets", """{"type":"object","properties":{"status":{"type":"string"},"team":{"type":"string"}}}""", """{"type":"object","properties":{"rows":{"type":"array","items":{"type":"object","properties":{"id":{"type":"number"},"title":{"type":"string"},"status":{"type":"string"}}}}}}"""),
                Tool("createTicket", "Creates a ticket", """{"type":"object","properties":{"title":{"type":"string"}},"required":["title"]}""", """{"type":"object","properties":{"id":{"type":"number"}}}"""),
                Tool("closeTicket", "Closes a ticket", """{"type":"object","properties":{"id":{"type":"number"}},"required":["id"]}""", """{"type":"object","properties":{"ok":{"type":"boolean"}}}"""),
            ],
            Examples = ["root = Stack([Text(\"Hello\", \"heading\")])", "root = Stack([Metric(\"Open\", 3)])"],
            ToolExamples = ["tickets = Query(\"listTickets\", {}, {rows: []})\nroot = Stack([Metric(\"Open\", @Count(tickets.rows))])"],
            AdditionalRules = ["Prefer a Table for more than five rows.", "Never invent a tool."],
        };
        Check.That(Default().Length > 0 && Full().Length > Default().Length, "the prompts are empty or the options change nothing");
    }

    /// <summary>The default options: components and syntax only.</summary>
    [Benchmark]
    public string Default() => SystemPrompt.Generate(_spec);

    /// <summary>Tools, bindings, built-ins, edit mode and examples all on.</summary>
    [Benchmark]
    public string Full() => SystemPrompt.Generate(_spec, _full);

    private static ToolDescriptor Tool(string name, string description, string input, string output) =>
        new(name, description, JsonDocument.Parse(input).RootElement.Clone(), JsonDocument.Parse(output).RootElement.Clone());

    private static LibrarySpec Generated(int components)
    {
        var builder = OpenUILibrary.CreateBuilder("generated").Root("Page");
        builder.Add(new ComponentSpec("Page", "The page", [new PropSpec("children", new ArrayShape(new ComponentRefShape([])), true)]));
        for (var i = 1; i <= components; i++)
        {
            builder.Add(new ComponentSpec(
                $"Widget{i}",
                $"Widget number {i}: shows a labeled value, and may hold other widgets",
                [
                    new PropSpec("label", TypeShape.String, true),
                    new PropSpec("value", TypeShape.Number, true),
                    new PropSpec("tone", new EnumShape(["info", "warning", "error"]), false),
                    new PropSpec("note", TypeShape.String, false),
                    new PropSpec("children", new ArrayShape(new ComponentRefShape([])), false),
                ]));
        }

        return builder.Build().ToSpec();
    }
}
