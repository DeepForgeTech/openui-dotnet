using OpenUI.Lang.Core.Tests.Conformance;
using OpenUI.Lang.Library;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Runtime;

public class OpenUrlPolicyTests
{
    private static OpenUILibrary Library { get; } = ConformanceAdapter.CreateLibrary(FixtureCatalog.LoadLibrary());

    private static async Task<(List<OpenUIHostEvent> Events, IReadOnlyList<string> Codes)> ClickAsync(string url, OpenUISessionOptions? options = null)
    {
        var events = new RecordingEvents();
        await using var session = OpenUISession.Create(Library, (options ?? new OpenUISessionOptions()) with { Events = events });
        session.Load("root = Stack([go])\ngo = Button(\"Go\", @OpenUrl(" + url + "))\n");
        Assert.True(session.Current.Root!["children"].Value.TryGetArray(out var children));
        Assert.True(children[0].TryGetComponent(out var go));
        Assert.True(go["action"].Value.TryGetAction(out var action));
        await session.RunActionAsync(action, new ActionOrigin(go, null));
        await session.WhenIdleAsync();
        return (events.Seen, [.. session.Current.Errors.Select(e => e.Code.Value)]);
    }

    [Theory]
    [InlineData("\"https://example.com/a?b=1\"")]
    [InlineData("\"http://example.com\"")]
    [InlineData("\"HTTPS://EXAMPLE.COM\"")]
    public async Task A_web_address_reaches_the_host(string url)
    {
        var (events, codes) = await ClickAsync(url);

        Assert.Equal("open_url", Assert.Single(events).Type);
        Assert.Empty(codes);
    }

    [Theory]
    [InlineData("\"javascript:alert(document.cookie)\"")]
    [InlineData("\"file:///etc/passwd\"")]
    [InlineData("\"data:text/html,<script>alert(1)</script>\"")]
    [InlineData("\" javascript:alert(1)\"")]
    [InlineData("\"java\\nscript:alert(1)\"")]
    [InlineData("\"/relative/path\"")]
    [InlineData("\"https:evil.example\"")]
    [InlineData("\"http:/evil.example\"")]
    [InlineData("\"https://\"")]
    [InlineData("\"https://a b.example\"")]
    [InlineData("\"\"")]
    [InlineData("42")]
    [InlineData("{a: 1}")]
    [InlineData("null")]
    public async Task Anything_else_gives_a_diagnostic_and_no_event(string url)
    {
        var (events, codes) = await ClickAsync(url);

        Assert.Empty(events);
        Assert.Contains("runtime-error", codes);
    }

    [Fact]
    public async Task A_renderer_can_run_a_to_assistant_action_with_the_form_state()
    {
        var events = new RecordingEvents();
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Events = events });
        session.Load("root = Form(\"f\", [Input(\"name\", \"p\"), Button(\"Send\")])\n");
        session.Forms.Set("f", "name", "Ada", "input");
        Assert.True(session.Current.Root!["children"].Value.TryGetArray(out var children));
        Assert.True(children[1].TryGetComponent(out var button));

        var outcome = await session.RunActionAsync(OuiAction.ToAssistant("Send"), new ActionOrigin(button, "f"));

        Assert.Equal(ActionOutcomeKind.Completed, outcome.Kind);
        await session.WhenIdleAsync();
        var sent = Assert.Single(events.Seen);
        Assert.Equal("continue_conversation", sent.Type);
        Assert.Equal("Send", sent.HumanFriendlyMessage);
        Assert.Equal("f", sent.FormName);
        Assert.Contains("Ada", System.Text.Encoding.UTF8.GetString(Bytes(sent)), StringComparison.Ordinal);
    }

    private static byte[] Bytes(OpenUIHostEvent hostEvent)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            hostEvent.WriteTo(writer);
        }

        return buffer.WrittenSpan.ToArray();
    }

    [Fact]
    public async Task A_host_can_allow_other_schemes_and_refuse_the_defaults()
    {
        var options = new OpenUISessionOptions { AllowedUrlSchemes = ["mailto"] };

        Assert.Single((await ClickAsync("\"mailto:a@example.com\"", options)).Events);
        Assert.Empty((await ClickAsync("\"https://example.com\"", options)).Events);
    }

    private sealed class RecordingEvents : IOpenUIHostEventHandler
    {
        public List<OpenUIHostEvent> Seen { get; } = [];

        public ValueTask OnEventAsync(OpenUIHostEvent hostEvent, CancellationToken ct)
        {
            lock (Seen)
            {
                Seen.Add(hostEvent);
            }

            return ValueTask.CompletedTask;
        }
    }
}
