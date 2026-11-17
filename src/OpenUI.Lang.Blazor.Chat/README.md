# OpenUI.Lang.Blazor.Chat

A chat for [OpenUI Lang](https://github.com/thesysdev/openui) in Blazor. Each reply is a program, drawn while it streams with the components a host registers, such as [OpenUI.Lang.Blazor.Mud](https://www.nuget.org/packages/OpenUI.Lang.Blazor.Mud)'s. It holds the send loop, the stored conversations and plain HTML components to draw them, and references no UI library.

```sh
dotnet add package OpenUI.Lang.Blazor.Chat
```

Blazor Server, with any Microsoft.Extensions.AI `IChatClient` in the container:

```csharp
builder.Services.AddOpenUIComponents<YourComponents>();
builder.Services.AddOpenUIChat(options => options.PromptOptions = new PromptOptions { AdditionalRules = ["Reply only with a program."] });
```

Blazor WebAssembly, against a server that maps `MapOpenUIChat` from OpenUI.Lang.AspNetCore:

```csharp
builder.Services.AddOpenUIComponents<YourComponents>();
builder.Services.AddOpenUIChat().UseEndpoint("api/chat");
```

```razor
@page "/chat/{Id:guid?}"
@inject NavigationManager Navigation

<OpenUIChat ConversationId="Id" ConversationIdChanged="@(id => Navigation.NavigateTo($"/chat/{id}", replace: true))" />

@code {
    [Parameter] public Guid? Id { get; set; }
}
```

Link `_content/OpenUI.Lang.Blazor.Chat/chat.css`, and `mud.css` after it on a MudBlazor site.

- `OpenUIChat` draws a conversation and its message box. `EmptyContent`, `Composer`, `PendingContent`, `ErrorContent` and `FailureContent` replace its parts, `Tools` connects the replies to the host's tools, and `OnOpenUrl` and `OnHostEvent` hand it the actions it does not handle itself.
- `OpenUIConversationList` lists the stored conversations, as links with `Href` or buttons with `OnSelect`.
- `OpenUIChatSession` is the send loop with no UI, for a host that draws its own chat.
- `IOpenUIConversationStore` keeps conversations. The default keeps them in memory; `AddConversationStore<T>()` puts a host's own in its place, registered scoped.
- In endpoint mode the server owns the prompt, so register the same component sources on the client as on the server.

The [repository README](https://github.com/DeepForgeTech/openui-dotnet#a-chat-in-blazor) has the options and full examples.

This is an independent project, not part of upstream OpenUI. MIT license. See `THIRD_PARTY_NOTICES.md` in the package for the upstream material.
