namespace OpenUI.Lang.Samples.BlazorApp.Widgets;

/// <summary>A saved widget: a program with a name, and the requests that built it.</summary>
public sealed class Widget
{
    /// <summary>Its id, which is also its address in the builder.</summary>
    public required Guid Id { get; init; }

    /// <summary>What the person called it.</summary>
    public required string Name { get; set; }

    /// <summary>The program that draws it.</summary>
    public required string Program { get; set; }

    /// <summary>The requests that made the program, oldest first, so the builder can show how it got here.</summary>
    public List<string> Requests { get; set; } = [];

    /// <summary>When it was last saved.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
