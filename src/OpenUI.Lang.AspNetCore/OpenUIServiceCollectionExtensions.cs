using System.Text.Json;
using OpenUI.Lang.AspNetCore;
using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers OpenUI Lang for the chat endpoint that <c>MapOpenUIChat</c> maps.</summary>
public static class OpenUIServiceCollectionExtensions
{
    /// <summary>
    /// Registers a component library. The <see cref="OpenUILibrary"/> and the system prompt are built here,
    /// once, so a bad spec fails at startup. The chat endpoint also needs an
    /// <c>Microsoft.Extensions.AI.IChatClient</c> in the container.
    /// </summary>
    /// <param name="services">The app's services.</param>
    /// <param name="librarySpec">The LibrarySpec, as <c>openui generate --spec</c> writes it or <see cref="OpenUILibrary.ToSpec"/> returns it.</param>
    /// <param name="promptOptions">
    /// What the client supports and extra prompt text. When <see langword="null"/>, the spec's own
    /// <c>promptOptions</c> member is used if it has one.
    /// </param>
    /// <param name="configure">Sets the <see cref="OpenUIChatOptions"/>. It runs once, here.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentException">The prompt options in the spec do not have the documented shape.</exception>
    /// <exception cref="OpenUILibraryException">The spec breaks the rules for libraries.</exception>
    public static IServiceCollection AddOpenUI(
        this IServiceCollection services,
        LibrarySpec librarySpec,
        PromptOptions? promptOptions = null,
        Action<OpenUIChatOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddSingleton(OpenUIChatSetup.Create(librarySpec, promptOptions, configure));
    }

    /// <summary>Registers a library built in code, such as with <see cref="OpenUILibrary.CreateBuilder"/>. See the first overload.</summary>
    /// <param name="services">The app's services.</param>
    /// <param name="library">The component library.</param>
    /// <param name="promptOptions">What the client supports and extra prompt text.</param>
    /// <param name="configure">Sets the <see cref="OpenUIChatOptions"/>. It runs once, here.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddOpenUI(
        this IServiceCollection services,
        OpenUILibrary library,
        PromptOptions? promptOptions = null,
        Action<OpenUIChatOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddSingleton(OpenUIChatSetup.Create(library, promptOptions, configure));
    }

    /// <summary>
    /// Registers the chat endpoint for the <see cref="IComponentSource"/>s in the container: the prompt it sends and the validator
    /// it replies with are composed from them, once, the first time they are asked for, which <c>MapOpenUIChat</c> does.
    /// So a component package's registration is all a host adds, and a later source wins a clash on a name; see
    /// <see cref="ComponentSources.Compose"/>. A host with only a spec registers <see cref="ComponentSources.FromSpec"/>. See the
    /// overload that takes a spec for the rest.
    /// </summary>
    /// <param name="services">The app's services.</param>
    /// <param name="promptOptions">What the client supports and extra prompt text. When <see langword="null"/>, the composed spec's own <c>promptOptions</c> member is used if it has one.</param>
    /// <param name="configure">Sets the <see cref="OpenUIChatOptions"/>. It runs once, when the sources are composed.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddOpenUI(
        this IServiceCollection services,
        PromptOptions? promptOptions = null,
        Action<OpenUIChatOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddSingleton(provider =>
            OpenUIChatSetup.Create(ComponentSources.Compose(provider.GetServices<IComponentSource>()), promptOptions, configure));
    }

    /// <summary>Registers a component library read from a LibrarySpec JSON file. See the first overload.</summary>
    /// <param name="services">The app's services.</param>
    /// <param name="librarySpecPath">The path of the LibrarySpec file.</param>
    /// <param name="promptOptions">What the client supports and extra prompt text. When <see langword="null"/>, the spec's own <c>promptOptions</c> member is used if it has one.</param>
    /// <param name="configure">Sets the <see cref="OpenUIChatOptions"/>. It runs once, here.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The file is missing or does not hold a LibrarySpec.</exception>
    /// <exception cref="ArgumentException">The prompt options in the spec do not have the documented shape.</exception>
    /// <exception cref="OpenUILibraryException">The spec breaks the rules for libraries.</exception>
    public static IServiceCollection AddOpenUI(
        this IServiceCollection services,
        string librarySpecPath,
        PromptOptions? promptOptions = null,
        Action<OpenUIChatOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(librarySpecPath);
        return services.AddOpenUI(ReadSpec(librarySpecPath), promptOptions, configure);
    }

    private static LibrarySpec ReadSpec(string path)
    {
        try
        {
            return LibrarySpec.Parse(File.ReadAllText(path));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            throw new InvalidOperationException($"Could not read the LibrarySpec file {path}: {error.Message}", error);
        }
    }
}
