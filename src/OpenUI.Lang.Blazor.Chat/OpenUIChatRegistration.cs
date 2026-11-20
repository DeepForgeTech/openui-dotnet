using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenUI.Lang.Blazor;
using OpenUI.Lang.Blazor.Chat;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the Blazor chat.</summary>
public static class OpenUIChatRegistration
{
    /// <summary>
    /// Registers the chat: its options, the <see cref="IOpenUIChatBackend"/> and the <see cref="IOpenUIConversationStore"/>.
    /// Replies come from the <see cref="IChatClient"/> in the container unless <see cref="OpenUIChatBuilder.UseEndpoint(Uri, Func{IServiceProvider, HttpClient}?)"/>
    /// says otherwise, and conversations are kept in memory unless <see cref="OpenUIChatBuilder.AddConversationStore{TStore}"/> says otherwise.
    /// The components come from <c>AddOpenUIComponents</c>, which the host calls too.
    /// </summary>
    /// <param name="services">The app's services.</param>
    /// <param name="configure">Sets the <see cref="OpenUIBlazorChatOptions"/>. It runs once, here.</param>
    /// <returns>A builder to choose the backend and the store.</returns>
    public static OpenUIChatBuilder AddOpenUIChat(this IServiceCollection services, Action<OpenUIBlazorChatOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new OpenUIBlazorChatOptions();
        configure?.Invoke(options);
        services.Replace(ServiceDescriptor.Singleton(options));
        services.TryAddScoped<IOpenUIConversationStore, OpenUIInMemoryConversationStore>();
        var builder = new OpenUIChatBuilder(services);
        if (!services.Any(service => service.ServiceType == typeof(IOpenUIChatBackend)))
        {
            builder.UseChatClient();
        }

        return builder;
    }
}

/// <summary>Chooses where the chat's replies come from and where its conversations are kept.</summary>
public sealed class OpenUIChatBuilder
{
    // Used when the container has no HttpClient of its own.
    private static readonly Lazy<HttpClient> SharedHttp = new(() => new HttpClient());

    internal OpenUIChatBuilder(IServiceCollection services) => Services = services;

    /// <summary>The app's services.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Replies come from the <see cref="IChatClient"/> in the container, in this process, through <see cref="OpenUIChatClientBackend"/>.
    /// This is the default, and suits Blazor Server.
    /// </summary>
    /// <returns>This builder.</returns>
    public OpenUIChatBuilder UseChatClient() => UseBackend(provider => new OpenUIChatClientBackend(
        provider.GetService<IChatClient>() ?? throw new InvalidOperationException("The chat needs an IChatClient in the container, or UseEndpoint."),
        provider.GetRequiredService<OpenUIComponents>(),
        provider.GetRequiredService<OpenUIBlazorChatOptions>()));

    /// <summary>
    /// Replies come from a server's <c>MapOpenUIChat</c> endpoint, through <see cref="OpenUIChatEndpointBackend"/>, which suits Blazor
    /// WebAssembly. The server builds the prompt from its own component sources, so register the same ones here.
    /// </summary>
    /// <param name="endpoint">The endpoint's absolute address. In WebAssembly, build it from the app's base address: <c>new Uri(new Uri(builder.HostEnvironment.BaseAddress), "api/chat")</c>.</param>
    /// <param name="httpClient">The client to post with. By default the <see cref="HttpClient"/> in the container, or else a shared one.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="endpoint"/> is relative.</exception>
    public OpenUIChatBuilder UseEndpoint(Uri endpoint, Func<IServiceProvider, HttpClient>? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        // On Unix a path such as /api/chat parses as an absolute file: address, so the scheme is checked too.
        if (!endpoint.IsAbsoluteUri || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                $"The chat endpoint must be an absolute http or https address, not '{endpoint.OriginalString}'. In WebAssembly, build it from the app's base address: new Uri(new Uri(builder.HostEnvironment.BaseAddress), \"{endpoint.OriginalString}\").",
                nameof(endpoint));
        }

        return UseBackend(provider => new OpenUIChatEndpointBackend(
            httpClient?.Invoke(provider) ?? provider.GetService<HttpClient>() ?? SharedHttp.Value,
            endpoint,
            provider.GetRequiredService<OpenUIBlazorChatOptions>()));
    }

    /// <summary>Replies come from the endpoint at this absolute address. See <see cref="UseEndpoint(Uri, Func{IServiceProvider, HttpClient}?)"/>.</summary>
    /// <param name="endpoint">The endpoint, such as <c>https://example.com/api/chat</c>.</param>
    /// <param name="httpClient">The client to post with.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="endpoint"/> is relative.</exception>
    public OpenUIChatBuilder UseEndpoint(string endpoint, Func<IServiceProvider, HttpClient>? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
        return UseEndpoint(new Uri(endpoint, UriKind.RelativeOrAbsolute), httpClient);
    }

    /// <summary>Replies come from a backend of the host's own, created once per scope.</summary>
    /// <param name="backend">Creates the backend.</param>
    /// <returns>This builder.</returns>
    public OpenUIChatBuilder UseBackend(Func<IServiceProvider, IOpenUIChatBackend> backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        Services.Replace(ServiceDescriptor.Scoped(backend));
        return this;
    }

    /// <summary>Conversations are kept by <typeparamref name="TStore"/>, created once per scope, so it can keep each person's apart.</summary>
    /// <typeparam name="TStore">The store.</typeparam>
    /// <returns>This builder.</returns>
    public OpenUIChatBuilder AddConversationStore<TStore>()
        where TStore : class, IOpenUIConversationStore
    {
        Services.Replace(ServiceDescriptor.Scoped<IOpenUIConversationStore, TStore>());
        return this;
    }

    /// <summary>Conversations are kept by the store this returns, asked for once per scope. It may hand back a shared store.</summary>
    /// <param name="store">Finds or creates the store.</param>
    /// <returns>This builder.</returns>
    public OpenUIChatBuilder AddConversationStore(Func<IServiceProvider, IOpenUIConversationStore> store)
    {
        ArgumentNullException.ThrowIfNull(store);
        Services.Replace(ServiceDescriptor.Scoped(store));
        return this;
    }
}
