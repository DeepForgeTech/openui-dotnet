using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Semantics;

// Taking a statement's result from the previous pass. A statement is reused only when its bound tree is the same
// instance and everything it read is the same: each state variable and query result, and each statement it
// referenced, which must be reusable in turn or still missing. Reusing it then does what evaluating it would: it
// adds the same errors in the same order, spends the same fuel and text, and reuses or evaluates the statements it
// references in the same order, so the pass ends exactly as one with nothing to reuse would. Close to a limit, where
// the pass must stop at the very step it would have, it evaluates as usual instead.
internal sealed partial class Evaluator
{
    private readonly IReadOnlyDictionary<string, StatementRecord>? _previous;
    private readonly Dictionary<string, StatementRecord>? _records;

    // The steps of every statement being recorded, innermost last. A statement takes its own off the end when it finishes,
    // so one buffer serves the whole pass.
    private readonly List<RecordedStep>? _steps;
    private readonly int _pass;
    private bool _reuseOff;

    private Recorder? Recording => _frames.Count > 0 ? _frames[^1].Recorder : null;

    private Result EvaluateReference(string name)
    {
        // Looked up by name in the document, the memo and each open frame.
        var cost = _budget.ReadCost(name.Length);
        if (Recording is not { } recorder)
        {
            _budget.Spend(cost * (2 + _frames.Count));
            return EvaluateStatement(name);
        }

        var (fuel, text) = (_budget.FuelSpent, _budget.TextSpent);
        _budget.Spend(cost * (2 + _frames.Count));
        var step = new RecordedStep(RecordedStepKind.Reference, name, Found: _document.TryGetStatement(name, out _), Cost: cost, Depth: _depth - recorder.Depth);
        var result = EvaluateStatement(name);
        recorder.NestedFuel += _budget.FuelSpent - fuel;
        recorder.NestedText += _budget.TextSpent - text;
        _steps!.Add(step);
        return result;
    }

    private OuiValue ReadState(StateKey key)
    {
        var value = _state(key);
        Note(new RecordedStep(RecordedStepKind.State, key.Name, Value: value));
        return value;
    }

    private bool ReadData(string name, out OuiValue data)
    {
        data = OuiValue.Null;
        var found = _queryData is not null && _queryData.TryGetValue(name, out data);
        Note(new RecordedStep(RecordedStepKind.Query, name, Found: found, Value: data));
        return found;
    }

    private void Note(RecordedStep step)
    {
        if (Recording is not null)
        {
            _steps!.Add(step);
        }
    }

    // The statement being evaluated ran code a reused result would not run again.
    private void NotReusable()
    {
        if (Recording is { } recorder)
        {
            recorder.Unusable = true;
        }
    }

    private void Record(string name, Statement statement, Result result, Recorder recorder, bool reusable)
    {
        var count = _steps!.Count - recorder.Start;
        if (reusable && !recorder.Unusable)
        {
            _records![name] = new StatementRecord(
                statement.Bound,
                result,
                ImmutableArray.Create(CollectionsMarshal.AsSpan(_steps).Slice(recorder.Start, count)),
                _budget.FuelSpent - recorder.Fuel - recorder.NestedFuel,
                _budget.TextSpent - recorder.Text - recorder.NestedText,
                _deepest - recorder.Depth);
        }

        _steps.RemoveRange(recorder.Start, count);
    }

    private bool TryReuse(string name, out Result result)
    {
        result = default;
        if (_previous is null || _reuseOff || !CanReuse(name, 0))
        {
            return false;
        }

        // Inside the statement one more frame is open, and every reference it reads pays for each.
        var frames = _frames.Count + 1;
        var bounds = BoundsOf(name);
        if (bounds.Depth > _limits.MaxEvalDepth - _depth
            || bounds.Text > _budget.TextLeft
            || Add(bounds.Fuel, Multiply(bounds.FuelPerFrame, frames)) > _budget.FuelLeft)
        {
            _reuseOff = true;
            return false;
        }

        Replay(name, _previous[name], frames);
        result = _previous[name].Result;
        return true;
    }

