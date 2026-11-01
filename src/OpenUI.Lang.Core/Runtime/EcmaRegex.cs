using System.Text;
using System.Text.RegularExpressions;

namespace OpenUI.Lang.Runtime;

/// <summary>
/// The regular expressions of the <c>pattern</c> and <c>email</c> rules (language.md, section 5.4):
/// character classes, quantifiers, anchors, groups and alternation, with ECMAScript's meaning of
/// <c>\d</c>, <c>\w</c>, <c>\s</c>, <c>.</c> and <c>$</c>. Patterns come from the model, so they run
/// only on <see cref="RegexOptions.NonBacktracking"/>, which is linear in the input. Anything outside
/// the subset, such as a backreference or a lookaround, is unsupported.
/// </summary>
internal static class EcmaRegex
{
    /// <summary>Longer patterns are not compiled and count as invalid.</summary>
    internal const int MaxPatternLength = 1024;

    /// <summary>A longer value is not matched and fails the rule: the engine's tables grow with the text it has read.</summary>
    internal const int MaxMatchedChars = 16 * 1024;

    /// <summary>The largest automaton the engine accepts; it refuses a larger pattern without building it.</summary>
    internal const int MaxPatternSize = 10_000;

    // Steps, measured on hostile patterns: compiling costs a flat price and more per unit of size, and
    // each character read can make a state as wide as the pattern, plus its transitions.
    private const int CompileSteps = 1024;
    private const int CompileStepsPerSize = 64;
    private const int StateSize = 128;
    private const int CharsPerStep = 2;

    private const string EmailPattern = @"^[^\s@]+@[^\s@]+\.[^\s@]+$";
    private const string Digits = "0-9";
    private const string WordChars = "0-9A-Z_a-z";
    private const string Space = @"\t-\r \u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF";
    private const string NotDigits = @"\u0000-/:-\uFFFF";
    private const string NotWordChars = @"\u0000-/:-@\[-\^`{-\uFFFF";
    private const string NotSpace = @"\u0000-\u0008\u000E-\u001F!-\u009F\u00A1-\u167F\u1681-\u1FFF\u200B-\u2027\u202A-\u202E\u2030-\u205E\u2060-\u2FFF\u3001-\uFEFE\uFF00-\uFFFF";

    private static readonly Regex Email = Build(EmailPattern)!;

    /// <summary>
    /// Whether <paramref name="pattern"/> matches anywhere in <paramref name="input"/>. Null when the
    /// pattern is invalid, unsupported or too long; the rule then fails.
    /// </summary>
    public static bool? IsMatch(string pattern, string input) => Build(pattern)?.IsMatch(input);

    /// <summary>What compiling <paramref name="pattern"/> and matching <paramref name="length"/> characters costs, in evaluation steps.</summary>
    public static long MatchSteps(string pattern, int length)
    {
        var size = Size(pattern);
        return CompileSteps + (size * CompileStepsPerSize) + ((size + StateSize) * length / CharsPerStep);
    }

    /// <summary>Whether <paramref name="input"/> is an email address by the expression in section 5.4.</summary>
    public static bool IsEmail(string input) => input.Length <= MaxMatchedChars && Email.IsMatch(input);

    // No match timeout: a rule's answer must not depend on how busy the machine is. MatchSteps pays
    // for the work first. A compiled pattern is not kept past the pass that paid for it (see
    // EcmaRegexPass): its tables grow with what it has read and would outlive that payment.
    internal static Regex? Build(string pattern)
    {
        if (pattern.Length > MaxPatternLength || Translate(pattern) is not { } translated)
        {
            return null;
        }

        try
        {
            return new Regex(translated, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            // Unbalanced groups, a quantifier with nothing to repeat, or more than the engine takes.
            return null;
        }
    }

    // An upper bound on the size of the pattern's automaton: every atom counts one, a group counts
    // what it holds and a counted repetition multiplies what it repeats. Stops counting past the limit.
    internal static long Size(string pattern)
    {
        var outer = new Stack<long>();
        long total = 0;
        long last = 0;
        for (var i = 0; i < pattern.Length; i++)
        {
            switch (pattern[i])
            {
                case '(':
                    outer.Push(total);
                    total = 0;
                    break;
                case ')':
                    last = total;
                    total += outer.TryPop(out var before) ? before : 0;
                    break;
                case '|' or '*' or '+' or '?':
                    break;
                case '{' when Repeats(pattern, ref i) is { } repeats:
                    total += last * (repeats - 1);
                    last *= repeats;
                    break;
                default:
                    i = SkipAtom(pattern, i);
                    last = 1;
                    total++;
                    break;
            }

            total = Math.Min(total, MaxPatternSize + 1);
            last = Math.Min(last, MaxPatternSize + 1);
        }

        return total + outer.Sum();
    }

    // The larger bound of {m}, {m,} or {m,n} starting at i, moving i to its closing brace; null if it is not one.
    private static long? Repeats(string pattern, ref int i)
    {
        long largest = 1;
        long bound = 0;
        var end = i + 1;
        for (; end < pattern.Length && (char.IsAsciiDigit(pattern[end]) || pattern[end] == ','); end++)
        {
            bound = pattern[end] == ',' ? 0 : Math.Min((bound * 10) + (pattern[end] - '0'), MaxPatternSize + 1);
            largest = Math.Max(largest, bound);
        }

        if (end == i + 1 || end == pattern.Length || pattern[end] != '}')
        {
            return null;
        }

        i = end;
        return largest;
    }

    private static int SkipAtom(string pattern, int i)
    {
        if (pattern[i] == '\\')
        {
            return i + 1;
        }

        if (pattern[i] != '[')
        {
            return i;
        }

        for (i++; i < pattern.Length && pattern[i] != ']'; i++)
        {
            if (pattern[i] == '\\')
            {
                i++;
            }
        }

        return i;
    }

    // The .NET spelling of a pattern in the subset, or null for anything outside it.
    private static string? Translate(string pattern)
    {
        var output = new StringBuilder(pattern.Length + 16);
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            switch (c)
            {
                case '\\':
                    if (!AppendEscape(output, pattern, ref i, inClass: false))
                    {
                        return null;
                    }

                    break;
                case '[':
                    if (!AppendClass(output, pattern, ref i))
                    {
                        return null;
                    }

                    break;
                case '(' when i + 1 < pattern.Length && pattern[i + 1] == '?':
                    // Of the (?...) forms, only a plain non-capturing group is in the subset.
                    if (i + 2 >= pattern.Length || pattern[i + 2] != ':')
                    {
                        return null;
                    }

                    output.Append("(?:");
                    i += 2;
                    break;
                case '$':
                    output.Append(@"\z");
                    break;
                case '.':
                    output.Append(@"[^\n\r\u2028\u2029]");
                    break;
                default:
                    output.Append(c);
                    break;
            }
        }

        return output.ToString();
    }

