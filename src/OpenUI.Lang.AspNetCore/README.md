# OpenUI.Lang.AspNetCore

The ASP.NET Core chat endpoint for the stock [OpenUI](https://github.com/thesysdev/openui) react-ui chat, built on [`OpenUI.Lang.Core`](https://www.nuget.org/packages/OpenUI.Lang.Core). It builds the system prompt from your component library, streams the reply of any [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai) `IChatClient` as the chunks `react-headless` reads, and has no provider code of its own.

```sh
dotnet add package OpenUI.Lang.AspNetCore
```

```csharp
using Microsoft.Extensions.AI;
using OpenUI.Lang.Library;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IChatClient>(/* any Microsoft.Extensions.AI chat client */);

var library = OpenUILibrary.CreateBuilder("demo")
    .Root("Stack")
    .Add(new ComponentSpec("Stack", "Lays out its children", [new PropSpec("children", new ArrayShape(new ComponentRefShape([])), true)]))
    .Add(new ComponentSpec("Text", "One line of text", [new PropSpec("value", TypeShape.String, true)]))
    .Build();
builder.Services.AddOpenUI(library);   // or AddOpenUI("library.json") for a LibrarySpec file

var app = builder.Build();
app.MapOpenUIChat("/api/chat");
app.Run();
```

`MapOpenUIChat` returns an endpoint convention builder, so `.RequireAuthorization()` and `.RequireRateLimiting(...)` apply. The request body is treated as untrusted: it is size and message limited, a `system` message from the browser is dropped, and a model failure reaches the client as a fixed message. The [repository README](https://github.com/DeepForgeTech/openui-dotnet#openuilangaspnetcore) has the options and a full example.

This is an independent project, not part of upstream OpenUI. MIT license. See `THIRD_PARTY_NOTICES.md` in the package for the upstream material.
