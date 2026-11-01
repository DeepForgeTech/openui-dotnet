namespace OpenUI.Lang;

/// <summary>The kinds of statement (language.md, section 2.1).</summary>
public enum StatementKind
{
    /// <summary>Anything that is not one of the other kinds (language.md, section 2.1).</summary>
    Value,

    /// <summary>A <c>$name</c> on the left: its right side is the default (language.md, section 2.1).</summary>
    State,

    /// <summary><c>Query(...)</c> as the whole right side (language.md, section 2.1).</summary>
    Query,

    /// <summary><c>Mutation(...)</c> as the whole right side (language.md, section 2.1).</summary>
    Mutation,
}
