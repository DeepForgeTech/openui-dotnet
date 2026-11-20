using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenUI.Lang.Blazor.Chat.Tests.Kit;

namespace OpenUI.Lang.Blazor.Chat.Tests;

internal static class ChatTestContext
{
    /// <summary>A test context on an interactive renderer, with the chat over this model and store and the plain kit's components.</summary>
    public static BunitContext Create(IChatClient model, IOpenUIConversationStore store, Action<OpenUIBlazorChatOptions>? configure = null)
    {
        // The default of a second is too short on a busy machine.
        BunitContext.DefaultWaitTimeout = TimeSpan.FromSeconds(30);
        var context = new BunitContext();
        context.Services.AddLogging();
        context.Services.AddOpenUIComponents<ChatKit>();
        context.Services.AddSingleton(model);
        context.Services.AddOpenUIChat(configure).AddConversationStore(_ => store);
        context.SetRendererInfo(new RendererInfo("Server", true));
        return context;
    }

    /// <summary>
    /// Finds and clicks in one step on the renderer's thread, for a page something else is rendering: a piece of a reply
    /// landing between the two replaces the handler that was found.
    /// </summary>
    public static Task ClickWhere<T>(this IRenderedComponent<T> cut, string selector, string text = "")
        where T : IComponent =>
        cut.InvokeAsync(() => cut.FindAll(selector).First(element => element.TextContent.Contains(text)).Click());
}
