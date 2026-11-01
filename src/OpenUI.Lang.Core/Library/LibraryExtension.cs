using System.Collections.Immutable;

namespace OpenUI.Lang.Library;

/// <summary>The changes <see cref="OpenUILibrary.Extend"/> applies (prompt.md, section 2.8).</summary>
public sealed class LibraryExtension
{
    private readonly List<(ComponentSpec Component, string[] Slots)> _added = [];
    private readonly List<ComponentSpec> _overridden = [];
    private readonly List<string> _removed = [];
    private readonly List<FunctionDefinition> _functions = [];
    private readonly List<ActionSpec> _actions = [];

    internal LibraryExtension()
    {
    }

    /// <summary>
    /// Appends a component and adds it to the <c>$ref</c> unions of <paramref name="slots"/>: each is
    /// <c>"Parent.prop"</c>, or <c>"Parent"</c> when the parent has one content slot (prompt.md, section 2.8).
    /// </summary>
    public LibraryExtension AddComponent(ComponentSpec component, params ReadOnlySpan<string> slots)
    {
        ArgumentNullException.ThrowIfNull(component);
        _added.Add((component, slots.ToArray()));
        return this;
    }

    /// <summary>Replaces a component by name; slots that named the old one name the new one (prompt.md, section 2.8).</summary>
    public LibraryExtension OverrideComponent(ComponentSpec component)
    {
        ArgumentNullException.ThrowIfNull(component);
        _overridden.Add(component);
        return this;
    }

    /// <summary>Drops a component from the list, from every slot union and from the groups (prompt.md, section 2.8).</summary>
    public LibraryExtension RemoveComponent(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _removed.Add(name);
        return this;
    }

    /// <summary>Adds a custom function (language.md, section 3.6).</summary>
    public LibraryExtension AddFunction(FunctionDefinition function)
    {
        ArgumentNullException.ThrowIfNull(function);
        _functions.Add(function);
        return this;
    }

    /// <summary>Adds a custom action (language.md, section 6.3).</summary>
    public LibraryExtension AddAction(ActionSpec action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _actions.Add(action);
        return this;
    }

    internal OpenUILibrary ApplyTo(OpenUILibrary library)
    {
        var errors = new List<string>();
        var components = library.Components.ToList();
        var groups = library.Groups.ToList();

        foreach (var (component, slots) in _added)
        {
            if (components.Exists(c => c.Name == component.Name))
            {
                errors.Add($"Component \"{component.Name}\" already exists.");
                continue;
            }

            components.Add(component);
            foreach (var slot in slots)
            {
                AddToSlot(components, slot, component.Name, errors);
            }
        }

        foreach (var component in _overridden)
        {
            var index = components.FindIndex(c => c.Name == component.Name);
            if (index < 0)
            {
                errors.Add($"Component \"{component.Name}\" cannot be overridden: it does not exist.");
            }
            else
            {
                components[index] = component;
            }
        }

        foreach (var name in _removed)
        {
            if (name == library.RootComponent)
            {
                errors.Add($"Component \"{name}\" is the root and cannot be removed.");
                continue;
            }

            if (components.RemoveAll(c => c.Name == name) == 0)
            {
                errors.Add($"Component \"{name}\" cannot be removed: it does not exist.");
                continue;
            }

            for (var i = 0; i < components.Count; i++)
            {
                components[i] = WithProps(components[i], [.. components[i].Props.Select(p => p with { Type = Without(p.Type, name, components[i].Name, p.Name, errors) })]);
            }

            groups = [.. groups.Select(g => g with { Components = g.Components.Remove(name) })];
        }

        var functions = library.FunctionDefinitions.ToList();
        var actions = library.Actions.ToList();
        foreach (var function in _functions)
        {
            if (functions.Exists(f => f.Spec.Name == function.Spec.Name) || actions.Exists(a => a.Name == function.Spec.Name))
            {
                errors.Add($"Function \"{function.Spec.Name}\" already exists.");
            }
            else
            {
                functions.Add(function);
            }
        }

        foreach (var action in _actions)
        {
            if (actions.Exists(a => a.Name == action.Name) || functions.Exists(f => f.Spec.Name == action.Name))
            {
                errors.Add($"Action \"{action.Name}\" already exists.");
            }
            else
            {
                actions.Add(action);
            }
        }

        var result = new OpenUILibrary(null, null, library.RootComponent, [.. components], [.. functions], [.. actions], [.. groups]);
        var (violations, orderFindings) = LibraryRules.Check(result);
        errors.AddRange(violations);

        // Breaks inherited from a spec stay warnings; a new one is this extension's own definition.
        errors.AddRange(orderFindings.Except(library.DefinitionWarnings));
        result.DefinitionWarnings = [.. orderFindings.Intersect(library.DefinitionWarnings)];
        return errors.Count == 0 ? result : throw new OpenUILibraryException(errors);
    }

    private static void AddToSlot(List<ComponentSpec> components, string slot, string added, List<string> errors)
    {
        var dot = slot.IndexOf('.');
        var parentName = dot < 0 ? slot : slot[..dot];
        var index = components.FindIndex(c => c.Name == parentName);
        if (index < 0)
        {
            errors.Add($"Slot \"{slot}\" names no component.");
            return;
        }

        var parent = components[index];
        var candidates = dot < 0
            ? parent.Props.Where(p => FindRefs(p.Type) is not null).ToList()
            : parent.Props.Where(p => p.Name == slot[(dot + 1)..]).ToList();
        if (candidates.Count != 1 || FindRefs(candidates[0].Type) is null)
        {
            errors.Add($"Slot \"{slot}\" is not one component slot.");
            return;
        }

        var target = candidates[0];
        components[index] = WithProps(parent, parent.Props.Replace(target, target with { Type = With(target.Type, added) }));
    }

    // A signature is derived from the schema (prompt.md, section 2.4), so a slot that changes prints again.
    // One the change does not reach stays as written: a spec's signature may say more than its schema.
    private static ComponentSpec WithProps(ComponentSpec component, ImmutableArray<PropSpec> props)
    {
        var printed = SignatureText.Write(component.Name, props);
        return printed == SignatureText.Write(component.Name, component.Props)
            ? component with { Props = props }
            : component with { Props = props, Signature = printed };
    }

    private static ComponentRefShape? FindRefs(TypeShape shape) => shape switch
    {
        ComponentRefShape refs => refs,
        ArrayShape array => FindRefs(array.Item),
        _ => null,
    };

    private static TypeShape With(TypeShape shape, string added) => shape switch
    {
        ComponentRefShape { Components.IsEmpty: true } any => any,
        ComponentRefShape refs => refs.Components.Contains(added) ? refs : new ComponentRefShape(refs.Components.Add(added)),
        ArrayShape array => new ArrayShape(With(array.Item, added)),
        _ => shape,
    };

    private static TypeShape Without(TypeShape shape, string removed, string owner, string prop, List<string> errors)
    {
        switch (shape)
        {
            case ComponentRefShape refs when refs.Components.Contains(removed):
                if (refs.Components.Length == 1)
                {
                    errors.Add($"Removing \"{removed}\" leaves {owner}.{prop} with no type it accepts.");
                    return shape;
                }

                return new ComponentRefShape(refs.Components.Remove(removed));
            case ArrayShape array:
                return new ArrayShape(Without(array.Item, removed, owner, prop, errors));
            case UnionShape union:
                return new UnionShape([.. union.Options.Select(o => Without(o, removed, owner, prop, errors))]);
            default:
                return shape;
        }
    }
}
