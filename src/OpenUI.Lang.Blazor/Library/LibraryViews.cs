using Microsoft.AspNetCore.Components;
using OpenUI.Lang.Library;

namespace OpenUI.Lang.Blazor;

/// <summary>Names the view for each component of a definition read from a LibrarySpec.</summary>
public sealed class LibraryViews
{
    private readonly OpenUILibrary _library;
    private readonly Dictionary<string, Type> _views = new(StringComparer.Ordinal);

    internal LibraryViews(OpenUILibrary library) => _library = library;

    internal IReadOnlyDictionary<string, Type> Views => _views;

    /// <summary>Draws the component <paramref name="name"/> with <typeparamref name="TView"/>, replacing any earlier view.</summary>
    /// <typeparam name="TView">A Blazor component that accepts a <c>Node</c> parameter (see <see cref="OpenUIComponent"/>).</typeparam>
    /// <param name="name">A component of the library.</param>
    /// <exception cref="ArgumentException">The library has no such component.</exception>
    public LibraryViews Add<TView>(string name)
        where TView : IComponent => Add(name, typeof(TView));

    /// <summary>Draws the component <paramref name="name"/> with <paramref name="view"/>, replacing any earlier view.</summary>
    /// <param name="name">A component of the library.</param>
    /// <param name="view">A Blazor component that accepts a <c>Node</c> parameter (see <see cref="OpenUIComponent"/>).</param>
    /// <exception cref="ArgumentException">The library has no such component.</exception>
    public LibraryViews Add(string name, Type view)
    {
        if (!_library.TryGetComponent(name, out _))
        {
            throw new ArgumentException($"The library has no component \"{name}\".", nameof(name));
        }

        _views[name] = view;
        return this;
    }
}
