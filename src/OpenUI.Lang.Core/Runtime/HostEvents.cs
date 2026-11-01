using System.Text.Json;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Runtime;

/// <summary>Receives the events actions send to the host (language.md, section 6.3).</summary>
public interface IOpenUIHostEventHandler
{
    /// <summary>Handles one event. It is not awaited by the action; an exception is caught and traced (language.md, section 6.3).</summary>
    ValueTask OnEventAsync(OpenUIHostEvent e, CancellationToken ct);
}

/// <summary>An event sent to the host (language.md, section 6.3).</summary>
/// <param name="Type"><c>continue_conversation</c>, <c>open_url</c>, or a custom action's name.</param>
/// <param name="HumanFriendlyMessage">The message of <c>@ToAssistant</c>; empty otherwise.</param>
/// <param name="Params">The context, the URL, or a custom action's arguments.</param>
/// <param name="FormName">The nearest form around the triggering component, or null.</param>
/// <param name="FormState">The form state as of when the action ran.</param>
public sealed record OpenUIHostEvent(string Type, string HumanFriendlyMessage, OuiObject Params, string? FormName, OuiObject FormState)
{
    /// <summary>Writes the wire shape of section 6.3; <c>formName</c> is absent outside a form.</summary>
    public void WriteTo(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStartObject();
        writer.WriteString("type", Type);
        writer.WriteString("humanFriendlyMessage", HumanFriendlyMessage);
        writer.WritePropertyName("params");
        ((OuiValue)Params).WriteTo(writer);
        if (FormName is not null)
        {
            writer.WriteString("formName", FormName);
        }

        writer.WritePropertyName("formState");
        ((OuiValue)FormState).WriteTo(writer);
        writer.WriteEndObject();
    }
}

/// <summary>What triggered an action (language.md, section 6.3).</summary>
/// <param name="Component">The triggering component, or null.</param>
/// <param name="FormName">The nearest form around it, or null.</param>
public readonly record struct ActionOrigin(OuiComponent? Component, string? FormName);

/// <summary>How an action ended (language.md, section 6.3).</summary>
public enum ActionOutcomeKind
{
    /// <summary>Every step ran (language.md, section 6.3).</summary>
    Completed,

    /// <summary>A failed mutation stopped the remaining steps (language.md, section 6.3).</summary>
    Stopped,

    /// <summary>A mutation was already loading, or the program is still streaming (sections 4 and 6.2).</summary>
    Rejected,

    /// <summary>The action had nothing to do (language.md, section 6.3).</summary>
    NoOp,

    /// <summary>The action runs from inside a form and a field failed its rules, so no step ran (language.md, section 5.4).</summary>
    Invalid,
}

/// <summary>The result of running an action (language.md, section 6.3).</summary>
/// <param name="Kind">How it ended.</param>
/// <param name="StepsRun">The steps that ran, not counting no-op steps.</param>
/// <param name="Error">The error that stopped it, or null.</param>
/// <param name="Validation">The failing fields when <paramref name="Kind"/> is <see cref="ActionOutcomeKind.Invalid"/>.</param>
public sealed record ActionOutcome(ActionOutcomeKind Kind, int StepsRun, OpenUIError? Error, FormValidation? Validation = null);
