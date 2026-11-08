using System.Collections.Immutable;
using System.Text.Json;
using OpenUI.Lang.Library;

namespace OpenUI.Lang.Blazor;

/// <summary>A library as its definition gives it, whether or not it has a view for each component.</summary>
/// <param name="Library">The whole library, with the components' traits.</param>
/// <param name="Items">The components a parent reads as data, which need no view.</param>
/// <param name="Extras">Top-level LibrarySpec keys to carry over, such as <c>promptOptions</c>.</param>
internal sealed record DefinedLibrary(OpenUILibrary Library, IReadOnlySet<string> Items, ImmutableArray<KeyValuePair<string, JsonElement>> Extras)
{
    public IReadOnlyList<string> MissingViews(IReadOnlyDictionary<string, Type> views) =>
        [.. Library.Components.Select(component => component.Name).Where(name => !Items.Contains(name) && !views.ContainsKey(name))];

    public LibrarySpec ToSpec(string id) => Library.ToSpec() with { Id = id, AdditionalProperties = Extras };
}
