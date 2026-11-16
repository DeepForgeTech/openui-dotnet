using System.Text.RegularExpressions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using OpenUI.Lang.Blazor.Mud;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Samples.BlazorApp;
using OpenUI.Lang.Samples.BlazorApp.Components.Pages;
using OpenUI.Lang.Samples.BlazorApp.Spray;

namespace OpenUI.Lang.Blazor.Tests.Mud;

public sealed class GalleryTests
{
    public static TheoryData<string> Names() => [.. ProgramFiles.ReadFolder("gallery").Select(program => program.Name)];

    private static BunitContext NewContext()
    {
        var context = TestLibrary.NewContext(services => services.AddMudServices().AddOpenUIComponents<MudComponents>().AddOpenUIComponents<SprayComponents>());
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Render<MudPopoverProvider>();
        context.Render<MudDialogProvider>();
        return context;
    }

    [Fact]
    public void The_gallery_draws_every_component_a_program_can_name_itself()
    {
        var programs = string.Concat(ProgramFiles.ReadFolder("gallery").Select(program => ProgramFiles.Read($"gallery/{program.Name}")));
        var registered = new OpenUIComponents([new MudComponents(), new SprayComponents()]);
        var drawn = registered.Library.Components.Where(component => !MudDefinitions.Items.Contains(component.Name)).Select(component => component.Name).ToList();

        Assert.Equal(65, drawn.Count);
        var notDrawn = drawn.Where(name => !Regex.IsMatch(programs, $@"\b{name}\(")).ToList();
        Assert.Empty(notDrawn);
    }

    [Fact]
    public void The_gallery_folder_holds_programs()
    {
        Assert.NotEmpty(ProgramFiles.ReadFolder("gallery"));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task A_gallery_program_draws_with_no_errors_and_no_unknown_component(string name)
    {
        await using var context = NewContext();
        var errors = new List<OpenUIErrorList>();

        var cut = context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, ProgramFiles.Read($"gallery/{name}"))
            .Add(component => component.OnErrors, (OpenUIErrorList list) => errors.Add(list)));

        Assert.All(errors, list => Assert.Empty(list));
        Assert.Empty(cut.Instance.Session.Current.Errors);
        Assert.DoesNotContain("oui-diagnostic", cut.Markup);
        Assert.DoesNotContain("mud-secondary-text", cut.Markup);
        Assert.NotEmpty(cut.Markup);
    }

    [Fact]
    public async Task The_gallery_page_says_it_is_canned_and_draws_each_program_under_a_heading_the_nav_links_to()
    {
        await using var context = NewContext();

        var cut = context.Render<Gallery>();

        Assert.Contains("no model is involved", cut.Markup);
        var files = ProgramFiles.ReadFolder("gallery").Select(program => Path.GetFileNameWithoutExtension(program.Name)).ToList();
        Assert.Equal(files.Count, cut.FindAll("section").Count);
        Assert.Equal(files, cut.FindAll("section").Select(section => section.Id));
        Assert.Equal(files.Select(file => $"/gallery#{file}"), cut.FindAll("nav a").Select(link => link.GetAttribute("href")));
        Assert.Contains("Data display", cut.FindAll("section > h2").Select(heading => heading.TextContent));
    }
}
