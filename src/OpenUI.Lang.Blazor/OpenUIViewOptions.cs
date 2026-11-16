namespace OpenUI.Lang.Blazor;

/// <summary>
/// How every <see cref="OpenUIView"/> in the app draws, unless a view says otherwise. Register one as a singleton:
/// <c>services.AddSingleton(new OpenUIViewOptions { ShowAllErrors = builder.Environment.IsDevelopment() })</c>.
/// </summary>
public sealed record OpenUIViewOptions
{
    /// <summary>
    /// True to draw the error list under the tree whenever it has errors, such as a broken <c>@Each</c> that draws nothing.
    /// By default errors are drawn only when nothing renders: a fatal error, or no root. Meant for development.
    /// </summary>
    public bool ShowAllErrors { get; init; }
}
