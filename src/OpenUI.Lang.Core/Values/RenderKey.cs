using System.Collections.Immutable;

namespace OpenUI.Lang.Values;

/// <summary>
/// What tells one component from its siblings when a renderer diffs a list (language.md, section 9.1).
/// A statement referenced twice gives one component in two places, so <see cref="OuiComponent.Id"/> alone repeats.
/// </summary>
/// <param name="Statement">The statement the component came from.</param>
/// <param name="Node">Its place inside that statement.</param>
/// <param name="Repeat">How many earlier siblings have the same statement and place.</param>
public readonly record struct RenderKey(string? Statement, NodeId Node, int Repeat)
{
    /// <summary>A key for each of <paramref name="siblings"/>, unique among them and the same when other content comes or goes (language.md, section 9.1).</summary>
    /// <param name="siblings">The components of one list, in order.</param>
    public static ImmutableArray<RenderKey> ForSiblings(IEnumerable<OuiComponent> siblings)
    {
        ArgumentNullException.ThrowIfNull(siblings);
        var seen = new Dictionary<(string?, NodeId), int>();
        var keys = ImmutableArray.CreateBuilder<RenderKey>();
        foreach (var sibling in siblings)
        {
            var place = (sibling.StatementId, sibling.Id);
            var repeat = seen.GetValueOrDefault(place);
            seen[place] = repeat + 1;
            keys.Add(new RenderKey(sibling.StatementId, sibling.Id, repeat));
        }

        return keys.ToImmutable();
    }
}
