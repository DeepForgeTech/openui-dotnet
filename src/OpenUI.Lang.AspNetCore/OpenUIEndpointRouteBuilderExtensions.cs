using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenUI.Lang.AspNetCore;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Maps the chat endpoint that the stock OpenUI react-ui chat talks to.</summary>
public static class OpenUIEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps <c>POST</c> on <paramref name="pattern"/> to a chat endpoint. It reads the OpenAI-format
    /// <c>messages</c> that react-headless's <c>openAIMessageFormat</c> sends, streams the reply of the
    /// <c>Microsoft.Extensions.AI.IChatClient</c> in the container, and writes it as
    /// <c>application/x-ndjson</c>, one <c>ChatCompletionChunk</c> per line, which is what
    /// <c>openAIReadableStreamAdapter</c> reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The library's system prompt always leads the conversation. A <c>system</c> or <c>developer</c>
    /// message in the request is dropped, so a browser cannot replace the prompt. Only text reaches the
    /// model; parts of other types, such as images, are ignored.
    /// </para>
    /// <para>
    /// A bad request is answered with 400 and a JSON <c>{ "error": "..." }</c> body, 413 for a body
    /// over the size limit and 415 for a body that is not JSON. A model call that fails before the reply starts is answered the same way with
    /// 429, 502 or 504. The exception text is logged and never sent. A failure after the reply has started ends the
    /// stream early, and so does a reply past <c>OpenUILimits.MaxInputChars</c> or <see cref="OpenUIChatOptions.ReplyTimeout"/>. When the client goes away, the model call is cancelled.
    /// </para>
    /// </remarks>
    /// <param name="endpoints">The route builder, such as the <c>WebApplication</c>.</param>
    /// <param name="pattern">The route, such as <c>/api/chat</c>.</param>
    /// <returns>A convention builder, for authorization, rate limiting and the like.</returns>
    /// <exception cref="InvalidOperationException"><c>AddOpenUI</c> was not called, no <c>IChatClient</c> is registered, or <c>AddOpenUI</c> is to compose component sources and there are none.</exception>
    public static IEndpointConventionBuilder MapOpenUIChat(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string pattern)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(pattern);

        var setup = endpoints.ServiceProvider.GetService<OpenUIChatSetup>()
            ?? throw new InvalidOperationException("MapOpenUIChat needs services.AddOpenUI(...) to be called first.");
        if (endpoints.ServiceProvider.GetService<IServiceProviderIsService>() is { } services && !services.IsService(typeof(IChatClient)))
        {
            throw new InvalidOperationException("MapOpenUIChat needs an IChatClient in the container.");
        }

        var logger = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("OpenUI.Lang.AspNetCore.Chat");
        return endpoints.MapPost(pattern, new OpenUIChatEndpoint(setup, logger).HandleAsync).WithDisplayName("OpenUI chat");
    }
}
