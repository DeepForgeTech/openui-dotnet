using OpenUI.Lang.Samples.Llm;

namespace OpenUI.Lang.Samples.ChatApi;

/// <summary>The sample's settings, read from environment variables at startup.</summary>
internal sealed record SampleSettings(LlmSettings Llm, string SpecPath, int Port, string? FakeReplyFile)
{
    /// <exception cref="InvalidOperationException">A setting is missing or has a value the sample does not know.</exception>
    public static SampleSettings Read(IConfiguration config, string contentRoot)
    {
        var llm = LlmSettings.Read(config);

        var port = config["OPENUI_API_PORT"] is { Length: > 0 } portText
            ? int.TryParse(portText, out var parsed) && parsed is > 0 and < 65536
                ? parsed
                : throw new InvalidOperationException($"OPENUI_API_PORT must be a port number, not '{portText}'.")
            : 5280;

        var specPath = Path.GetFullPath(
            config["OPENUI_SPEC_PATH"] is { Length: > 0 } spec ? spec : Path.Combine("..", "react-chat", "src", "generated", "spec.json"),
            contentRoot);

        return new SampleSettings(llm, specPath, port, config["OPENUI_FAKE_REPLY_FILE"] is { Length: > 0 } file ? file : null);
    }
}
