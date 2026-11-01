using System.Text.Json;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Runtime;

/// <summary>A query's latest result and fetch state (language.md, section 6.1).</summary>
/// <param name="Data">What references resolve to: the latest result, or null before the first one.</param>
/// <param name="IsLoading">True while the first fetch is in flight.</param>
/// <param name="IsRefetching">True while a later fetch is in flight; the previous result stays visible.</param>
/// <param name="Errors">The errors of the latest fetch.</param>
public sealed record QueryStatus(OuiValue Data, bool IsLoading, bool IsRefetching, OpenUIErrorList Errors)
{
    internal static QueryStatus Unknown { get; } = new(OuiValue.Null, false, false, OpenUIErrorList.Empty);

    /// <summary>
    /// Writes the data's fields with the reserved keys <c>__openui_loading</c>, <c>__openui_refetching</c>
    /// and <c>__openui_errors</c>, which exist only for the host (section 6.1). Data that is not an object goes under <c>data</c>.
    /// </summary>
    public void WriteHostJson(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStartObject();
        if (Data.TryGetObject(out var fields))
        {
            foreach (var (key, value) in fields)
            {
                writer.WritePropertyName(key);
                value.WriteTo(writer);
            }
        }
        else
        {
            writer.WritePropertyName("data");
            Data.WriteTo(writer);
        }

        writer.WriteBoolean("__openui_loading", IsLoading);
        writer.WriteBoolean("__openui_refetching", IsRefetching);
        writer.WritePropertyName("__openui_errors");
        Errors.WriteTo(writer);
        writer.WriteEndObject();
    }
}

/// <summary>The states of a mutation (language.md, section 6.2).</summary>
public enum MutationState
{
    /// <summary>It has not run (language.md, section 6.2).</summary>
    Idle,

    /// <summary>A run is in flight; another run is rejected (language.md, section 6.2).</summary>
    Loading,

    /// <summary>The last run succeeded (language.md, section 6.2).</summary>
    Success,

    /// <summary>The last run failed (language.md, section 6.2).</summary>
    Error,
}

/// <summary>What references to a mutation resolve to (language.md, section 6.2).</summary>
/// <param name="State">The state.</param>
/// <param name="Data">The tool's result after a success; otherwise null.</param>
/// <param name="Error">The error message after a failure; otherwise null.</param>
public sealed record MutationStatus(MutationState State, OuiValue Data, string? Error)
{
    internal static MutationStatus Idle { get; } = new(MutationState.Idle, OuiValue.Null, null);

    /// <summary>The <c>{ status, data, error }</c> object expressions read (section 6.2).</summary>
    public OuiObject ToObject() => new OuiObject.Builder()
        .Set("status", State.ToString().ToLowerInvariant())
        .Set("data", Data)
        .Set("error", Error)
        .ToObject();
}
