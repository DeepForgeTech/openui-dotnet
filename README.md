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

`OpenUIWiringCheck.Check(library, toolDescriptors, program)` is an opt-in check for programs that run but are not wired the way they look: a required query arg that starts null or empty, a query that reads another query's result or reaches a `$variable` only through another statement, so it never fetches again (language.md, section 6.1), and a `$variable` written and never read. Its findings are `OpenUIError`s with `x-` codes, ready to show beside `page.Errors` and to send back to the model. The package also carries an analyzer: `OUI0001` warns when a reference that may be null becomes an `OuiValue?`, because a null `string`, `OuiArray` or other reference converts to `OuiValue.Null` and the result has a value.

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

`MapOpenUIChat` returns the usual endpoint convention builder, so `.RequireAuthorization()`, `.RequireRateLimiting(...)` and the rest apply. `AddOpenUI` takes a `LibrarySpec`, the registered component sources (no argument; see [Drawing it in Blazor](#drawing-it-in-blazor)), a library built in code (`OpenUILibrary`), or a path to a LibrarySpec file, optional `PromptOptions` (the spec's own `promptOptions` are used when you give none), and an options callback:

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

## Drawing it in Blazor

There are three layers, and each only knows about the one below it:

- `OpenUI.Lang.Core` is the language. It knows nothing about any UI, and its only idea of components is `IComponentSource`: a library for the parser and a spec for the prompt.
- `OpenUI.Lang.Blazor` is the renderer and the seam. It draws a session's tree and takes its components from whatever is registered. It has no components of its own and references no UI library.
- A component package holds the definitions and views for one UI library. `OpenUI.Lang.Blazor.Mud` is the one for MudBlazor: 63 drawn components and their 20 items, and everything they need, such as tabs, tables, dates, safe markdown and icon names. [The samples](samples/README.md) use it.

A host registers a package and gives MudBlazor what it needs from any app. In a project made with `dotnet new blazor --interactivity Server --all-interactive`:

```sh
dotnet add package OpenUI.Lang.Blazor.Mud
```

`Program.cs`:

```csharp
using MudBlazor.Services;
using OpenUI.Lang.Blazor.Mud;

builder.Services.AddMudServices();
builder.Services.AddOpenUIComponents<MudComponents>();
```

`Components/App.razor`, the stylesheet in `<head>` and the script after the framework's:

```razor
<link rel="stylesheet" href="@Assets["_content/MudBlazor/MudBlazor.min.css"]" />
<script src="@Assets["_content/MudBlazor/MudBlazor.min.js"]"></script>
```

`Components/Layout/MainLayout.razor`, around `@Body`:

```razor
<MudThemeProvider />
<MudPopoverProvider />
```

`Components/_Imports.razor`:

```razor
@using MudBlazor
@using OpenUI.Lang.Blazor
@using OpenUI.Lang.Blazor.Mud
```

A host that also serves the model adds `OpenUI.Lang.AspNetCore` and `builder.Services.AddOpenUI();`, and the prompt is composed from what is registered.

Without the stylesheet the page is bare, and without the script and the popover provider a program's `Select` does not open. MudBlazor needs an interactive layout, which is why the template is made `--all-interactive`: with interactivity set page by page, the layout is static and the providers do nothing.

Then a page draws a program with `<OpenUIProgram Program="@text" />`, with no library to pass. A client that sends the request itself injects `OpenUIComponents` and calls `components.SystemPrompt()` for the same prompt. An API that draws nothing can register the same source, or `ComponentSources.FromSpec(spec)`, as an `IComponentSource`.

The Mud package also lets a person choose light, dark or their system's setting, and remembers the choice in the browser:

```csharp
builder.Services.AddMudThemeMode(ThemeMode.System, yourMudTheme); // Both arguments are optional.
```

```razor
@* In App.razor's <head>: colours the page before anything is drawn. *@
<ThemeModeHead />

@* In the layout, in place of MudThemeProvider. *@
<ThemeModeProvider>
    <ThemeSwitcher Compact="true" />
</ThemeModeProvider>
```

The switcher is drawn in the theme's primary colour, and Mud's default app bar is that colour in light mode. To put it in the bar, give your theme a different `AppbarBackground` and an `AppbarText` to suit it, as the sample's does. A program can draw the same control, because `ThemeSwitcher` is a registered component too. Without prerendering, nothing is drawn until the stored choice is known, so the first draw has the right palette. A prerendered page can't do that, because the server can't read the browser's storage: it shows the host's default palette over the right page colour until the circuit starts, then changes.

Every registered source adds to one set, so a program can mix components from all of them and the prompt teaches them all:

- A later source wins a clash on a name, in its definition and its view. A source with views and no definitions just redraws those names.
- The root is the first one a source declares, in registration order. Adding components to a package does not change what its programs start from.
- A component is only taught where a slot lists it, so one that should sit inside another source's component names that component in `Slots`. A program can put it anywhere either way.
- A spec's `promptOptions` add up: every source's additional rules, examples and tool examples are kept in order, and a later source wins only the other options it sets.
- With nothing registered, `OpenUIProgram` and `MapOpenUIChat` fail with a message that says what to register.

### Writing a component package

Start with a Razor project that references the renderer. A package for an external UI library references that too.

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>YourPackage</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\OpenUI.Lang.Blazor\OpenUI.Lang.Blazor.csproj" />
  </ItemGroup>
</Project>
```

`_Imports.razor` makes every view an `OpenUIComponent`. Keep the first line: without it `@onclick` still compiles, and does nothing.

```razor
@using Microsoft.AspNetCore.Components.Web
@using OpenUI.Lang.Blazor
@using OpenUI.Lang.Values
@inherits OpenUIComponent
```

Write a view per component. `Text`, `Number`, `Flag`, `Value`, `Items` and `ItemComponents` read props, `OpenUIValue` draws a component the program put in one, `Input` and `Write` read and write a field, and `RunAsync` runs an action.

`ColumnView.razor`:

```razor
<div class="column"><OpenUIValue Value="@Value("children")" /></div>
```

`HeadingView.razor`:

```razor
<h2>@Text("text")</h2>
```

`FieldView.razor`:

```razor
<input value="@RuntimeValues.ToText(Input("value"))" @oninput='e => Write("value", e.Value?.ToString())' />
```

`PressView.razor`:

```razor
<button @onclick='() => RunAsync("onPress", Text("text"))'>@Text("text")</button>
```

The source defines the components and says which view draws each. This one class is the parser's library and the system prompt too.

```csharp
using OpenUI.Lang.Blazor;
using OpenUI.Lang.Library;

namespace YourPackage;

public sealed class YourComponents() : LibraryDefinition("yours", "Column",
[
    new("Layout",
    [
        new("Column", "Stacks things.", typeof(ColumnView),
        [
            new("children", new ArrayShape(new ComponentRefShape(["Heading", "Field", "Press"]))) { Required = true },
        ]),
        new("Heading", "A heading.", typeof(HeadingView), [new("text", TypeShape.String) { Required = true }]),
        new("Field", "A text field.", typeof(FieldView),
        [
            new("name", TypeShape.String) { Required = true },
            new("value", TypeShape.String) { Access = PropAccess.ReadWrite },
        ])
        {
            Traits = new ComponentTraits(FieldNameProp: "name", FieldComponentType: "input"),
        },
        new("Press", "A button.", typeof(PressView),
        [
            new("text", TypeShape.String) { Required = true },
            new("onPress", new ActionSlotShape([])),
        ]),
    ], []),
]);
```

A host registers it, and a program can use it:

```csharp
builder.Services.AddOpenUIComponents<YourComponents>();
```

```
$name = ""
root = Column([Heading("Who is it?"), Field("who", $name), Press("Clear", @Set($name, ""))])
```

To add to a package instead of replacing it, register a second source after the first. It can be as small as one component:

```csharp
builder.Services.AddOpenUIComponents(new LibraryDefinition("maps", null,
[
    new("Maps", [new("FieldMap", "A map of a field.", typeof(FieldMapView), [new("field", TypeShape.String) { Required = true }]) { Slots = ["Card"] }], []),
]));
```

A package can also be built from an existing LibrarySpec, as the MudBlazor one is: `new LibraryDefinition(id, spec, items, implement, views)`. The prompt teaches everything in the spec, so give every component that is not an item a view, and check in a test that `MissingViews` is empty.

With tools on, the prompt names upstream's fixed text components (`Button`, `Form`, `FormControl`, `Input`, `Select` and `TextContent`) in its examples whether or not your package draws them.

## Order of authority

The fixtures come first, then the spec. Where upstream's reference implementation does something the spec rules out, this follows the spec. One example: in edit mode, the merge keeps multi-line ternaries intact, as spec §7 requires.

## Status

Version 1.1.0 is four packages: `OpenUI.Lang.Core`, `OpenUI.Lang.AspNetCore`, `OpenUI.Lang.Blazor` and `OpenUI.Lang.Blazor.Mud`. The [samples](samples/README.md) use them.

## License

MIT. The spec and fixtures under `spec/` and `tests/fixtures/` come from thesysdev/openui under its MIT license. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
