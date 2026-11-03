using System.Collections.Frozen;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor;

public static partial class DisplayValues
{
    // The icon name a category stands for when the icon asked for has no match, as upstream's categories do.
    private static readonly FrozenDictionary<string, string> IconCategories = new Dictionary<string, string>
    {
        ["accessibility"] = "accessibility",
        ["account"] = "user",
        ["animals"] = "cat",
        ["arrows"] = "arrow-right",
        ["brands"] = "box",
        ["buildings"] = "building",
        ["charts"] = "chart-line",
        ["communication"] = "message-square",
        ["connectivity"] = "wifi",
        ["cursors"] = "mouse-pointer",
        ["design"] = "palette",
        ["development"] = "code",
        ["devices"] = "smartphone",
        ["emoji"] = "smile",
        ["files"] = "file",
        ["finance"] = "dollar-sign",
        ["food-beverage"] = "cooking-pot",
        ["gaming"] = "gamepad",
        ["home"] = "house",
        ["layout"] = "layout",
        ["mail"] = "mail",
        ["math"] = "calculator",
        ["medical"] = "heart-pulse",
        ["multimedia"] = "music",
        ["nature"] = "tree-pine",
        ["navigation"] = "map-pin",
        ["notifications"] = "bell",
        ["people"] = "users",
        ["photography"] = "camera",
        ["science"] = "microscope",
        ["seasons"] = "sun",
        ["security"] = "shield",
        ["shapes"] = "circle",
        ["shopping"] = "shopping-cart",
        ["social"] = "share-2",
        ["sports"] = "trophy",
        ["sustainability"] = "leaf",
        ["text"] = "type",
        ["time"] = "clock",
        ["tools"] = "wrench",
        ["transportation"] = "car",
        ["travel"] = "plane",
        ["weather"] = "cloud",
    }.ToFrozenDictionary();

    /// <summary>The lucide names to try for an <c>Icon</c> component, best first: its name, then what its category stands for. A view draws the first it has.</summary>
    public static IEnumerable<string> IconNames(OuiComponent icon)
    {
        ArgumentNullException.ThrowIfNull(icon);
        yield return IconName(icon.Text("name"));
        if (IconCategories.TryGetValue(icon.Text("category").Trim().ToLowerInvariant(), out var stand))
        {
            yield return stand;
        }
    }
}
