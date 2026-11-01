using OpenUI.Lang.Values;

namespace OpenUI.Lang.Runtime;

/// <summary>The validation rules of an input's rules object (language.md, section 5.4).</summary>
internal static class ValidationRules
{
    /// <summary>
    /// Checks <paramref name="value"/> against <paramref name="rules"/> in key order and returns the
    /// first failing rule's message, or null when every rule passes (section 5.4).
    /// </summary>
    /// <remarks>
    /// A rule whose argument is <c>false</c> or null is off, and unknown rule names are ignored.
    /// Every rule but <c>required</c> skips an empty value: null, <c>""</c>, <c>[]</c> or <c>{}</c>.
    /// <paramref name="patterns"/> holds the patterns compiled so far in one validation pass; without it each check compiles its own.
    /// </remarks>
    public static string? Check(OuiObject rules, OuiValue value, EcmaRegexPass? patterns = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var empty = IsEmpty(value);
        foreach (var (name, argument) in rules)
        {
            if (argument.IsNull || (argument.TryGetBoolean(out var on) && !on) || (empty && name != "required"))
            {
                continue;
            }

            var message = name switch
            {
                "required" => Required(value),
                "email" => value.TryGetString(out var email) && EcmaRegex.IsEmail(email)
                    ? null
                    : "Please enter a valid email",
                "url" => value.TryGetString(out var url) && UrlText.IsAbsolute(url) ? null : "Please enter a valid URL",
                "numeric" => IsNumeric(value) ? null : "Must be a number",
                "minLength" => value.TryGetString(out var shorter) && shorter.Length < Ecma.ToNumber(argument)
                    ? $"Must be at least {Ecma.NumberToString(Ecma.ToNumber(argument))} characters"
                    : null,
                "maxLength" => value.TryGetString(out var longer) && longer.Length > Ecma.ToNumber(argument)
                    ? $"Must be no more than {Ecma.NumberToString(Ecma.ToNumber(argument))} characters"
                    : null,
                "min" => Ecma.ToNumber(value) < Ecma.ToNumber(argument)
                    ? $"Must be at least {Ecma.NumberToString(Ecma.ToNumber(argument))}"
                    : null,
                "max" => Ecma.ToNumber(value) > Ecma.ToNumber(argument)
                    ? $"Must be no more than {Ecma.NumberToString(Ecma.ToNumber(argument))}"
                    : null,
                "pattern" => Pattern(value, argument, patterns),
                _ => null,
            };

            if (message is not null)
            {
                return message;
            }
        }

        return null;
    }

    private static bool IsEmpty(OuiValue value) => value.Kind switch
    {
        OuiValueKind.Null => true,
        OuiValueKind.String => ((string)value.Payload!).Length == 0,
        OuiValueKind.Array => ((OuiArray)value.Payload!).Count == 0,
        OuiValueKind.Object => ((OuiObject)value.Payload!).Count == 0,
        _ => false,
    };

    // A checkbox group is an object of booleans; with every option false nothing is chosen.
    private static string? Required(OuiValue value)
    {
        if (IsEmpty(value))
        {
            return "This field is required";
        }

        return value.TryGetObject(out var options) && options.All(option => option.Value.TryGetBoolean(out var ticked) && !ticked)
            ? "At least one option is required"
            : null;
    }

    private static bool IsNumeric(OuiValue value) =>
        value.TryGetNumber(out _) || (value.TryGetString(out var text) && !double.IsNaN(Ecma.ParseFloat(text)));

    // Only a string value is checked. A pattern that is invalid, unsupported or not a string, or a
    // value too long to match, fails the rule: a model cannot switch a rule off with a bad pattern.
    private static string? Pattern(OuiValue value, OuiValue argument, EcmaRegexPass? patterns)
    {
        if (!value.TryGetString(out var text))
        {
            return null;
        }

        var matched = text.Length <= EcmaRegex.MaxMatchedChars
            && argument.TryGetString(out var pattern)
            && (patterns is null ? EcmaRegex.IsMatch(pattern, text) : patterns.IsMatch(pattern, text)) == true;
        return matched ? null : "Invalid format";
    }
}
