using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;

namespace OpenUI.Lang.AspNetCore.Tests;

/// <summary>Every way in reaches the model and the endpoint, each option seen through a real request.</summary>
public sealed class AddOpenUITests
{
    private const string OneMessage = """{"messages":[{"role":"user","content":"hi"}]}""";
    private const string TwoMessages = """{"messages":[{"role":"user","content":"hi"},{"role":"user","content":"again"}]}""";

    private static readonly PromptOptions Rule = new() { AdditionalRules = ["Answer in French."] };

    public static TheoryData<string> Overloads() => ["path", "spec", "library"];

    private static string WriteSpec(string? promptOptions = null)
    {
        var spec = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "library.json")))!;
        if (promptOptions is not null)
        {
            spec["promptOptions"] = JsonNode.Parse(promptOptions);
        }

        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, spec.ToJsonString());
        return path;
    }

    private static async Task<WebApplication> StartAsync(FakeChatClient model, Action<IServiceCollection> addOpenUI)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IChatClient>(model);
        addOpenUI(builder.Services);
        var app = builder.Build();
        app.MapOpenUIChat(ChatHost.Route);
        await app.StartAsync();
        return app;
    }

    private static Action<IServiceCollection> Register(string overload, PromptOptions? promptOptions, Action<OpenUIChatOptions>? configure) => services =>
    {
        switch (overload)
        {
            case "path":
                services.AddOpenUI(Path.Combine(AppContext.BaseDirectory, "library.json"), promptOptions, configure);
                break;
            case "spec":
                services.AddOpenUI(ChatHost.Spec(), promptOptions, configure);
                break;
            default:
                services.AddOpenUI(OpenUILibrary.FromSpec(ChatHost.Spec()), promptOptions, configure);
                break;
        }
    };

    private static async Task<HttpStatusCode> PostAsync(WebApplication app, string body)
    {
        using var response = await app.GetTestClient().PostAsync(ChatHost.Route, ChatHost.Json(body));
        await response.Content.ReadAsStringAsync();
        return response.StatusCode;
    }

    private static async Task<string> SystemMessageAsync(Action<IServiceCollection> addOpenUI)
    {
        var model = FakeChatClient.Replying("ok");
        await using var app = await StartAsync(model, addOpenUI);
        await PostAsync(app, OneMessage);
        var message = model.Calls[0][0];
        Assert.Equal(ChatRole.System, message.Role);
        return message.Text;
    }

    [Theory]
    [MemberData(nameof(Overloads))]
    public async Task Configure_reaches_the_endpoint(string overload)
    {
        var replies = 0;
        await using var app = await StartAsync(FakeChatClient.Replying("ok"), Register(overload, null, options =>
        {
            options.MaxMessages = 1;
            options.OnReplyCompleted = _ =>
            {
                replies++;
                return Task.CompletedTask;
            };
        }));

        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(app, TwoMessages));
        Assert.Equal(HttpStatusCode.OK, await PostAsync(app, OneMessage));
        Assert.Equal(1, replies);
    }

    [Theory]
    [MemberData(nameof(Overloads))]
    public async Task Prompt_options_reach_the_model(string overload)
    {
        var prompt = await SystemMessageAsync(Register(overload, Rule, null));

        var spec = overload == "library" ? OpenUILibrary.FromSpec(ChatHost.Spec()).ToSpec() : ChatHost.Spec();
        Assert.Equal(SystemPrompt.Generate(spec, Rule), prompt);
        Assert.Contains("Answer in French.", prompt);
    }

    [Theory]
    [InlineData("path")]
    [InlineData("spec")]
    public async Task Explicit_prompt_options_beat_the_spec_s_and_the_spec_s_are_the_default(string overload)
    {
        const string FromSpec = """{"additionalRules":["Answer in Latin."]}""";
        var path = WriteSpec(FromSpec);
        try
        {
            Action<IServiceCollection> Add(PromptOptions? options) => services =>
            {
                if (overload == "path")
                {
                    services.AddOpenUI(path, options);
                }
                else
                {
                    services.AddOpenUI(ChatHost.WithPromptOptions(ChatHost.Spec(), FromSpec), options);
                }
            };

            var explicitly = await SystemMessageAsync(Add(Rule));
            Assert.Contains("Answer in French.", explicitly);
            Assert.DoesNotContain("Answer in Latin.", explicitly);

            Assert.Contains("Answer in Latin.", await SystemMessageAsync(Add(null)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static readonly JsonElement InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"city":{"type":"string"}}}""").RootElement;
    private static readonly JsonElement OutputSchema = JsonDocument.Parse("""{"type":"object","properties":{"highF":{"type":"number"}}}""").RootElement;
    private static readonly JsonElement Annotations = JsonDocument.Parse("""{"readOnlyHint":true}""").RootElement;

    // One row for each member of promptOptions. The tools are there because some sections only
    // appear with them.
    public static TheoryData<string, string, PromptOptions> SpecPromptOptionRows() => new()
    {
        { "toolCalls", """{"toolCalls":true}""", new() { ToolCalls = true } },
        { "bindings", """{"bindings":true}""", new() { Bindings = true } },
        { "builtinFunctions", """{"builtinFunctions":true}""", new() { BuiltinFunctions = true } },
        { "editMode", """{"editMode":true}""", new() { EditMode = true } },
        { "inlineMode", """{"inlineMode":true}""", new() { InlineMode = true } },
        { "preamble", """{"preamble":"You design weather dashboards."}""", new() { Preamble = "You design weather dashboards." } },
        { "examples", """{"examples":["root = Text(\"hi\")"]}""", new() { Examples = ["root = Text(\"hi\")"] } },
        { "toolExamples", """{"tools":["t"],"toolExamples":["q = Query(\"t\", {})"]}""", new() { Tools = ["t"], ToolExamples = ["q = Query(\"t\", {})"] } },
        { "additionalRules", """{"additionalRules":["Answer in French."]}""", new() { AdditionalRules = ["Answer in French."] } },
        { "a tool by name", """{"tools":["getForecast"]}""", new() { Tools = ["getForecast"] } },
        {
            "a tool with everything",
            """{"tools":[{"name":"getForecast","description":"The forecast.","inputSchema":{"type":"object","properties":{"city":{"type":"string"}}},"outputSchema":{"type":"object","properties":{"highF":{"type":"number"}}},"annotations":{"readOnlyHint":true}}]}""",
            new() { Tools = [new ToolDescriptor("getForecast", "The forecast.", InputSchema, OutputSchema, Annotations)] }
        },
    };

    [Theory]
    [MemberData(nameof(SpecPromptOptionRows))]
    public async Task Each_prompt_option_in_the_spec_reaches_the_model(string member, string json, PromptOptions expected)
    {
        Assert.NotEmpty(member);
        var spec = ChatHost.Spec();
        var expectedPrompt = SystemPrompt.Generate(spec, expected);
        Assert.NotEqual(SystemPrompt.Generate(spec), expectedPrompt);

        var prompt = await SystemMessageAsync(services => services.AddOpenUI(ChatHost.WithPromptOptions(spec, json)));

        Assert.Equal(expectedPrompt, prompt);
    }

    [Fact]
    public void A_tool_keeps_its_annotations_in_the_spec_though_the_prompt_never_prints_them()
    {
        var spec = ChatHost.WithPromptOptions(ChatHost.Spec(), """{"tools":[{"name":"t","annotations":{"readOnlyHint":true}}]}""");

        Assert.True(spec.ReadPromptOptions()!.Tools[0].Annotations!.Value.GetProperty("readOnlyHint").GetBoolean());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"yes\"")]
    [InlineData("""{"toolCalls":"yes"}""")]
    [InlineData("""{"preamble":1}""")]
    [InlineData("""{"examples":"one"}""")]
    [InlineData("""{"additionalRules":[1]}""")]
    [InlineData("""{"tools":"t"}""")]
    [InlineData("""{"tools":[1]}""")]
    [InlineData("""{"tools":[{}]}""")]
    [InlineData("""{"tools":[{"name":1}]}""")]
    public void Prompt_options_of_the_wrong_shape_fail_at_registration(string json) =>
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddOpenUI(ChatHost.WithPromptOptions(ChatHost.Spec(), json)));

    [Fact]
    public async Task The_sample_s_generated_spec_gives_the_model_what_upstream_s_generator_wrote()
    {
        var generated = Path.Combine(AppContext.BaseDirectory, "generated");

        var prompt = await SystemMessageAsync(services => services.AddOpenUI(Path.Combine(generated, "spec.json")));

        // Upstream's file ends in a newline, and the rest has to match to the byte.
        Assert.Equal(File.ReadAllText(Path.Combine(generated, "prompt.txt")).TrimEnd('\n'), prompt);
    }
}
