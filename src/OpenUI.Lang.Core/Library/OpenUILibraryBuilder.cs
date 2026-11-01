using System.Collections.Immutable;

namespace OpenUI.Lang.Library;

/// <summary>
/// Defines a library in code (prompt.md, section 2). <see cref="Build"/> checks the rules of
/// prompt.md section 3.1 and throws <see cref="OpenUILibraryException"/> listing every violation.
/// </summary>
public sealed class OpenUILibraryBuilder
{
    private readonly string? _id;
    private readonly List<ComponentSpec> _components = [];
    private readonly List<FunctionDefinition> _functions = [];
    private readonly List<ActionSpec> _actions = [];
    private readonly List<GroupDraft> _groups = [];
    private readonly Dictionary<string, ComponentTraits> _traits = new(StringComparer.Ordinal);
    private string? _version;
    private string? _root;

    internal OpenUILibraryBuilder(string? id) => _id = id;

    /// <summary>Sets the library version (prompt.md, section 2.1).</summary>
    public OpenUILibraryBuilder Version(string version)
    {
        _version = version;
        return this;
    }

    /// <summary>Sets the root component (prompt.md, section 2.2) to <typeparamref name="TComponent"/>.</summary>
    public OpenUILibraryBuilder Root<TComponent>()
        where TComponent : IOpenUIComponent<TComponent> => Root(TComponent.Spec.Name);

    /// <summary>Sets the root component (prompt.md, section 2.2).</summary>
    public OpenUILibraryBuilder Root(string componentName)
    {
        _root = componentName;
        return this;
    }

    /// <summary>Adds <typeparamref name="TComponent"/>'s component, optionally to a group (prompt.md, section 4.4).</summary>
    public OpenUILibraryBuilder Add<TComponent>(string? group = null)
        where TComponent : IOpenUIComponent<TComponent> => Add(TComponent.Spec, group);

    /// <summary>Adds a component, optionally to a group (prompt.md, section 4.4).</summary>
    public OpenUILibraryBuilder Add(ComponentSpec component, string? group = null)
    {
        ArgumentNullException.ThrowIfNull(component);
        _components.Add(component);
        if (group is not null)
        {
            GroupNamed(group).Members.Add(component.Name);
        }

        return this;
    }

    /// <summary>Adds custom functions (language.md, section 3.6).</summary>
    public OpenUILibraryBuilder AddFunctions(params ReadOnlySpan<FunctionDefinition> functions)
    {
        foreach (var function in functions)
        {
            _functions.Add(function);
        }

        return this;
    }

    /// <summary>Adds custom actions (language.md, section 6.3).</summary>
    public OpenUILibraryBuilder AddActions(params ReadOnlySpan<ActionSpec> actions)
    {
        foreach (var action in actions)
        {
            _actions.Add(action);
        }

        return this;
    }

    /// <summary>Declares a component group, fixing its position among the groups (prompt.md, section 4.4).</summary>
    public OpenUILibraryBuilder Group(string name, string? notes = null)
    {
        var group = GroupNamed(name);
        if (notes is not null)
        {
            group.Notes = [.. group.Notes ?? [], notes];
        }

        return this;
    }

    /// <summary>Sets the form and field roles of a component (language.md, section 5.3).</summary>
    public OpenUILibraryBuilder WithTraits(string component, ComponentTraits traits)
    {
        _traits[component] = traits;
        return this;
    }

    /// <summary>Checks the definition (prompt.md, section 3.1) and returns the immutable library.</summary>
    public OpenUILibrary Build() => Finish(orderIsFatal: true);

    // A spec that arrives as data was defined elsewhere, and its positional order is already fixed.
    internal OpenUILibrary BuildFromSpec() => Finish(orderIsFatal: false);

