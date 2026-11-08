using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;

namespace OpenUI.Lang.Blazor;

/// <summary>
/// What the registered component sources add up to, for whatever draws a program or sends a prompt on the client.
/// <see cref="OpenUIProgram"/> takes its components from here, and <see cref="SystemPrompt"/> teaches them. A later source
/// wins a clash on a name, and the first root declared stands; see <see cref="ComponentSources.Compose"/>. A source that is not a <see cref="LibraryDefinition"/>
/// has no views, so its components are taught and draw as unknown.
/// </summary>
public sealed class OpenUIComponents
{
    private readonly IComponentSource _composed;

    /// <summary>Composes the sources.</summary>
    /// <param name="sources">The sources, earliest first.</param>
    /// <exception cref="InvalidOperationException">There are no sources.</exception>
    public OpenUIComponents(IEnumerable<IComponentSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var all = sources.ToList();
        _composed = ComponentSources.Compose(all);
        foreach (var definition in all.OfType<LibraryDefinition>())
        {
            definition.AddViewsTo(Components);
        }
    }

    /// <summary>The parser's library, with the components' traits.</summary>
    public OpenUILibrary Library => _composed.Library;

    /// <summary>The LibrarySpec the prompt comes from.</summary>
    public LibrarySpec Spec => _composed.Spec;

    /// <summary>The Blazor components for the library's names.</summary>
    public ComponentRegistry Components { get; } = new();

    /// <summary>The system prompt that teaches a model these components.</summary>
    /// <param name="options">What else the prompt teaches, such as the tools; null for the sources' own, if they have any, or else the defaults.</param>
    public string SystemPrompt(PromptOptions? options = null) => Prompting.SystemPrompt.Generate(Spec, options);
}
