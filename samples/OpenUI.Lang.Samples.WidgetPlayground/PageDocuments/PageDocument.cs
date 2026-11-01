using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenUI.Lang.Samples.WidgetPlayground.PageDocuments;

/// <summary>
/// A page in its stored form: plain JSON with no OpenUI Lang left in it except custom widget
/// programs. A page is authored as a program, translated to this, and rendered from this.
/// </summary>
/// <param name="Title">The page title.</param>
/// <param name="Columns">How many grid columns the page has.</param>
/// <param name="Variables">The page variables that connect widgets, with their defaults.</param>
/// <param name="Widgets">The widget instances, in grid order.</param>
public sealed record PageDocument(string Title, int Columns, IReadOnlyList<PageVariable> Variables, IReadOnlyList<WidgetInstance> Widgets);

/// <summary>A page variable.</summary>
/// <param name="Name">The name, without <c>$</c>.</param>
/// <param name="Type"><c>string</c>, <c>number</c>, <c>boolean</c> or <c>any</c>, read from the default.</param>
/// <param name="Default">The value the page starts with.</param>
public sealed record PageVariable(string Name, string Type, JsonNode? Default);

/// <summary>One widget on the page.</summary>
/// <param name="Id">The statement that placed it, or its type, with its place on the page. Unique on the page.</param>
/// <param name="Type">The catalog widget name.</param>
/// <param name="Grid">Its size on the grid.</param>
/// <param name="Settings">Params given as literal values.</param>
/// <param name="Bindings">
/// Params given as page variables: param name to variable name. For a custom widget the key is
/// the program's variable name, without <c>$</c>.
/// </param>
public sealed record WidgetInstance(
    string Id,
    string Type,
    GridSize Grid,
    IReadOnlyDictionary<string, JsonNode?> Settings,
    IReadOnlyDictionary<string, string> Bindings);

/// <summary>A widget's size in grid cells.</summary>
/// <param name="W">Columns.</param>
/// <param name="H">Rows.</param>
public sealed record GridSize(int W, int H);

/// <summary>Reads and writes <see cref="PageDocument"/> JSON.</summary>
public static class PageDocumentJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // Keep custom widget programs readable: the JSON is shown as text, never embedded in HTML or script.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // Stored documents are input: a missing field is an error, not a silent null.
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    /// <summary>The document as indented JSON.</summary>
    public static string Serialize(PageDocument document) => JsonSerializer.Serialize(document, Options);

    /// <summary>Reads a stored document.</summary>
    /// <exception cref="JsonException">The JSON is not a valid page document.</exception>
    public static PageDocument Deserialize(string json) =>
        JsonSerializer.Deserialize<PageDocument>(json, Options) ?? throw new JsonException("A page document cannot be null.");
}
