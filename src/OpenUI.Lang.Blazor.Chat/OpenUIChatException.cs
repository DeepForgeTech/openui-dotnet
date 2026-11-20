using System.Net;

namespace OpenUI.Lang.Blazor.Chat;

/// <summary>
/// A reply failed, with a message that is safe to show a person: it names no host, key or prompt. A backend throws it for a
/// failure it can describe. <see cref="OpenUIChatSession"/> shows its message, and only its message: any other exception
/// becomes a fixed sentence and is logged.
/// </summary>
public sealed class OpenUIChatException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The sentence to show.</param>
    /// <param name="statusCode">The HTTP status the server answered with, when there was one.</param>
    /// <param name="innerException">What went wrong underneath, for the log.</param>
    public OpenUIChatException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status the server answered with, or null.</summary>
    public HttpStatusCode? StatusCode { get; }
}