    private static bool AppendClass(StringBuilder output, string pattern, ref int i)
    {
        output.Append('[');
        i++;
        if (i < pattern.Length && pattern[i] == '^')
        {
            output.Append('^');
            i++;
        }

        // ECMAScript reads [] as matching nothing and [^] as anything; .NET reads them differently.
        if (i < pattern.Length && pattern[i] == ']')
        {
            return false;
        }

        for (; i < pattern.Length; i++)
        {
            switch (pattern[i])
            {
                case ']':
                    output.Append(']');
                    return true;
                case '\\':
                    if (!AppendEscape(output, pattern, ref i, inClass: true))
                    {
                        return false;
                    }

                    break;
                case '[':
                    // Unescaped, .NET could read a following -[ as class subtraction.
                    output.Append(@"\[");
                    break;
                default:
                    output.Append(pattern[i]);
                    break;
            }
        }

        return false;
    }

    private static bool AppendEscape(StringBuilder output, string pattern, ref int i, bool inClass)
    {
        if (++i == pattern.Length)
        {
            return false;
        }

        var c = pattern[i];
        if (ClassEscape(c) is { } set)
        {
            output.Append(inClass ? set : "[" + set + "]");
            return true;
        }

        switch (c)
        {
            case 'n' or 'r' or 't' or 'f' or 'v':
                output.Append('\\').Append(c);
                return true;
            case 'x' or 'u':
                var digits = c == 'x' ? 2 : 4;
                if (i + digits >= pattern.Length || pattern.AsSpan(i + 1, digits).ContainsAnyExcept("0123456789abcdefABCDEF"))
                {
                    return false;
                }

                output.Append('\\').Append(pattern, i, digits + 1);
                i += digits;
                return true;
            default:
                // Letters and digits are other features: \b, \1, \k, \p and so on. Punctuation stands for itself.
                if (char.IsAsciiLetterOrDigit(c))
                {
                    return false;
                }

                output.Append('\\').Append(c);
                return true;
        }
    }

    private static string? ClassEscape(char c) => c switch
    {
        'd' => Digits,
        'D' => NotDigits,
        'w' => WordChars,
        'W' => NotWordChars,
        's' => Space,
        'S' => NotSpace,
        _ => null,
    };
}

/// <summary>
/// The patterns compiled during one validation pass, so fields that share a rule compile it once. It
/// is dropped with the pass and keeps at most <see cref="MaxKept"/> patterns: fuel alone would let a
/// program of many distinct patterns hold over a hundred megabytes. Past that, a pattern is compiled
/// for its match and let go. Every field still pays <see cref="EcmaRegex.MatchSteps"/> for its own match.
/// </summary>
internal sealed class EcmaRegexPass
{
    /// <summary>How many compiled patterns a pass holds on to.</summary>
    internal const int MaxKept = 16;

    private readonly Dictionary<string, Regex?> _built = new(StringComparer.Ordinal);

    /// <summary>The compiled <paramref name="pattern"/>, or null when it is invalid, unsupported or too long.</summary>
    public Regex? Get(string pattern)
    {
        if (!_built.TryGetValue(pattern, out var regex))
        {
            regex = EcmaRegex.Build(pattern);
            if (_built.Count < MaxKept)
            {
                _built[pattern] = regex;
            }
        }

        return regex;
    }

    /// <summary>Like <see cref="EcmaRegex.IsMatch"/>.</summary>
    public bool? IsMatch(string pattern, string input) => Get(pattern)?.IsMatch(input);
}
