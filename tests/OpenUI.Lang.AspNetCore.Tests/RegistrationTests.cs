using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;

namespace OpenUI.Lang.AspNetCore.Tests;

public sealed class RegistrationTests
{
    private static readonly string SpecPath = Path.Combine(AppContext.BaseDirectory, "library.json");

    [Fact]
    public async Task The_spec_can_be_a_file_path()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IChatClient>(FakeChatClient.Replying("ok"));
        builder.Services.AddOpenUI(SpecPath);
        await using var app = builder.Build();
        app.MapOpenUIChat("/chat");
        await app.StartAsync();

        using var response = await app.GetTestClient().PostAsync("/chat", ChatHost.Json("""{"messages":[{"role":"user","content":"hi"}]}"""));

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("\"ok\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_library_built_in_code_can_be_registered_instead_of_a_spec()
    {
        var library = OpenUILibrary.FromSpec(ChatHost.Spec());
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        var model = FakeChatClient.Replying("ok");
        builder.Services.AddSingleton<IChatClient>(model);
        builder.Services.AddOpenUI(library);
        await using var app = builder.Build();
        app.MapOpenUIChat("/chat");
        await app.StartAsync();

        using var response = await app.GetTestClient().PostAsync("/chat", ChatHost.Json("""{"messages":[{"role":"user","content":"hi"}]}"""));
        await response.Content.ReadAsStringAsync();

        Assert.Equal(SystemPrompt.Generate(library.ToSpec()), model.Calls[0][0].Text);
    }

    [Fact]
    public void A_missing_or_unreadable_spec_file_fails_at_registration_with_a_clear_message()
    {
        var services = new ServiceCollection();

        var missing = Assert.Throws<InvalidOperationException>(() => services.AddOpenUI(Path.Combine(AppContext.BaseDirectory, "nope.json")));
        Assert.Contains("nope.json", missing.Message);

        var notJson = Path.GetTempFileName();
        try
        {
            File.WriteAllText(notJson, "not json");
            Assert.Throws<InvalidOperationException>(() => services.AddOpenUI(notJson));
            File.WriteAllText(notJson, "[]");
            Assert.Throws<InvalidOperationException>(() => services.AddOpenUI(notJson));
        }
        finally
        {
            File.Delete(notJson);
        }
    }

    [Fact]
    public async Task A_spec_without_a_schema_registers_and_reads_the_signatures_instead()
    {
        // Core reads a library from the signature lines when the document has no schema (prompt.md section 2.3),
        // so a spec without one is valid. It used to be rejected at registration.
        var spec = ChatHost.Spec() with { Schema = null };
        OpenUIReplyCompletedContext? seen = null;
        await using var host = await ChatHost.StartAsync(
            FakeChatClient.Replying("root = Stack([t])\nt = Text(\"Hello\", \"heading\")"),
            options => options.OnReplyCompleted = context =>
            {
                seen = context;
                return Task.CompletedTask;
            },
            spec: spec);

        using var response = await host.PostMessagesAsync(("user", "hi"));
        await response.Content.ReadAsStringAsync();

        Assert.Equal(SystemPrompt.Generate(spec), host.Model.Calls[0][0].Text);
        Assert.NotNull(seen);
        Assert.Equal("Stack", seen.Report.Render.Root?.Type);
        Assert.Empty(seen.Report.Errors);
    }

    [Fact]
    public void Prompt_options_of_the_wrong_shape_in_the_spec_fail_at_registration()
    {
        var spec = ChatHost.WithPromptOptions(ChatHost.Spec(), """{"editMode":"yes"}""");

        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddOpenUI(spec));
    }

    [Fact]
    public async Task Mapping_the_endpoint_without_AddOpenUI_fails_at_startup()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();

        var error = Assert.Throws<InvalidOperationException>(() => app.MapOpenUIChat("/chat"));

        Assert.Contains("AddOpenUI", error.Message);
    }

    [Fact]
    public async Task Mapping_the_endpoint_without_a_chat_client_fails_at_startup()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddOpenUI(ChatHost.Spec());
        await using var app = builder.Build();

        var error = Assert.Throws<InvalidOperationException>(() => app.MapOpenUIChat("/chat"));

        Assert.Contains("IChatClient", error.Message);
    }

    [Fact]
    public void The_limits_must_be_positive()
    {
        var options = new OpenUIChatOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxRequestBodyBytes = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxMessages = -1);
        Assert.Equal(1024 * 1024, options.MaxRequestBodyBytes);
        Assert.Equal(100, options.MaxMessages);
    }

    [Fact]
    public async Task The_options_run_once_at_registration_not_per_request()
    {
        var calls = 0;
        await using var host = await ChatHost.StartAsync(FakeChatClient.Replying("ok"), _ => calls++);

        using var first = await host.PostMessagesAsync(("user", "hi"));
        using var second = await host.PostMessagesAsync(("user", "hi"));

        Assert.Equal(1, calls);
        Assert.Equal(host.Model.Calls[0][0].Text, host.Model.Calls[1][0].Text);
    }
}
