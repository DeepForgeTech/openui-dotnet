using Microsoft.AspNetCore.Components;
using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor;

/// <summary>How a prop relates to state variables.</summary>
public enum PropAccess
{
    /// <summary>A plain value. A <c>$variable</c> passed here is read once per render.</summary>
    Value,

    /// <summary>May be bound to a variable; the component reads it and never writes it.</summary>
    ReadOnly,

    /// <summary>May be bound to a variable; the component reads it and writes user changes back.</summary>
    ReadWrite,
}

/// <summary>One prop of a component, in positional order.</summary>
/// <param name="Name">The prop name.</param>
/// <param name="Type">Its type.</param>
/// <param name="Description">What the type cannot say: units, ranges, meaning. Empty for none.</param>
public sealed record PropDefinition(string Name, TypeShape Type, string Description = "")
{
    /// <summary>True when a program must pass it. Required props come before optional ones (prompt.md §3.1).</summary>
    public bool Required { get; init; }

    /// <summary>Whether it binds to a variable, and in which direction.</summary>
    public PropAccess Access { get; init; } = PropAccess.Value;

    /// <summary>The value used when the prop is left out, or null for none.</summary>
    public OuiValue? Default { get; init; }

    /// <summary>True when a <c>$variable</c> passed here arrives as a binding rather than its value.</summary>
    public bool Bindable => Access != PropAccess.Value;
}

/// <summary>A component defined once: its props, its description and the Blazor component that draws it.</summary>
/// <param name="Name">The component name programs call.</param>
/// <param name="Description">One line on what it is for.</param>
/// <param name="Renderer">The Blazor component type registered for <paramref name="Name"/>, or null for an <see cref="IsItem"/> one.</param>
/// <param name="Props">The props in positional order.</param>
public sealed record ComponentDefinition(string Name, string Description, Type? Renderer, IReadOnlyList<PropDefinition> Props)
{
    /// <summary>Its form and field roles (language.md §5.3).</summary>
    public ComponentTraits Traits { get; init; }

    /// <summary>
    /// True for an item a parent reads as data and never draws, such as a table's column: it needs no view, and
    /// <see cref="LibraryDefinition.MissingViews"/> leaves it out.
    /// </summary>
    public bool IsItem { get; init; }

    /// <summary>
    /// Where the prompt should say it fits among the components of a source registered earlier: <c>"Parent.prop"</c>, or
    /// <c>"Parent"</c> when the parent has one content slot (prompt.md §2.8). A slot accepts any component whether it lists this one or not (language.md §8.4).
    /// </summary>
    public IReadOnlyList<string> Slots { get; init; } = [];

    /// <summary>The component as the library's builder takes it. The prompt's one line of description carries a note per prop.</summary>
    public ComponentSpec Spec => new(
        Name,
        DescriptionWithNotes(),
        [.. Props.Select(prop => new PropSpec(prop.Name, prop.Type, prop.Required, prop.Default, prop.Bindable))],
        Traits);

    /// <summary>The signature line (prompt.md §5.1), such as <c>Text(text: string, style?: "body" | "muted")</c>.</summary>
    public string Signature => Spec.Signature;

    /// <summary>The prop with this name, or null.</summary>
    public PropDefinition? Prop(string name) => Props.FirstOrDefault(prop => prop.Name == name);

    private string DescriptionWithNotes()
    {
        var notes = Props
            .Select(prop => (prop.Name, Text: string.Join(" ", new[] { prop.Description, AccessNote(prop.Access) }.Where(part => part.Length > 0))))
            .Where(note => note.Text.Length > 0)
            .Select(note => $"{note.Name}: {note.Text}")
            .ToList();
        return notes.Count == 0 ? Description : $"{Description} {string.Join("; ", notes)}.";
    }

    private static string AccessNote(PropAccess access) => access switch
    {
        PropAccess.ReadOnly => "(read-only)",
        PropAccess.ReadWrite => "(read-write)",
        _ => "",
    };
}

