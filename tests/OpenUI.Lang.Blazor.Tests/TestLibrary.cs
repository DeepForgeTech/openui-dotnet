using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using OpenUI.Lang.Library;
using OpenUI.Lang.Blazor.Tests.TestComponents;

namespace OpenUI.Lang.Blazor.Tests;

/// <summary>A plain component library, so these tests exercise the renderer and nothing else.</summary>
internal static class TestLibrary
{
    private static readonly ArrayShape AnyList = new(TypeShape.Any);

    /// <summary>The MudBlazor package's source, for a test that names its library rather than registering one.</summary>
    public static OpenUI.Lang.Blazor.Mud.MudComponents Mud { get; } = new();

    /// <summary>The MudBlazor package's definitions, drawn by the views <paramref name="views"/> names.</summary>
    public static LibraryDefinition MudWith(string id, Action<LibraryViews> views) =>
        new(id, OpenUI.Lang.Blazor.Mud.MudDefinitions.Spec, OpenUI.Lang.Blazor.Mud.MudDefinitions.Items, OpenUI.Lang.Blazor.Mud.MudDefinitions.Implement, views);

    public static LibraryDefinition Definition { get; } = new("test", "Stack",
    [
        new("All",
        [
            new("Stack", "A column.", typeof(StackView), [new("children", AnyList) { Required = true }]),
            new("Box", "Holds one value.", typeof(BoxView), [new("child", TypeShape.Any)]),
            new("Text", "A line.", typeof(TextView), [new("text", TypeShape.String) { Required = true }]),
            new("Input", "An input.", typeof(InputView),
            [
                new("label", TypeShape.String) { Required = true },
                new("value", TypeShape.String) { Access = PropAccess.ReadWrite },
                new("rules", new ObjectShape([])),
            ])
            {
                Traits = new ComponentTraits(FieldNameProp: "label", FieldComponentType: "input", FieldRulesProp: "rules"),
            },
            new("Button", "A button.", typeof(ButtonView),
            [
                new("label", TypeShape.String) { Required = true },
                new("action", new ActionSlotShape([])),
            ]),
            new("Form", "A form.", typeof(FormView),
            [
                new("name", TypeShape.String) { Required = true },
                new("children", AnyList) { Required = true },
            ])
            {
                Traits = new ComponentTraits(FormNameProp: "name"),
            },
            new("Fragile", "Throws when its text says boom.", typeof(FragileView), [new("text", TypeShape.String) { Required = true }]),
        ], []),
    ]);

    /// <summary>
    /// Finds and clicks in one step on the renderer's thread, for a page something else is rendering:
    /// a chunk or a query landing between the two replaces the handler that was found.
    /// </summary>
    public static Task ClickWhere<T>(this IRenderedComponent<T> cut, string selector, string text = "")
        where T : IComponent =>
        cut.InvokeAsync(() => cut.FindAll(selector).First(element => element.TextContent.Contains(text)).Click());

    /// <summary>A test context on an interactive renderer, that draws with this plain library.</summary>
    public static BunitContext NewContext(bool interactive = true) => NewContext(Definition, interactive);

    /// <summary>A test context on an interactive renderer, that draws with this source.</summary>
    public static BunitContext NewContext(OpenUI.Lang.Library.IComponentSource source, bool interactive = true) => NewContext(services => services.AddOpenUIComponents(source), interactive);

    /// <summary>A test context with these services; they must go in before the renderer is touched.</summary>
    public static BunitContext NewContext(Action<IServiceCollection> services, bool interactive = true)
    {
        // The default of a second is too short on a busy machine.
        BunitContext.DefaultWaitTimeout = TimeSpan.FromSeconds(30);
        var context = new BunitContext();
        services(context.Services);
        context.SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Server", interactive));
        return context;
    }
}
