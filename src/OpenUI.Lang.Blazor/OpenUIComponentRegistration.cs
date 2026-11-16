using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenUI.Lang.Blazor;
using OpenUI.Lang.Library;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers component sources, which is all a host does to draw their components and teach them.</summary>
public static class OpenUIComponentRegistration
{
    /// <summary>
    /// Registers a component source. Every source adds to one set: a program can use them together and the prompt teaches them all, so a host can add components of its own on top of a
    /// package's. A later source wins a clash on a name, in its definition and its view, and the root is the first one a source declares. Registering the same type again does nothing.
    /// </summary>
    /// <typeparam name="TSource">The source, usually a <see cref="LibraryDefinition"/>. It is created once.</typeparam>
    /// <param name="services">The app's services.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddOpenUIComponents<TSource>(this IServiceCollection services)
        where TSource : class, IComponentSource
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IComponentSource, TSource>());
        services.TryAddSingleton<OpenUIComponents>();
        return services;
    }

    /// <summary>Registers a component source that already exists. See <see cref="AddOpenUIComponents{TSource}(IServiceCollection)"/>.</summary>
    /// <param name="services">The app's services.</param>
    /// <param name="source">The source.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddOpenUIComponents(this IServiceCollection services, IComponentSource source)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(source);
        services.AddSingleton(source);
        services.TryAddSingleton<OpenUIComponents>();
        return services;
    }
}