    private OpenUILibrary Finish(bool orderIsFatal)
    {
        var components = _components
            .Select(c => _traits.TryGetValue(c.Name, out var traits) ? c with { Traits = traits } : c)
            .ToImmutableArray();
        var groups = _groups.Select(g => new ComponentGroup(g.Name, [.. g.Members], g.Notes)).ToImmutableArray();
        var library = new OpenUILibrary(_id, _version, _root, components, [.. _functions], [.. _actions], groups);

        var (violations, orderFindings) = LibraryRules.Check(library);
        foreach (var name in _traits.Keys.Where(name => !library.TryGetComponent(name, out _)))
        {
            violations.Add($"Traits are set for \"{name}\", which is not a component.");
        }

        if (orderIsFatal)
        {
            violations.AddRange(orderFindings);
        }
        else
        {
            library.DefinitionWarnings = [.. orderFindings];
        }

        return violations.Count == 0 ? library : throw new OpenUILibraryException(violations);
    }

    internal OpenUILibraryBuilder Group(string name, ImmutableArray<string>? notes, ImmutableArray<string> members)
    {
        var group = GroupNamed(name);
        group.Members.AddRange(members);
        group.Notes = notes;
        return this;
    }

    private GroupDraft GroupNamed(string name)
    {
        var group = _groups.Find(g => g.Name == name);
        if (group is null)
        {
            group = new GroupDraft(name);
            _groups.Add(group);
        }

        return group;
    }

    private sealed class GroupDraft(string name)
    {
        public string Name { get; } = name;

        public ImmutableArray<string>? Notes { get; set; }

        public List<string> Members { get; } = [];
    }
}

/// <summary>The definition-time checks of prompt.md, section 3.1 and language.md, section 3.6.</summary>
internal static class LibraryRules
{
    public static (List<string> Violations, List<string> OrderFindings) Check(OpenUILibrary library)
    {
        var violations = new List<string>();
        var orderFindings = new List<string>();
        var componentNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var component in library.Components)
        {
            CheckName("Component", component.Name, violations);
            if (!componentNames.Add(component.Name))
            {
                violations.Add($"Component \"{component.Name}\" is defined twice.");
            }

            CheckOrder($"Component \"{component.Name}\"", component.Props, orderFindings);
        }

        var callables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (kind, name, parameters) in library.Functions.Select(f => ("Function", f.Name, f.Params))
                     .Concat(library.Actions.Select(a => ("Action", a.Name, a.Params))))
        {
            CheckName(kind, name, violations);
            if (componentNames.Contains(name))
            {
                violations.Add($"{kind} \"{name}\" has the name of a component.");
            }

            if (!callables.Add(name))
            {
                violations.Add($"\"{name}\" is defined twice among the functions and actions.");
            }

            CheckOrder($"{kind} \"{name}\"", parameters, orderFindings);
        }

        if (library.RootComponent is { } root && !componentNames.Contains(root))
        {
            violations.Add($"The root \"{root}\" is not a component.");
        }

        foreach (var group in library.Groups)
        {
            foreach (var member in group.Components.Where(m => !componentNames.Contains(m)))
            {
                violations.Add($"Group \"{group.Name}\" lists \"{member}\", which is not a component.");
            }
        }

        return (violations, orderFindings);
    }

    private static void CheckName(string kind, string name, List<string> violations)
    {
        if (!ReservedNames.IsUppercaseIdentifier(name))
        {
            violations.Add($"{kind} \"{name}\" must start with an uppercase letter and match [A-Za-z0-9_].");
        }

        if (ReservedNames.IsReserved(name))
        {
            violations.Add($"{kind} \"{name}\" uses a reserved name.");
        }
    }

    // A required prop that has a default counts as optional here (prompt.md, section 3.1).
    private static void CheckOrder(string owner, ImmutableArray<PropSpec> props, List<string> findings)
    {
        var sawOptional = false;
        foreach (var prop in props)
        {
            if (!prop.IsEffectivelyRequired)
            {
                sawOptional = true;
            }
            else if (sawOptional)
            {
                findings.Add($"{owner}: required \"{prop.Name}\" comes after an optional prop.");
            }
        }
    }
}
