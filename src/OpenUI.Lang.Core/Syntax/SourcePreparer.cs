using OpenUI.Lang.Values;

namespace OpenUI.Lang.Syntax;

/// <summary>
/// Prepares program text (language.md, section 1.2), one character at a time, in three chained
/// stages: fence extraction, comment stripping, trimming. Output only grows, except when the first
/// fence opener arrives: everything before it was prose, so the output restarts
/// (<see cref="Push"/> returns true; section 4, rule 4).
/// </summary>
internal sealed class SourcePreparer
{
    private readonly CharBuffer _output;
    private readonly FenceStage _fences = new();
    private readonly CommentStage _comments = new();
    private readonly TrimStage _trim = new();

    public SourcePreparer(CharBuffer output) => _output = output;

    /// <summary>Feeds one character; returns true when the output was cleared and restarted.</summary>
    public bool Push(char c) => _fences.Push(c, this);

    /// <summary>Flushes held characters at the end of the stream; trailing whitespace is dropped.</summary>
    public void Complete()
    {
        _fences.Complete(this);
        _comments.Complete(this);
    }

    private void FromFences(char c) => _comments.Push(c, this);

    private void FromComments(char c) => _trim.Push(c, _output);

    private void Restart()
    {
        _output.Clear();
        _comments.Clear();
        _trim.Clear();
    }

    /// <summary>
    /// Fences open with three backticks at the start of a line. Inside a fence a run of three
    /// backticks outside a double-quoted string closes it; single quotes do not shield. Fence
    /// contents are joined with newlines and prose outside fences is dropped.
    /// </summary>
    private sealed class FenceStage
    {
        private Mode _mode = Mode.NoFence;
        private int _lineStartTicks;
        private bool _atLineStart = true;
        private bool _inString;
        private bool _escape;
        private int _heldTicks;

        private enum Mode
        {
            NoFence,
            Prose,
            OpenerLine,
            InFence,
        }

        public bool Push(char c, SourcePreparer owner)
        {
            switch (_mode)
            {
                case Mode.NoFence or Mode.Prose:
                    return OutsideFence(c, owner);
                case Mode.OpenerLine:
                    if (c == '\n')
                    {
                        _mode = Mode.InFence;
                        _inString = false;
                        _escape = false;
                        _heldTicks = 0;
                    }

                    return false;
                default:
                    InsideFence(c, owner);
                    return false;
            }
        }

        public void Complete(SourcePreparer owner) => FlushTicks(owner);

        private bool OutsideFence(char c, SourcePreparer owner)
        {
            if (_mode == Mode.NoFence)
            {
                owner.FromFences(c);
            }

            if (c == '\n')
            {
                _atLineStart = true;
                _lineStartTicks = 0;
                return false;
            }

            if (c != '`' || !_atLineStart)
            {
                _atLineStart = false;
                return false;
            }

            if (++_lineStartTicks < 3)
            {
                return false;
            }

            var first = _mode == Mode.NoFence;
            _mode = Mode.OpenerLine;
            _atLineStart = false;
            if (first)
            {
                owner.Restart();
            }
            else
            {
                owner.FromFences('\n');
            }

            return first;
        }

        private void InsideFence(char c, SourcePreparer owner)
        {
            if (_inString)
            {
                owner.FromFences(c);
                if (_escape)
                {
                    _escape = false;
                }
                else if (c == '\\')
                {
                    _escape = true;
                }
                else if (c == '"')
                {
                    _inString = false;
                }

                return;
            }

            if (c == '`')
            {
                if (++_heldTicks == 3)
                {
                    _heldTicks = 0;
                    _mode = Mode.Prose;
                    _atLineStart = false;
                }

                return;
            }

            FlushTicks(owner);
            owner.FromFences(c);
            _inString = c == '"';
        }

        private void FlushTicks(SourcePreparer owner)
        {
            for (; _heldTicks > 0; _heldTicks--)
            {
                owner.FromFences('`');
            }
        }
    }

    /// <summary><c>//</c> and <c>#</c> start a comment to the end of the line, except inside either kind of string.</summary>
    private sealed class CommentStage
    {
        private char _quote;
        private bool _escape;
        private bool _inComment;
        private bool _heldSlash;

        public void Push(char c, SourcePreparer owner)
        {
            if (_inComment)
            {
                if (c == '\n')
                {
                    _inComment = false;
                    owner.FromComments(c);
                }

                return;
            }

            if (_heldSlash)
            {
                _heldSlash = false;
                if (c == '/')
                {
                    _inComment = true;
                    return;
                }

                owner.FromComments('/');
            }

            if (_quote != '\0')
            {
                owner.FromComments(c);
                if (_escape)
                {
                    _escape = false;
                }
                else if (c == '\\')
                {
                    _escape = true;
                }
                else if (c == _quote)
                {
                    _quote = '\0';
                }

                return;
            }

            switch (c)
            {
                case '/':
                    _heldSlash = true;
                    return;
                case '#':
                    _inComment = true;
                    return;
                case '"' or '\'':
                    _quote = c;
                    break;
            }

            owner.FromComments(c);
        }

        public void Complete(SourcePreparer owner)
        {
            if (_heldSlash)
            {
                _heldSlash = false;
                owner.FromComments('/');
            }
        }

        public void Clear() => (_quote, _escape, _inComment, _heldSlash) = ('\0', false, false, false);
    }

    /// <summary>Drops leading whitespace and holds trailing whitespace until something follows it.</summary>
    private sealed class TrimStage
    {
        private readonly CharBuffer _held = new();
        private bool _started;

        public void Push(char c, CharBuffer output)
        {
            if (Ecma.IsWhiteSpace(c))
            {
                if (_started)
                {
                    _held.Append(c);
                }

                return;
            }

            _started = true;
            output.Append(_held).Append(c);
            _held.Clear();
        }

        public void Clear()
        {
            _held.Clear();
            _started = false;
        }
    }
}
