# OpenUI Lang for .NET

A .NET implementation of [OpenUI Lang](https://github.com/thesysdev/openui), the compact, streaming-first language that models write to describe UI. It is an independent project, not part of upstream OpenUI. `OpenUI.Lang.Core` implements the language from its specification, [`spec/language.md`](spec/language.md) and [`spec/prompt.md`](spec/prompt.md).

| Package | What it is |
|---|---|
| `OpenUI.Lang.Core` | The language: parse a model's response as it streams, check it against your component library, evaluate state, queries and actions, merge edits, and build the system prompt. No dependencies beyond the .NET base library. |
| `OpenUI.Lang.AspNetCore` | The chat endpoint for the stock `@openuidev/react-ui` chat. It streams the reply of any [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai) `IChatClient`. |

```sh
dotnet add package OpenUI.Lang.Core
dotnet add package OpenUI.Lang.AspNetCore
```

Requires .NET 10. `OpenUI.Lang.AspNetCore` brings `OpenUI.Lang.Core` with it.

## Using it

### OpenUI.Lang.Core

Define the components a model may use, or read a LibrarySpec file with `OpenUILibrary.FromSpec(LibrarySpec.Parse(json))`. Then generate the system prompt and run what the model writes through a session. Here `modelReply` is the reply as an `IAsyncEnumerable<string>` of chunks:

```csharp
using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

var library = OpenUILibrary.CreateBuilder("tickets")
    .Root("Stack")
    .Add(new ComponentSpec("Stack", "Lays out its children", [new PropSpec("children", new ArrayShape(new ComponentRefShape([])), true)]))
    .Add(new ComponentSpec("Metric", "One headline number", [new PropSpec("label", TypeShape.String, true), new PropSpec("value", TypeShape.Number, true)]))
    .Build();

// Send this as the system prompt.
var systemPrompt = SystemPrompt.Generate(library.ToSpec());

// The tools a program's queries and mutations can call. Query only calls tools that declare Read.
var tools = new OpenUITools().Add(
    "countOpenTickets",
    OpenUIToolEffect.Read,
    (args, ct) => new ValueTask<OuiValue>(OuiValue.Number(3)));

await using var session = OpenUISession.Create(library, new OpenUISessionOptions { Tools = tools });

// Feed in the model's reply as it streams. Render session.Current on each session.Invalidated.
await session.ConsumeAsync(modelReply);
await session.WhenIdleAsync();

var page = session.Current;   // page.Root is the tree to render
```

For edit mode, `session.ApplyEdit(patch)` applies the model's changed statements, and `OpenUISerializer.Serialize(root)` (in `OpenUI.Lang.Editing`) writes a tree back out as a program. `OpenUISessionOptions.Events` receives what actions send to the host. Malformed input never throws: it ends in diagnostics on `page.Errors`, and `OpenUILimits` bounds the work and memory one program can take.

### OpenUI.Lang.AspNetCore

A chat endpoint in a new web app:

```sh
dotnet new web -o Chat && cd Chat
dotnet add package OpenUI.Lang.AspNetCore
dotnet add package Microsoft.Extensions.AI.OpenAI
```

Replace `Program.cs`:

```csharp
using Microsoft.Extensions.AI;
using OpenAI;
using OpenUI.Lang.Library;

var builder = WebApplication.CreateBuilder(args);

// Any Microsoft.Extensions.AI provider works. This one is OpenAI's.
builder.Services.AddSingleton<IChatClient>(
    new OpenAIClient(builder.Configuration["OpenAI:ApiKey"]!).GetChatClient("gpt-4.1-mini").AsIChatClient());

var library = OpenUILibrary.CreateBuilder("demo")
    .Root("Stack")
    .Add(new ComponentSpec("Stack", "Lays out its children", [new PropSpec("children", new ArrayShape(new ComponentRefShape([])), true)]))
    .Add(new ComponentSpec("Text", "One line of text", [new PropSpec("value", TypeShape.String, true)]))
    .Build();
builder.Services.AddOpenUI(library);

var app = builder.Build();
app.MapOpenUIChat("/api/chat");
app.Run();
```

Start it with `OpenAI__ApiKey=<your key> dotnet run --urls http://localhost:5000`, then post a chat request:

```sh
curl -N http://localhost:5000/api/chat -H 'Content-Type: application/json' \
  -d '{"messages":[{"role":"user","content":"Say hello"}]}'
```

The reply is `application/x-ndjson`, one OpenAI `chat.completion.chunk` per line, with the model's OpenUI Lang program in `choices[0].delta.content`.

For the stock `@openuidev/react-ui` chat, pass the library spec that `openui generate --spec` writes for it: `builder.Services.AddOpenUI("library.json")`. Point the frontend at the endpoint with `fetchLLM({ url: "/api/chat", streamAdapter: openAIReadableStreamAdapter(), messageFormat: openAIMessageFormat })`, as in [`samples/react-chat`](samples/react-chat).

`MapOpenUIChat` returns the usual endpoint convention builder, so `.RequireAuthorization()`, `.RequireRateLimiting(...)` and the rest apply. `AddOpenUI` takes a `LibrarySpec`, a library built in code (`OpenUILibrary`), or a path to a LibrarySpec file, optional `PromptOptions` (the spec's own `promptOptions` are used when you give none), and an options callback:

```csharp
builder.Services.AddOpenUI("library.json", configure: options =>
{
    options.MaxRequestBodyBytes = 256 * 1024;            // default 1 MiB
    options.MaxMessages = 50;                            // default 100
    options.ReplyTimeout = TimeSpan.FromMinutes(1);      // default 5 minutes
    options.OnReplyCompleted = context =>                // after each reply, with its ValidationReport
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("reply root={Root} errors={Errors}", context.Report.Render.Root?.Type, context.Report.Errors.Count);
        return Task.CompletedTask;
    };
});
```

The request body is treated as untrusted. It must be JSON, within the limits, with OpenAI-format `messages` (string, text-part or null content). A bad request gets a 400 with `{ "error": "..." }` (413 for a body over the size limit, 415 for the wrong content type). The library's system prompt always leads the conversation, and any `system` or `developer` message the browser sends is dropped, so a client can't replace it. If the model fails before the reply starts, the client gets 429, 502 or 504 with a fixed message; the exception text is only logged. If it fails mid-stream, the stream just ends, and the same goes for a reply past `OpenUILimits.MaxInputChars` or past `ReplyTimeout`; the model call is stopped and `OnReplyCompleted` is told how the reply ended.

## Conformance

This implementation is checked against the spec and the conformance fixtures of [thesysdev/openui at commit `7df27bc`](https://github.com/thesysdev/openui/tree/7df27bc8eeb6fd15ac318ca7745d225a8e16e9b9). `spec/` and `tests/fixtures/1.0/` are unchanged copies of that commit's `spec/` and `spec/fixtures/1.0/`. It passes all 59 program cases and all 12 interaction cases.

```sh
dotnet test tests/OpenUI.Lang.Core.Tests --filter Level=1   # parse, stream, evaluate, edit, errors
dotnet test tests/OpenUI.Lang.Core.Tests --filter Level=2   # clicks, typing, forms, queries and mutations
dotnet test                                                 # everything
```

On top of the fixtures:

- **Streaming equals parsing.** Every program case is also fed in one character at a time, and the result has to equal a batch parse (spec §4).
- **Prompts match byte for byte.** `SystemPrompt.Generate` is checked against 25 golden prompts (spec §4.1). The generator is in `tools/prompt-goldens/`.
- **ECMAScript semantics.** Spec §3.1 requires ECMAScript number parsing, number formatting, loose equality and `localeCompare`. These are checked against thousands of values produced by Node. The generator is in `tools/ecma-reference/`.

## Order of authority

The fixtures come first, then the spec. Where upstream's reference implementation does something the spec rules out, this follows the spec. One example: in edit mode, the merge keeps multi-line ternaries intact, as spec §7 requires.

## Status

Version 1.0.0 is `OpenUI.Lang.Core` and `OpenUI.Lang.AspNetCore`. Blazor rendering is being built and is not packaged yet. Its projects are in the tree, and the [samples](samples/README.md) use them.

## License

MIT. The spec and fixtures under `spec/` and `tests/fixtures/` come from thesysdev/openui under its MIT license. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
