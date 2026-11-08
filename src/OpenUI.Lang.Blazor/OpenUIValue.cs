using System.Collections.Immutable;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor;

/// <summary>
/// Draws any runtime value: a component through the registry, a list item by item, a scalar as text.
/// Null, actions and plain objects draw nothing. A component drawing a child prop uses this.
/// </summary>
public sealed class OpenUIValue : ComponentBase
{
    /// <summary>The value to draw.</summary>
    [Parameter]
    public OuiValue Value { get; set; }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var next = 0;
        Draw(builder, Value, RenderKey.ForSiblings(RuntimeValues.Components(Value)), ref next);
    }

    // A list inside a list draws as one run of siblings, so they share one set of keys.
    private static void Draw(RenderTreeBuilder builder, OuiValue value, ImmutableArray<RenderKey> keys, ref int next)
    {
        if (value.TryGetComponent(out var node))
        {
            builder.OpenComponent<OpenUINode>(0);
            builder.SetKey(keys[next++]);
            builder.AddComponentParameter(1, nameof(OpenUINode.Node), node);
            builder.CloseComponent();
        }
        else if (value.TryGetArray(out var items))
        {
            foreach (var item in items)
            {
                Draw(builder, item, keys, ref next);
            }
        }
        else
        {
            builder.AddContent(2, RuntimeValues.ToText(value));
        }
    }
}
