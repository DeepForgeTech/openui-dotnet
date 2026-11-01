using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;

namespace OpenUI.Lang.AspNetCore;

/// <summary>What <c>AddOpenUI</c> builds once: the system prompt, the validator for replies, and the options.</summary>
internal sealed record OpenUIChatSetup(string Prompt, OpenUIValidator Validator, OpenUIChatOptions Options)
{
    public static OpenUIChatSetup Create(LibrarySpec spec, PromptOptions? promptOptions, Action<OpenUIChatOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return Create(OpenUILibrary.FromSpec(spec), spec, promptOptions, configure);
    }

    public static OpenUIChatSetup Create(OpenUILibrary library, PromptOptions? promptOptions, Action<OpenUIChatOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(library);
        return Create(library, library.ToSpec(), promptOptions, configure);
    }

    private static OpenUIChatSetup Create(OpenUILibrary library, LibrarySpec spec, PromptOptions? promptOptions, Action<OpenUIChatOptions>? configure)
    {
        var options = new OpenUIChatOptions();
        configure?.Invoke(options);
        return new OpenUIChatSetup(SystemPrompt.Generate(spec, promptOptions), new OpenUIValidator(library), options);
    }
}
