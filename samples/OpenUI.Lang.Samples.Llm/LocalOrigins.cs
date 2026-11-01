using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace OpenUI.Lang.Samples.Llm;

public static class LocalOrigins
{
    /// <summary>
    /// Refuses a POST that a page on another site sends, since a provider can spend money on every
    /// one. Listening on localhost doesn't stop those: any page can post to localhost. A request with
    /// no Origin header doesn't come from a page and is let through. <c>AllowedHosts</c> in
    /// appsettings.json does the same job for a site that rebinds its own name to this machine.
    /// </summary>
    public static IApplicationBuilder UseLocalOriginsOnly(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method)
                && context.Request.Headers.Origin is { Count: > 0 } origin
                && !(Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback))
            {
                await Results.Json(new { error = "Requests from other sites are refused." }, statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);
                return;
            }

            await next();
        });
}
