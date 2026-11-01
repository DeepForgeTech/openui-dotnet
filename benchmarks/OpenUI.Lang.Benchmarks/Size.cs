namespace OpenUI.Lang.Benchmarks;

/// <summary>How big the input is. What each size means is in <see cref="Corpus"/>.</summary>
public enum Size
{
    /// <summary>A program taken from the conformance fixtures.</summary>
    Small,

    /// <summary>A generated dashboard of about 90 statements.</summary>
    Medium,

    /// <summary>A generated dashboard of about 400 statements.</summary>
    Large,
}
