using System.Collections.Immutable;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Semantics;

/// <summary>
/// An expression an action evaluates when it runs (language.md, section 6.3), with the <c>@Each</c>
/// loop values it captured when the action was built (section 3.5) and the statement it belongs to.
/// </summary>
internal sealed record DeferredExpression(Bound Node, ImmutableArray<OuiValue> Loop, string? StatementId);
