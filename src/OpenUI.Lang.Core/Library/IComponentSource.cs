using System.Collections.Immutable;

namespace OpenUI.Lang.Library;

/// <summary>
/// A set of components a host registers, usually from a component package. Whichever side sends the prompt composes it
/// from the registered sources with <see cref="ComponentSources.Compose"/>, so an API that draws nothing teaches what
/// the client that draws it teaches (prompt.md, section 2).
/// </summary>
public interface IComponentSource
{
    /// <summary>The parser's library, with the components' traits (prompt.md, section 2).</summary>
    OpenUILibrary Library { get; }

    /// <summary>The LibrarySpec the prompt comes from (prompt.md, section 2).</summary>
    LibrarySpec Spec { get; }

    /// <summary>
    /// Where the prompt should say a component of this source fits among those of the sources before it, by component
    /// name: each slot is <c>"Parent.prop"</c>, or <c>"Parent"</c> when the parent has one content slot (prompt.md, section 2.8).
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>> Slots => ImmutableDictionary<string, IReadOnlyList<string>>.Empty;
}
