using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenUI.Lang.Library;

/// <summary>Makes and composes <see cref="IComponentSource"/>s (prompt.md, section 2).</summary>
public static class ComponentSources
{
    private const string PromptOptionsKey = "promptOptions";

    private static readonly HashSet<string> AddedUp = ["additionalRules", "examples", "toolExamples"];

    /// <summary>A source read from a LibrarySpec, for a host that composes a prompt and has no component package to register (prompt.md, section 2.1).</summary>
    /// <param name="spec">The LibrarySpec.</param>
    /// <exception cref="OpenUILibraryException">The spec breaks the rules for libraries.</exception>
    public static IComponentSource FromSpec(LibrarySpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return new Composed(OpenUILibrary.FromSpec(spec), spec);
    }

    /// <summary>
    /// What the sources add up to, in order: one set of components, so a program can use any of them together and a prompt
    /// teaches them all. A later source wins a clash: its component replaces the earlier one of that name and keeps that one's
    /// place, and its functions, actions and other top-level spec keys replace theirs. The exception is <c>promptOptions</c>:
    /// the additional rules, examples and tool examples of every source are kept, in order, and the later source wins only
    /// the other options it sets. A component with a new name goes after
    /// the earlier ones, with its group. The root is the first one a source declares, so adding components to a package does
    /// not change what its programs start from; a later source can still redraw it by defining that name. One source comes back as it is (prompt.md, section 2.8).
    /// </summary>
    /// <param name="sources">The sources, earliest first.</param>
    /// <exception cref="InvalidOperationException">There are no sources.</exception>
    /// <exception cref="OpenUILibraryException">A slot names no component, or the result breaks the rules for libraries.</exception>
    public static IComponentSource Compose(IEnumerable<IComponentSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        IComponentSource? composed = null;
        foreach (var source in sources)
        {
            composed = composed is null ? source : Merge(composed, source);
        }

        return composed ?? throw new InvalidOperationException(
            "No component source is registered, so there are no components to draw or teach. Register one, such as a component package's source, as an IComponentSource.");
    }

    private static Composed Merge(IComponentSource earlier, IComponentSource later)
    {
        var errors = new List<string>();
        var components = earlier.Library.Components.ToList();
        foreach (var component in later.Library.Components)
        {
            var at = components.FindIndex(existing => existing.Name == component.Name);
            if (at >= 0)
            {
                components[at] = component;
                continue;
            }

            components.Add(component);
            foreach (var slot in later.Slots.GetValueOrDefault(component.Name) ?? [])
            {
                LibraryExtension.AddToSlot(components, slot, component.Name, errors);
            }
        }

        var functions = ByNameLaterWins(earlier.Library.FunctionDefinitions, later.Library.FunctionDefinitions, function => function.Spec.Name);
        var actions = ByNameLaterWins(earlier.Library.Actions, later.Library.Actions, action => action.Name);
        var groups = MergeGroups(earlier, later);
        var library = new OpenUILibrary(earlier.Library.Id, earlier.Library.Version, earlier.Library.RootComponent ?? later.Library.RootComponent, [.. components], functions, actions, groups);

        // Each source has already ruled on the argument order of its own components.
        var (violations, orderFindings) = LibraryRules.Check(library);
        errors.AddRange(violations);
        library.DefinitionWarnings = [.. orderFindings];
        if (errors.Count > 0)
        {
            throw new OpenUILibraryException(errors);
        }

        return new Composed(library, library.ToSpec() with
        {
            Id = earlier.Spec.Id,
            AdditionalProperties = MergeProperties(earlier.Spec.AdditionalProperties, later.Spec.AdditionalProperties),
        });
    }

    // The specs' groups and not the libraries': a source can have cut a spec's notes down to what it teaches.
    private static ImmutableArray<ComponentGroup> MergeGroups(IComponentSource earlier, IComponentSource later)
    {
        var groups = (earlier.Spec.ComponentGroups ?? []).ToList();
        foreach (var group in later.Spec.ComponentGroups ?? [])
        {
            var members = group.Components.RemoveAll(name => earlier.Library.TryGetComponent(name, out _));
            var at = groups.FindIndex(existing => existing.Name == group.Name);
            if (at >= 0)
            {
                var existing = groups[at];
                groups[at] = existing with
                {
                    Components = existing.Components.AddRange(members),
                    Notes = existing.Notes is null && group.Notes is null ? null : [.. existing.Notes ?? [], .. group.Notes ?? []],
                };
            }
            else if (!members.IsEmpty)
            {
                groups.Add(group with { Components = members });
            }
        }

        return [.. groups];
    }

    private static ImmutableArray<KeyValuePair<string, JsonElement>> MergeProperties(
        ImmutableArray<KeyValuePair<string, JsonElement>> earlier,
        ImmutableArray<KeyValuePair<string, JsonElement>> later)
    {
        var before = earlier.FirstOrDefault(property => property.Key == PromptOptionsKey);
        if (before.Key is not null)
        {
            later = [.. later.Select(property => property.Key == PromptOptionsKey ? new(property.Key, MergePromptOptions(before.Value, property.Value)) : property)];
        }

        return ByNameLaterWins(earlier, later, property => property.Key);
    }

    // The lists of text add up in source order, since each source wrote them for its own components. Any other option is the later source's if it sets one.
    private static JsonElement MergePromptOptions(JsonElement earlier, JsonElement later)
    {
        if (earlier.ValueKind != JsonValueKind.Object || later.ValueKind != JsonValueKind.Object)
        {
            return later;
        }

        var merged = JsonNode.Parse(earlier.GetRawText())!.AsObject();
        foreach (var option in later.EnumerateObject().Where(option => option.Value.ValueKind != JsonValueKind.Null))
        {
            var value = JsonNode.Parse(option.Value.GetRawText())!;
            if (AddedUp.Contains(option.Name) && merged[option.Name] is JsonArray existing && value is JsonArray added)
            {
                foreach (var item in added)
                {
                    existing.Add(item?.DeepClone());
                }
            }
            else
            {
                merged[option.Name] = value;
            }
        }

        using var document = JsonDocument.Parse(merged.ToJsonString());
        return document.RootElement.Clone();
    }

    private static ImmutableArray<T> ByNameLaterWins<T>(ImmutableArray<T> earlier, ImmutableArray<T> later, Func<T, string> name) =>
        [.. earlier.Select(item => later.FirstOrDefault(replacement => name(replacement) == name(item), item)), .. later.Where(item => !earlier.Any(existing => name(existing) == name(item)))];

    private sealed record Composed(OpenUILibrary Library, LibrarySpec Spec) : IComponentSource;
}
