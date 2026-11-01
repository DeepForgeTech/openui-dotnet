using Microsoft.AspNetCore.Components;

namespace OpenUI.Lang.Blazor;

/// <summary>
/// Maps a component name from a program to the Blazor component that draws it. This is the seam a
/// host fills in: the renderer knows nothing about any particular component.
/// </summary>
public sealed class ComponentRegistry
{
    private readonly Dictionary<string, Type> _components = new(StringComparer.Ordinal);

    /// <summary>Registers <paramref name="component"/> under <paramref name="name"/>, replacing any earlier one.</summary>
    /// <param name="name">The component name, as programs write it.</param>
    /// <param name="component">A Blazor component that accepts a <c>Node</c> parameter (see <see cref="OpenUIComponent"/>).</param>
    /// <exception cref="ArgumentException"><paramref name="component"/> is not a Blazor component.</exception>
    public ComponentRegistry Add(string name, Type component)
    {
        if (!typeof(IComponent).IsAssignableFrom(component))
        {
            throw new ArgumentException($"{component.Name} is not a Blazor component.", nameof(component));
        }

        _components[name] = component;
        return this;
    }

    /// <summary>The component registered for <paramref name="name"/>, or null.</summary>
    public Type? Find(string name) => _components.GetValueOrDefault(name);
}
