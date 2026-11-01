using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Benchmarks;

/// <summary>Reading and writing the rows of a query as JSON: what a tool returns and what a host event carries.</summary>
[MemoryDiagnoser]
public class ValueJsonBenchmarks
{
    private byte[] _json = null!;
    private JsonDocument _document = null!;
    private OuiValue _value;
    private TicketRow[] _rows = null!;
    private ArrayBufferWriter<byte> _buffer = null!;
    private Utf8JsonWriter _writer = null!;

    [ParamsAllValues]
    public Size Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _value = Corpus.Tickets(Corpus.Rows(Size)) is var tickets && tickets.TryGetObject(out var wrapper) ? wrapper["rows"] : default;
        _buffer = new ArrayBufferWriter<byte>();
        _writer = new Utf8JsonWriter(_buffer);
        _value.WriteTo(_writer);
        _writer.Flush();
        _json = _buffer.WrittenSpan.ToArray();
        _document = JsonDocument.Parse(_json);
        _rows = JsonSerializer.Deserialize(_json, BenchJson.Default.TicketRowArray)!;
        Check.That(_rows.Length == Corpus.Rows(Size), "the rows did not round trip");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _document.Dispose();
        _writer.Dispose();
    }

    /// <summary>Bytes to a value in one step, as a tool's reply is read.</summary>
    [Benchmark]
    public OuiValue Read()
    {
        var reader = new Utf8JsonReader(_json);
        return OuiValue.ReadFrom(ref reader);
    }

    /// <summary>A parsed document to a value, without parsing the text.</summary>
    [Benchmark]
    public OuiValue FromElement() => OuiValue.FromJson(_document.RootElement);

    /// <summary>A value to bytes, into a buffer and writer that are reused.</summary>
    [Benchmark]
    public long Write()
    {
        _buffer.ResetWrittenCount();
        _writer.Reset(_buffer);
        _value.WriteTo(_writer);
        _writer.Flush();
        return _buffer.WrittenCount;
    }

    /// <summary>A typed host value to a value, as a typed tool's result is.</summary>
    [Benchmark]
    public OuiValue FromTyped() => OuiValue.From(_rows, BenchJson.Default.TicketRowArray);

    /// <summary>A value to a typed host value, as a typed tool's arguments are.</summary>
    [Benchmark]
    public TicketRow[]? AsTyped() => _value.As(BenchJson.Default.TicketRowArray);
}

/// <summary>A row of <see cref="Corpus.Tickets"/>.</summary>
public sealed record TicketRow(int Id, string Title, string Status, string Team, double Age, string[] Tags);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TicketRow[]))]
internal sealed partial class BenchJson : JsonSerializerContext;