/// <summary>A titled slice of a library's component list, with notes printed after it in the prompt.</summary>
/// <param name="Name">The group title.</param>
/// <param name="Components">The components, in prompt order.</param>
/// <param name="Notes">Extra prompt lines, each starting with <c>- </c>.</param>
public sealed record GroupDefinition(string Name, IReadOnlyList<ComponentDefinition> Components, IReadOnlyList<string> Notes);

/// <summary>
/// A set of components, defined once: the parser's library, the system prompt and the Blazor components all come from it, so
/// they cannot disagree. It is the component source a package exposes, by deriving from this class, and a host registers
/// with <c>AddOpenUIComponents</c>. Thread-safe; one instance serves an application.
/// </summary>
public class LibraryDefinition : IComponentSource
{
    private readonly Lazy<DefinedLibrary> _defined;
    private readonly IReadOnlyDictionary<string, Type> _views;
    private readonly Lazy<LibrarySpec> _spec;
    private readonly Lazy<ComponentRegistry> _components;

    /// <summary>Defines a library in code.</summary>
    /// <param name="id">The library id (prompt.md §2.1).</param>
    /// <param name="root">The root component name (prompt.md §2.2), or null for a source that adds to another's.</param>
    /// <param name="groups">The components, grouped.</param>
    /// <param name="views">More views, for components of a source registered earlier: that component's definition stays, and this view draws it.</param>
    public LibraryDefinition(string id, string? root, IReadOnlyList<GroupDefinition> groups, IReadOnlyDictionary<string, Type>? views = null)
        : this(id, root, new Lazy<DefinedLibrary>(() => new DefinedLibrary(BuildLibrary(id, root, groups), ItemsOf(groups), [])), RenderersOf(groups, views))
    {
        Slots = groups
            .SelectMany(group => group.Components)
            .Where(component => component.Slots.Count > 0)
            .ToDictionary(component => component.Name, component => component.Slots, StringComparer.Ordinal);
    }

    /// <summary>Defines a library from a LibrarySpec document, such as one written by another tool.</summary>
    /// <param name="id">The library id (prompt.md §2.1).</param>
    /// <param name="spec">The LibrarySpec.</param>
    /// <param name="items">The components a parent reads as data, which need no view.</param>
    /// <param name="implement">Attaches what a spec cannot carry, such as the components' form and field roles.</param>
    /// <param name="views">Names the view for each component.</param>
    public LibraryDefinition(string id, LibrarySpec spec, IEnumerable<string> items, Action<LibraryImplementation>? implement, Action<LibraryViews> views)
        : this(id, spec, Read(spec, items, implement), views)
    {
    }

    private LibraryDefinition(string id, LibrarySpec spec, DefinedLibrary defined, Action<LibraryViews> views)
        : this(id, spec.Root, new Lazy<DefinedLibrary>(defined), NamedViews(defined, views))
    {
    }

    private LibraryDefinition(string id, string? root, Lazy<DefinedLibrary> defined, IReadOnlyDictionary<string, Type> views)
    {
        Id = id;
        Root = root;
        _defined = defined;
        _views = views;
        _spec = new(() => defined.Value.ToSpec(id));
        _components = new(BuildComponents);
    }

    /// <summary>The library id (prompt.md §2.1).</summary>
    public string Id { get; }

    /// <summary>The root component name (prompt.md §2.2), or null for a source that adds to another's.</summary>
    public string? Root { get; }

    /// <summary>The LibrarySpec document. A server given this one generates the same prompt as <see cref="SystemPrompt"/>.</summary>
    public LibrarySpec Spec => _spec.Value;

    /// <summary>The parser's library, with the components' traits. A server's prompt comes from <see cref="Spec"/>, because this library has no group notes, prompt rules or examples.</summary>
    public OpenUILibrary Library => _defined.Value.Library;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Slots { get; } = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>The Blazor components for the library's names.</summary>
    public ComponentRegistry Components => _components.Value;

