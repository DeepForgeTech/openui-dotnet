using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenUI.Lang.Blazor.Mud;
using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;
using OpenUI.Lang.Samples.BlazorApp.Chat;

namespace OpenUI.Lang.Blazor.Tests.Mud;

/// <summary>The prompt the package teaches, against a copy kept here so a change to it is always a deliberate one.</summary>
public sealed class MudPromptTests
{
    private static readonly PromptOptions WithTools = new()
    {
        Tools = [new ToolDescriptor("getForecast", "Reads a forecast.")],
        AdditionalRules = ["Answer in French."],
    };

    private static string Captured(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "captured", file));

    private static async Task<string> EndpointSystemMessageAsync(PromptOptions? options)
    {
        using var model = new RecordingModel();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IChatClient>(model);
        builder.Services.AddOpenUIComponents<MudComponents>();
        builder.Services.AddOpenUI(options);
        await using var app = builder.Build();
        app.MapOpenUIChat("/chat");
        await app.StartAsync();

        using var content = new StringContent("""{"messages":[{"role":"user","content":"hi"}]}""", System.Text.Encoding.UTF8, "application/json");
        using var response = await app.GetTestClient().PostAsync("/chat", content);
        await response.Content.ReadAsStringAsync();

        return model.SystemMessage!;
    }

    [Fact]
    public void Its_prompt_has_not_moved_by_a_byte()
    {
        Assert.Equal(Captured("mud-prompt.txt"), TestLibrary.Mud.SystemPrompt());
        Assert.Equal(Captured("mud-prompt-tools.txt"), TestLibrary.Mud.SystemPrompt(WithTools));
    }

    [Fact]
    public void A_host_that_registers_it_gets_that_prompt_from_the_one_call()
    {
        var components = new OpenUIComponents([new MudComponents()]);

        Assert.Equal(Captured("mud-prompt.txt"), components.SystemPrompt());
        Assert.Equal(Captured("mud-prompt-tools.txt"), components.SystemPrompt(WithTools));
    }

    [Fact]
    public void The_chat_sample_sends_that_prompt() =>
        Assert.Equal(Captured("mud-prompt-chat.txt"), ChatTurns.SystemPrompt(new OpenUIComponents([new MudComponents()])));

    [Fact]
    public async Task The_endpoint_composes_that_prompt_from_the_registered_source()
    {
        Assert.Equal(Captured("mud-prompt.txt"), await EndpointSystemMessageAsync(null));
        Assert.Equal(Captured("mud-prompt-tools.txt"), await EndpointSystemMessageAsync(WithTools));
    }

    private sealed class RecordingModel : IChatClient
    {
        public string? SystemMessage { get; private set; }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            SystemMessage = messages.First().Text;
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The endpoint streams.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
