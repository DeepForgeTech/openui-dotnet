using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace OpenUI.Lang.Library;

/// <summary>
/// A component library: the components, custom functions and custom actions a program may use
/// (prompt.md, section 2). Immutable and thread-safe; one instance serves a whole application.
/// </summary>
public sealed class OpenUILibrary
{
    private readonly FrozenDictionary<string, ComponentSpec> _components;
    private readonly FrozenDictionary<string, FunctionDefinition> _functions;
    private readonly FrozenDictionary<string, ActionSpec> _actions;

    internal OpenUILibrary(
        string? id,
        string? version,
        string? rootComponent,
        ImmutableArray<ComponentSpec> components,
        ImmutableArray<FunctionDefinition> functions,
        ImmutableArray<ActionSpec> actions,
        ImmutableArray<ComponentGroup> groups)
    {
        Id = id;
        Version = version;
        RootComponent = rootComponent;
        Components = components;
        FunctionDefinitions = functions;
        Functions = [.. functions.Select(f => f.Spec)];
        Actions = actions;
        Groups = groups;
        _components = ByName(components, c => c.Name);
        _functions = ByName(functions, f => f.Spec.Name);
        _actions = ByName(actions, a => a.Name);
    }

    // Tolerates a duplicate, so the definition checks can report it as a violation (prompt.md, section 3.1).
    private static FrozenDictionary<string, T> ByName<T>(ImmutableArray<T> items, Func<T, string> name)
    {
        var byName = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            byName[name(item)] = item;
        }

        return byName.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>The library name (prompt.md, section 2.1), or null.</summary>
    public string? Id { get; }

    /// <summary>The library version (prompt.md, section 2.1), or null.</summary>
    public string? Version { get; }

    /// <summary>The root component used by the fallback entry rule (language.md, section 2.2), or null.</summary>
    public string? RootComponent { get; }

    /// <summary>The components, in the prompt's listing order (prompt.md, section 4.4).</summary>
    public ImmutableArray<ComponentSpec> Components { get; }

    /// <summary>The custom function declarations (language.md, section 3.6).</summary>
    public ImmutableArray<FunctionSpec> Functions { get; }

    /// <summary>The custom actions (language.md, section 6.3).</summary>
    public ImmutableArray<ActionSpec> Actions { get; }

    /// <summary>The component groups (prompt.md, section 4.4).</summary>
    public ImmutableArray<ComponentGroup> Groups { get; }

    /// <summary>
    /// The required-before-optional breaks (prompt.md, section 3.1) a spec read by <see cref="FromSpec"/>
    /// arrived with, one sentence each. Positional binding follows the spec's own order regardless.
    /// </summary>
    public ImmutableArray<string> DefinitionWarnings { get; internal set; } = [];

    internal ImmutableArray<FunctionDefinition> FunctionDefinitions { get; }

    /// <summary>Starts a library definition (prompt.md, section 2).</summary>
    public static OpenUILibraryBuilder CreateBuilder(string? id = null) => new(id);

    /// <summary>
    /// Builds a library from a LibrarySpec (prompt.md, section 2). <paramref name="implement"/> attaches
    /// function implementations and component traits, which the spec cannot carry. Throws
    /// <see cref="OpenUILibraryException"/> listing every rule of prompt.md section 3.1 the spec breaks,
    /// except required-before-optional: that one is reported in <see cref="DefinitionWarnings"/>, because
    /// a spec that arrives as data was already defined elsewhere and programs depend on its order.
    /// </summary>
    public static OpenUILibrary FromSpec(LibrarySpec spec, Action<LibraryImplementation>? implement = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var (components, functions, actions) = SchemaReader.Read(spec);
        var implementation = new LibraryImplementation();
        implement?.Invoke(implementation);

        var builder = CreateBuilder(spec.Id);
        if (spec.Version is not null)
        {
            builder.Version(spec.Version);
        }

        if (spec.Root is not null)
        {
            builder.Root(spec.Root);
        }

        foreach (var group in spec.ComponentGroups ?? [])
        {
            builder.Group(group.Name, group.Notes, group.Components);
        }

        foreach (var component in components)
        {
            builder.Add(component);
        }

        foreach (var (name, traits) in implementation.TraitsByComponent)
        {
            builder.WithTraits(name, traits);
        }

        builder.AddFunctions([.. functions.Select(f => new FunctionDefinition(f, implementation.FunctionsByName.GetValueOrDefault(f.Name)))]);
        builder.AddActions([.. actions]);
        return builder.BuildFromSpec();
    }