    /// <summary>
    /// The components that get drawn and have no view yet. The library and the prompt teach all of them, so a program that
    /// uses one parses without an error and draws the unknown-component line in its place: a package built from a spec checks in
    /// a test that this is empty.
    /// </summary>
    public IReadOnlyList<string> MissingViews => _defined.Value.MissingViews(_views);

    /// <summary>
    /// Every problem with the definition, one sentence each, or none: the rules a library must keep (such as required props
    /// before optional ones), views that are not Blazor components, and <see cref="MissingViews"/>. Registering a definition with
    /// a problem fails when the app starts, so a host checks this in a unit test: <c>Assert.Empty(new MyLibrary().FindProblems())</c>.
    /// </summary>
    public IReadOnlyList<string> FindProblems()
    {
        var problems = new List<string>();
        try
        {
            _ = Library;
        }
        catch (OpenUILibraryException error)
        {
            problems.AddRange(error.Violations);
        }

        foreach (var (name, view) in _views.Where(pair => !typeof(IComponent).IsAssignableFrom(pair.Value)))
        {
            problems.Add($"The view {view.Name} for \"{name}\" is not a Blazor component.");
        }

        if (problems.Count == 0)
        {
            problems.AddRange(MissingViews.Select(name => $"\"{name}\" has no view, so programs that use it draw an unknown component. Give it one, or mark it as an item."));
        }

        return problems;
    }

    /// <summary>The system prompt that teaches a model this library.</summary>
    /// <param name="options">What else the prompt teaches, such as the tools; null for the library's own, if it has any, or else the defaults.</param>
    public string SystemPrompt(PromptOptions? options = null) => Prompting.SystemPrompt.Generate(Spec, options);

    internal void AddViewsTo(ComponentRegistry registry)
    {
        foreach (var (name, view) in _views)
        {
            registry.Add(name, view);
        }
    }

    private static DefinedLibrary Read(LibrarySpec spec, IEnumerable<string> items, Action<LibraryImplementation>? implement)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return new DefinedLibrary(OpenUILibrary.FromSpec(spec, implement), items.ToHashSet(StringComparer.Ordinal), spec.AdditionalProperties);
    }

    private static IReadOnlyDictionary<string, Type> NamedViews(DefinedLibrary defined, Action<LibraryViews> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var views = new LibraryViews(defined.Library);
        configure(views);
        return views.Views;
    }

    private static OpenUILibrary BuildLibrary(string id, string? root, IReadOnlyList<GroupDefinition> groups)
    {
        var builder = OpenUILibrary.CreateBuilder(id);
        if (root is not null)
        {
            builder.Root(root);
        }

        foreach (var group in groups)
        {
            builder.Group(group.Name);
            foreach (var note in group.Notes)
            {
                builder.Group(group.Name, note);
            }

            foreach (var component in group.Components)
            {
                builder.Add(component.Spec, group.Name);
            }
        }

        return builder.Build();
    }

    private static HashSet<string> ItemsOf(IReadOnlyList<GroupDefinition> groups) =>
        [.. groups.SelectMany(group => group.Components).Where(component => component.IsItem).Select(component => component.Name)];

    private static Dictionary<string, Type> RenderersOf(IReadOnlyList<GroupDefinition> groups, IReadOnlyDictionary<string, Type>? views)
    {
        var renderers = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var component in groups.SelectMany(group => group.Components))
        {
            if (component.Renderer is { } renderer)
            {
                renderers[component.Name] = renderer;
            }
        }

        foreach (var (name, view) in views ?? new Dictionary<string, Type>())
        {
            renderers[name] = view;
        }

        return renderers;
    }

    private ComponentRegistry BuildComponents()
    {
        var registry = new ComponentRegistry();
        AddViewsTo(registry);
        return registry;
    }
}
