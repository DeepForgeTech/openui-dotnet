using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using OpenUI.Lang.Blazor;
using OpenUI.Lang.Library;

namespace OpenUI.Lang.Samples.WidgetPlayground.Tests;

/// <summary>This project has no MudBlazor anywhere in its graph, so a package drawn here cannot be leaning on it.</summary>
public sealed class ComponentPackageTests : BunitContext
{
    private sealed class HeadingView : OpenUIComponent
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "h2");
            builder.AddContent(1, Text("text"));
            builder.CloseElement();
        }
    }

    [Fact]
    public void A_program_draws_with_only_a_source_registered_and_no_MudBlazor_loaded()
    {
        Services.AddOpenUIComponents(new LibraryDefinition("own", "Heading",
        [
            new("All", [new("Heading", "A heading.", typeof(HeadingView), [new("text", TypeShape.String) { Required = true }])], []),
        ]));

        Renderer.SetRendererInfo(new RendererInfo("Server", true));
        var cut = Render<OpenUIProgram>(parameters => parameters.Add(component => component.Program, "root = Heading(\"Hello\")\n"));

        Assert.Equal("Hello", cut.Find("h2").TextContent);
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), assembly => assembly.GetName().Name!.StartsWith("MudBlazor", StringComparison.Ordinal));
    }
}
