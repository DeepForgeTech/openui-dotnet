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
/// <param name="Renderer">The Blazor component type registered for <paramref name="Name"/>.</param>
/// <param name="Props">The props in positional order.</param>
public sealed record ComponentDefinition(string Name, string Description, Type Renderer, IReadOnlyList<PropDefinition> Props)
{
    /// <summary>Its form and field roles (language.md §5.3).</summary>
    public ComponentTraits Traits { get; init; }

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
/// A whole component library, defined once. The parser's library, the system prompt and the Blazor
/// components all come from it, so they cannot disagree. Thread-safe; one instance serves an application.
/// </summary>
public sealed class LibraryDefinition
{
    private readonly Lazy<LibrarySpec> _spec;
    private readonly Lazy<OpenUILibrary> _library;
    private readonly Lazy<ComponentRegistry> _components;

    /// <summary>Defines a library.</summary>
    /// <param name="id">The library id (prompt.md §2.1).</param>
    /// <param name="root">The root component name (prompt.md §2.2).</param>
    /// <param name="groups">The components, grouped.</param>
    public LibraryDefinition(string id, string root, IReadOnlyList<GroupDefinition> groups)
    {
        Id = id;
        Root = root;
        Groups = groups;
        _library = new(BuildLibrary);
        _spec = new(() => Library.ToSpec());
        _components = new(BuildComponents);
    }

    /// <summary>The library id (prompt.md §2.1).</summary>
    public string Id { get; }

    /// <summary>The root component name (prompt.md §2.2).</summary>
    public string Root { get; }

    /// <summary>The components, grouped.</summary>
    public IReadOnlyList<GroupDefinition> Groups { get; }

    /// <summary>Every component, in group order.</summary>
    public IEnumerable<ComponentDefinition> AllComponents => Groups.SelectMany(group => group.Components);

    /// <summary>The LibrarySpec document.</summary>
    public LibrarySpec Spec => _spec.Value;

    /// <summary>The parser's library, with the components' traits.</summary>
    public OpenUILibrary Library => _library.Value;

    /// <summary>The Blazor components for the library's names.</summary>
    public ComponentRegistry Components => _components.Value;

    /// <summary>The system prompt that teaches a model this library.</summary>
    /// <param name="options">What else the prompt teaches, such as the tools; null for the defaults.</param>
    public string SystemPrompt(PromptOptions? options = null) => Prompting.SystemPrompt.Generate(Spec, options);

    private OpenUILibrary BuildLibrary()
    {
        var builder = OpenUILibrary.CreateBuilder(Id).Root(Root);
        foreach (var group in Groups)
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

    private ComponentRegistry BuildComponents()
    {
        var registry = new ComponentRegistry();
        foreach (var component in AllComponents)
        {
            registry.Add(component.Name, component.Renderer);
        }

        return registry;
    }
}