    /// <summary>Looks up a component by its case-sensitive name (prompt.md, section 2).</summary>
    public bool TryGetComponent(string name, [NotNullWhen(true)] out ComponentSpec? spec) => _components.TryGetValue(name, out spec);

    /// <summary>Looks up a custom function by its case-sensitive name (without the <c>@</c>) (prompt.md, section 2).</summary>
    public bool TryGetFunction(string name, [NotNullWhen(true)] out FunctionSpec? spec)
    {
        spec = _functions.TryGetValue(name, out var definition) ? definition.Spec : null;
        return spec is not null;
    }

    /// <summary>Looks up a custom action by its case-sensitive name (without the <c>@</c>) (prompt.md, section 2).</summary>
    public bool TryGetAction(string name, [NotNullWhen(true)] out ActionSpec? spec) => _actions.TryGetValue(name, out spec);

    internal OpenUIFunction? GetImplementation(string name) => _functions.TryGetValue(name, out var definition) ? definition.Implementation : null;

    /// <summary>
    /// A new library with the changes <paramref name="configure"/> describes (prompt.md, section 2.8).
    /// This library is unchanged, and the result has no <see cref="Id"/>. Throws
    /// <see cref="OpenUILibraryException"/> for a duplicate name, removing the root, removing the only
    /// type a slot accepts, or a required-before-optional break not already in <see cref="DefinitionWarnings"/>.
    /// </summary>
    public OpenUILibrary Extend(Action<LibraryExtension> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var extension = new LibraryExtension();
        configure(extension);
        return extension.ApplyTo(this);
    }

    /// <summary>The LibrarySpec of this library, as <c>toSpec()</c> returns it (prompt.md, section 2.7). It has no <c>version</c>.</summary>
    public LibrarySpec ToSpec() => new()
    {
        Id = Id,
        Root = RootComponent,
        Schema = SchemaWriter.Write(this),
        Components = [.. Components.Select(c => new LibrarySpecEntry(c.Name, c.Signature, c.Description))],
        ComponentGroups = Groups.IsEmpty ? null : Groups,
        Functions = [.. Functions.Select(f => new LibrarySpecEntry(f.Name, f.Signature, f.Description))],
        Actions = [.. Actions.Select(a => new LibrarySpecEntry(a.Name, a.Signature, a.Description))],
    };

    /// <summary>Writes the validation schema alone, as <c>toJSONSchema()</c> does (prompt.md, section 2.7).</summary>
    public void WriteJsonSchema(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        SchemaWriter.Write(this).WriteTo(writer);
    }
}

/// <summary>Function implementations and component traits attached to a library read from a LibrarySpec (prompt.md, sections 2.5 and 2.7).</summary>
public sealed class LibraryImplementation
{
    internal Dictionary<string, OpenUIFunction> FunctionsByName { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, ComponentTraits> TraitsByComponent { get; } = new(StringComparer.Ordinal);

    /// <summary>Implements the custom function <paramref name="name"/> (language.md, section 3.6).</summary>
    public LibraryImplementation Function(string name, OpenUIFunction implementation)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(implementation);
        FunctionsByName[name] = implementation;
        return this;
    }

    /// <summary>Sets the form and field roles of <paramref name="component"/> (language.md, section 5.3).</summary>
    public LibraryImplementation Traits(string component, ComponentTraits traits)
    {
        ArgumentNullException.ThrowIfNull(component);
        TraitsByComponent[component] = traits;
        return this;
    }
}

/// <summary>A library definition breaks the rules for libraries (prompt.md, sections 3.1 and 2.8).</summary>
public sealed class OpenUILibraryException : Exception
{
    /// <summary>Creates the exception with every violation found (prompt.md, sections 3.1 and 2.8).</summary>
    public OpenUILibraryException(IReadOnlyList<string> violations)
        : base("The library breaks these rules:\n- " + string.Join("\n- ", violations)) => Violations = violations;

    /// <summary>Every violation, one sentence each (prompt.md, sections 3.1 and 2.8).</summary>
    public IReadOnlyList<string> Violations { get; }
}
