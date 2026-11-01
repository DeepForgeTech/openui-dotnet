using OpenUI.Lang.Values;

namespace OpenUI.Lang.Semantics;

/// <summary>A budget ran out. Caught per statement, which then drops with <c>x-limit-exceeded</c>.</summary>
internal sealed class LimitException(string message) : Exception(message);

/// <summary>
/// The work one pass may do: fuel for steps, proportional to what a step
/// really walks, and characters for the text it creates. Anything linear in a value's size or a
/// string's length goes through here, so one expression cannot do unbounded work for one step.
/// </summary>
internal sealed class EvaluationBudget(OpenUILimits limits)
{
    // Scanning characters is far cheaper than an evaluation step.
    private const int CharsPerStep = 64;

    // A failed comparison in a substring search costs less again: the search stops early at most positions.
    private const int SearchComparesPerStep = 4096;

    // A collator reads characters one at a time through its tables.
    private const int CollatedCharsPerStep = 16;

    private long _fuel = limits.Fuel;
    private long _text;
    private long _evaluationsPaid;
    private int _mutations;

    /// <summary>The steps spent so far.</summary>
    public long FuelSpent => limits.Fuel - Math.Max(_fuel, 0);

    public void Spend(long steps = 1)
    {
        if (steps > _fuel)
        {
            _fuel = -1;
            throw new LimitException($"Evaluation ran out of fuel ({limits.Fuel} steps).");
        }

        _fuel -= steps;
    }

    /// <summary>Pays for a walk over everything <paramref name="value"/> expands to.</summary>
    public void Walk(OuiValue value)
    {
        var measure = value.Measure;
        Spend(measure.Size - 1 + (measure.Chars / CharsPerStep));
    }

    /// <summary>Pays for hashing or comparing a name or key once.</summary>
    public void Read(string name) => Spend(ReadCost(name.Length));

    /// <summary>What reading this much text takes.</summary>
    public long ReadCost(long chars) => chars / CharsPerStep;

    /// <summary>Whether <paramref name="text"/> holds <paramref name="part"/>. The search can compare the whole part at every position, so that is its price.</summary>
    public bool Contains(string text, string part)
    {
        Spend((long)Math.Max(text.Length - part.Length + 1, 0) * part.Length / SearchComparesPerStep);
        return text.Contains(part, StringComparison.Ordinal);
    }

    /// <summary>Pays for a collator to compare these two; it reads until the shorter one ends.</summary>
    public void Collate(string left, string right) => Spend(Math.Min(left.Length, right.Length) / CollatedCharsPerStep);

    /// <summary>Takes from an action's budget the evaluations the session ran since the last call; <paramref name="total"/> is the session's running count.</summary>
    public void PayEvaluations(long total)
    {
        var owed = total - _evaluationsPaid;
        _evaluationsPaid = total;
        Spend(owed);
    }

    /// <summary>Starts counting the session's evaluations from <paramref name="total"/>.</summary>
    public void CountEvaluationsFrom(long total) => _evaluationsPaid = total;

    /// <summary>Counts one mutation of this action against its limit.</summary>
    public void CountMutation()
    {
        if (++_mutations > limits.MaxMutationsPerAction)
        {
            throw new LimitException($"An action called more than {limits.MaxMutationsPerAction} mutations.");
        }
    }

    /// <summary>Pays for creating this many characters; over the budget, nothing was allocated yet.</summary>
    public void ChargeText(long chars)
    {
        if (chars > limits.MaxTextChars - _text)
        {
            _text = limits.MaxTextChars;
            throw new LimitException($"Evaluation created more than {limits.MaxTextChars} characters.");
        }

        _text += chars;
    }

    /// <summary>Pays for writing a value out, as a tool's arguments or part of a host event; over the budget, nothing was written yet.</summary>
    public void ChargeJson(OuiValue value)
    {
        Walk(value);
        ChargeText(value.Measure.Chars + value.Measure.Size);
    }

    /// <summary>The <c>String(value)</c> of a value (section 3.1), refused before a join too long to keep gets built.</summary>
    public string Stringify(OuiValue value)
    {
        PriceText(value);
        var text = Ecma.ToString(value);

        // A string that was already there is the budget's to read, not to refuse.
        return value.Kind == OuiValueKind.String ? text : Kept(text);
    }

    /// <summary>The language's number conversion (section 3.1); a string costs its length, and everything else is a lookup.</summary>
    public double ToNumber(OuiValue value)
    {
        if (value.Kind == OuiValueKind.String)
        {
            Walk(value);
        }

        return Ecma.ToNumber(value);
    }

    /// <summary>The language's <c>==</c> (section 3.2); a composite against a primitive joins first.</summary>
    public bool LooselyEquals(OuiValue left, OuiValue right)
    {
        if (!left.IsNull && !right.IsNull && !(Ecma.IsComposite(left) && Ecma.IsComposite(right)))
        {
            PriceText(left);
            PriceText(right);
        }

        return Ecma.LooselyEquals(left, right);
    }

    /// <summary>The language's <c>+</c> (section 3.2), with the joined text paid for before it exists.</summary>
    public OuiValue Add(OuiValue left, OuiValue right)
    {
        if (left.Kind != OuiValueKind.String && right.Kind != OuiValueKind.String)
        {
            return Ecma.Add(left, right);
        }

        var length = (left.IsNull ? 0 : PriceText(left)) + (right.IsNull ? 0 : PriceText(right));
        if (length > limits.MaxStringLength)
        {
            throw TooLong();
        }

        ChargeText(length);
        var sum = Ecma.Add(left, right);
        return sum.TryGetString(out var text) ? Kept(text) : sum;
    }

    // Pays for what String(value) reads and creates, and answers how long the text will at least be.
    private long PriceText(OuiValue value)
    {
        if (value.TryGetString(out var text))
        {
            Walk(value);
            return text.Length;
        }

        if (!value.TryGetArray(out var array))
        {
            return 0;
        }

        Walk(value);
        var length = JoinedLength(array);
        ChargeText(length);
        return length;
    }

    // Numbers are not in the estimate, so a join can come out longer than it.
    private string Kept(string text) => text.Length > limits.MaxStringLength ? throw TooLong() : text;

    // The least the join can come to, found before building it. Items that join are bounded by the
    // caller's walk; only arrays recurse, and value depth bounds that.
    private long JoinedLength(OuiArray array)
    {
        long length = Math.Max(array.Count - 1, 0);
        foreach (var item in array)
        {
            length += item.TryGetArray(out var nested) ? JoinedLength(nested) : item.TryGetString(out var text) ? text.Length : 0;
            if (length > limits.MaxStringLength)
            {
                throw TooLong();
            }
        }

        return length;
    }

    private LimitException TooLong() => new($"A string is longer than {limits.MaxStringLength} characters.");
}
