using Bunit;
using Microsoft.Extensions.DependencyInjection;
using OpenUI.Lang.Blazor.Mud;
using OpenUI.Lang.Blazor.Tests.TestComponents;

namespace OpenUI.Lang.Blazor.Tests.Mud;

/// <summary>A host that teaches the package's definitions and draws them with views of its own, registering none of the package's.</summary>
public sealed class HostSourceTests
{
    /// <summary>Mud's Card definition, the host's CardView: only what is public, as a host outside this repository has it.</summary>
    private sealed class HostComponents() : LibraryDefinition("host-design", MudDefinitions.Taught, MudDefinitions.Items, MudDefinitions.Implement, views => views
        .Add<HostCardView>("Card"));

    [Theory]
    [InlineData(typeof(MudDefinitions))]
    [InlineData(typeof(MudIcons))]
    [InlineData(typeof(ChartValues))]
    [InlineData(typeof(DateValues))]
    [InlineData(typeof(DisplayValues))]
    public void What_a_host_view_is_built_from_is_public(Type type)
    {
        // These tests see the packages' internals, so only this says a host outside them can use these.
        Assert.True(type.IsPublic);
    }

    [Fact]
    public void Its_prompt_teaches_the_package_card_signature()
    {
        var card = MudDefinitions.Taught.Components.Single(component => component.Name == "Card");

        Assert.Contains(card.Signature, new HostComponents().SystemPrompt(), StringComparison.Ordinal);
        Assert.Equal(TestLibrary.Mud.SystemPrompt(), new HostComponents().SystemPrompt().Replace("host-design", "openui-mudblazor", StringComparison.Ordinal));
    }

    [Fact]
    public async Task It_draws_a_card_with_the_host_markup_and_none_of_mud()
    {
        await using var context = TestLibrary.NewContext(services => services.AddOpenUIComponents(new HostComponents()));

        var cut = context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, "root = Card([TextContent(\"Inside\")], \"sunk\")\n"));

        var card = cut.Find("section.host-card");
        Assert.Equal("sunk", card.GetAttribute("data-variant"));
        Assert.Contains("TextContent", card.TextContent);
        Assert.DoesNotContain("mud-", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void It_reports_every_component_it_has_no_view_for()
    {
        var drawn = MudDefinitions.Taught.Components
            .Select(component => component.Name)
            .Where(name => !MudDefinitions.Items.Contains(name) && name != "Card");

        var missing = new HostComponents().MissingViews;

        Assert.Equal(drawn.Order(StringComparer.Ordinal), missing.Order(StringComparer.Ordinal));
        Assert.Contains("TextContent", missing);
        Assert.DoesNotContain("Card", missing);
    }
}
