namespace OpenUI.Lang.Samples.WidgetPlayground.Generation;

/// <summary>New text for the editor from the prompt loop.</summary>
/// <param name="Text">The program to show and render.</param>
/// <param name="Streaming">True while the model is still writing; the editor is read-only and queries wait.</param>
public sealed record ProgramUpdate(string Text, bool Streaming);
