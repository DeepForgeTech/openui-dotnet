using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor;

/// <summary>One component of the tree, and the boundary that keeps its failure to itself (language.md §8.2).</summary>
internal sealed class OpenUINode : ErrorBoundaryBase
{
    private OuiComponent? _failed;

    [Parameter, EditorRequired]
    public OuiComponent Node { get; set; } = null!;

    [CascadingParameter]
    public OpenUIContext Context { get; set; } = null!;

    protected override void OnParametersSet()
    {
        // Reporting the failure invalidates the session, which renders this node again. Only a node
        // that really changed gets another try; anything else would fail and report in a loop.
        if (_failed is not null && !((OuiValue)_failed).Equals(Node))
        {
            _failed = null;
            Recover();
        }
    }

    protected override Task OnErrorAsync(Exception exception)
    {
        _failed = Node;
        try
        {
            Context.Session.ReportRenderError(Node, exception);
        }
        catch (ObjectDisposedException)
        {
            // The session went away with its owner, and nobody is left to tell.
        }

        return Task.CompletedTask;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var view = Context.View;
        if (CurrentException is not null)
        {
            builder.AddContent(0, view.FailedContent ?? OpenUIView.DefaultFailed, Node);
        }
        else if (Node.IsUnknown || view.Components.Find(Node.Type) is not { } component)
        {
            // A half-written name is unknown too, so this waits for the stream to end.
            if (!Context.Session.Current.IsStreaming)
            {
                builder.AddContent(1, view.UnknownContent ?? OpenUIView.DefaultUnknown, Node);
            }
        }
        else if (Node.DeclaredFormName is { } form)
        {
            builder.OpenComponent<CascadingValue<string>>(2);
            builder.AddComponentParameter(3, "Name", OpenUIContext.FormCascade);
            builder.AddComponentParameter(4, "Value", form);
            builder.AddComponentParameter(5, "ChildContent", (RenderFragment)(child => DrawComponent(child, component)));
            builder.CloseComponent();
        }
        else
        {
            DrawComponent(builder, component);
        }
    }

    private void DrawComponent(RenderTreeBuilder builder, Type component)
    {
        builder.OpenComponent(6, component);
        builder.AddComponentParameter(7, nameof(OpenUIComponent.Node), Node);
        builder.CloseComponent();
    }
}