    // Whether the statement's record can stand for evaluating it now. A statement the pass has already finished is
    // reusable only if it was reused, since one evaluated again may have come out different.
    private bool CanReuse(string name, int level)
    {
        if (!_previous!.TryGetValue(name, out var record)
            || !_document.TryGetStatement(name, out var statement)
            || !ReferenceEquals(record.Bound, statement.Bound))
        {
            return false;
        }

        if (_memo.ContainsKey(name))
        {
            return _records!.TryGetValue(name, out var kept) && ReferenceEquals(kept, record);
        }

        if (record.CheckedIn == _pass)
        {
            return record.Reusable;
        }

        if (level > _limits.MaxEvalDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            return false;
        }

        // Marked first, so a record that somehow reached itself again stops there.
        record.CheckedIn = _pass;
        record.Reusable = false;
        var reusable = true;
        foreach (var step in record.Steps)
        {
            reusable = step.Kind switch
            {
                RecordedStepKind.Reference => step.Found ? CanReuse(step.Name, level + 1) : !_document.TryGetStatement(step.Name, out _),
                RecordedStepKind.State => RecordedStep.Same(_state(new StateKey(step.Name)), step.Value),
                RecordedStepKind.Query => SameData(step),
                _ => true,
            };

            if (!reusable)
            {
                break;
            }
        }

        record.Reusable = reusable;
        return reusable;
    }

    private bool SameData(RecordedStep step)
    {
        OuiValue data = default;
        var found = _queryData?.TryGetValue(step.Name, out data) == true;
        return found == step.Found && (!found || RecordedStep.Same(data, step.Value));
    }

    // The most a reused statement may spend, the statements it references included as if each were evaluated there.
    private ReuseBounds BoundsOf(string name)
    {
        var record = _previous![name];
        if (record.BoundedIn == _pass)
        {
            return record.Bounds;
        }

        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            return new ReuseBounds(long.MaxValue, 0, long.MaxValue, long.MaxValue);
        }

        var (fuel, perFrame, text, depth) = (record.Fuel, 0L, record.Text, (long)record.Depth);
        foreach (var step in record.Steps)
        {
            if (step.Kind != RecordedStepKind.Reference)
            {
                continue;
            }

            fuel = Add(fuel, Add(step.Cost, step.Cost));
            perFrame = Add(perFrame, step.Cost);
            if (step.Found)
            {
                // It runs one frame further in, so it pays Fuel + FuelPerFrame * (frames + 1).
                var inner = BoundsOf(step.Name);
                fuel = Add(fuel, Add(inner.Fuel, inner.FuelPerFrame));
                perFrame = Add(perFrame, inner.FuelPerFrame);
                text = Add(text, inner.Text);
                depth = Math.Max(depth, Add(step.Depth, inner.Depth));
            }
        }

        record.Bounds = new ReuseBounds(fuel, perFrame, text, depth);
        record.BoundedIn = _pass;
        return record.Bounds;
    }

    private void Replay(string name, StatementRecord record, int frames)
    {
        foreach (var step in record.Steps)
        {
            switch (step.Kind)
            {
                case RecordedStepKind.Reference:
                    _budget.Spend(step.Cost * (2 + frames));
                    if (step.Found && !_memo.ContainsKey(step.Name))
                    {
                        Replay(step.Name, _previous![step.Name], frames + 1);
                    }

                    break;
                case RecordedStepKind.Report:
                    _bag.Add(step.Error!, step.Timing);
                    break;
            }
        }

        _budget.Spend(record.Fuel);
        _budget.ChargeText(record.Text);
        _memo[name] = record.Result;
        _records![name] = record;
    }

    private static long Add(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;

    private static long Multiply(long a, long b) => a != 0 && b > long.MaxValue / a ? long.MaxValue : a * b;

    /// <summary>What a statement being evaluated has done so far, and where the pass stood when it started.</summary>
    private sealed class Recorder(long fuel, long text, int depth, int start)
    {
        public long Fuel { get; } = fuel;

        public long Text { get; } = text;

        public int Depth { get; } = depth;

        /// <summary>Where its steps begin in the pass's buffer.</summary>
        public int Start { get; } = start;

        public long NestedFuel { get; set; }

        public long NestedText { get; set; }

        public bool Unusable { get; set; }
    }
}
