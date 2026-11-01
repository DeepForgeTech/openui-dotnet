using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Syntax;

/// <summary>The outcome of parsing one statement's text.</summary>
/// <param name="Statement">The statement, or null when the text has no statement shape or fails to parse.</param>
/// <param name="TooDeep">True when the statement nests deeper than the limit and was dropped.</param>
internal readonly record struct ParseOutcome(StatementSyntax? Statement, bool TooDeep);

/// <summary>
/// A Pratt parser for one statement (language.md, sections 1.5 and 1.6). Binary operators loop;
/// only brackets, unary operators and ternaries nest, each through a depth guard.
/// </summary>
internal sealed class Parser
{
    private readonly List<Token> _tokens;
    private readonly int _maxDepth;
    private int _position;
    private int _depth;
    private int _height;

    private Parser(List<Token> tokens, int maxDepth)
    {
        _tokens = tokens;
        _maxDepth = maxDepth;
    }

    /// <summary>Parses <paramref name="text"/> as one statement starting at <paramref name="offset"/> of the prepared text.</summary>
    public static ParseOutcome ParseStatement(string text, int offset, bool implicitClose, int maxDepth)
    {
        var parser = new Parser(Lexer.Tokenize(text, implicitClose), maxDepth);
        try
        {
            return new ParseOutcome(parser.Statement(new SourceSpan(offset, text.Length)), false);
        }
        catch (TooDeepException)
        {
            return new ParseOutcome(null, true);
        }
    }

    private Token Current => _tokens[_position];

    private TokenKind Peek(int ahead = 0) => _tokens[Math.Min(_position + ahead, _tokens.Count - 1)].Kind;

    private StatementSyntax? Statement(SourceSpan span)
    {
        if (Current.Kind is not (TokenKind.Identifier or TokenKind.StateName) || Peek(1) != TokenKind.Assign)
        {
            return null;
        }

        var name = Current.Text!;
        _position += 2;

        // Tokens after a complete expression are ignored (implementation-defined, section 1.6).
        return Expression() is { } value ? new StatementSyntax(name, value, span) : null;
    }

    private ExpressionSyntax? Expression()
    {
        var condition = Binary(0);
        if (condition is null || Peek() != TokenKind.Question)
        {
            return condition;
        }

        var tallest = _height;
        _position++;
        Enter();
        var whenTrue = Expression();
        if (whenTrue is null || Peek() != TokenKind.Colon)
        {
            return null;
        }

        tallest = Math.Max(tallest, _height);
        _position++;
        var whenFalse = Expression();
        Exit();
        Built(Math.Max(tallest, _height) + 1);
        return whenFalse is null ? null : new TernarySyntax(condition, whenTrue, whenFalse);
    }

    // Precedence climbing over the left-associative levels of section 1.5. Recursion is bounded by the
    // number of levels, but each loop turn deepens the tree that later passes recurse over.
    private ExpressionSyntax? Binary(int minPrecedence)
    {
        var left = Unary();
        var height = _height;
        while (left is not null && BinaryOperatorOf(Peek()) is var (op, precedence) && precedence >= minPrecedence)
        {
            _position++;
            var right = Binary(precedence + 1);
            height = Math.Max(height, _height) + 1;
            Built(height);
            left = right is null ? null : new BinarySyntax(op, left, right);
        }

        _height = height;
        return left;
    }

    private static (BinaryOperator Operator, int Precedence)? BinaryOperatorOf(TokenKind kind) => kind switch
    {
        TokenKind.OrOr => (BinaryOperator.Or, 0),
        TokenKind.AndAnd => (BinaryOperator.And, 1),
        TokenKind.Equal => (BinaryOperator.Equal, 2),
        TokenKind.NotEqual => (BinaryOperator.NotEqual, 2),
        TokenKind.Greater => (BinaryOperator.Greater, 3),
        TokenKind.Less => (BinaryOperator.Less, 3),
        TokenKind.GreaterOrEqual => (BinaryOperator.GreaterOrEqual, 3),
        TokenKind.LessOrEqual => (BinaryOperator.LessOrEqual, 3),
        TokenKind.Plus => (BinaryOperator.Add, 4),
        TokenKind.Minus => (BinaryOperator.Subtract, 4),
        TokenKind.Star => (BinaryOperator.Multiply, 5),
        TokenKind.Slash => (BinaryOperator.Divide, 5),
        TokenKind.Percent => (BinaryOperator.Remainder, 5),
        _ => null,
    };

    private ExpressionSyntax? Unary()
    {
        if (Peek() is not (TokenKind.Bang or TokenKind.Minus))
        {
            return Postfix();
        }

        var op = Peek() == TokenKind.Bang ? UnaryOperator.Not : UnaryOperator.Negate;
        _position++;
        Enter();
        var operand = Unary();
        Exit();
        Built(_height + 1);
        return operand is null ? null : new UnarySyntax(op, operand);
    }

    private ExpressionSyntax? Postfix()
    {
        var target = Primary();
        var height = _height;
        while (target is not null)
        {
            if (Peek() == TokenKind.Dot)
            {
                _position++;
                Built(++height);
                target = new MemberSyntax(target, KeyText());

                // A member access is never callable: foo.bar(x) drops its arguments (section 1.5).
                SkipParenthesizedGroup();
            }
            else if (Peek() == TokenKind.OpenBracket)
            {
                _position++;
                Enter();
                var index = Expression();
                Exit();
                if (index is null || Peek() != TokenKind.CloseBracket)
                {
                    return null;
                }

                _position++;
                height = Math.Max(height, _height) + 1;
                Built(height);
                target = new IndexSyntax(target, index);
            }
            else
            {
                break;
            }
        }

        _height = height;
        return target;
    }

    private ExpressionSyntax? Primary()
    {
        var token = Current;
        Built(1);
        switch (token.Kind)
        {
            case TokenKind.Number:
                _position++;
                return new LiteralSyntax(token.Number);
            case TokenKind.String:
                _position++;
                return new LiteralSyntax(token.Text);
            case TokenKind.True or TokenKind.False:
                _position++;
                return new LiteralSyntax(token.Kind == TokenKind.True);
            case TokenKind.Null:
                _position++;
                return new LiteralSyntax(OuiValue.Null);
            case TokenKind.OpenBracket:
                _position++;
                return List(TokenKind.CloseBracket) is { } items ? new ArraySyntax(items) : null;
            case TokenKind.OpenBrace:
                _position++;
                return Object();
            case TokenKind.OpenParen:
                _position++;
                Enter();
                var inner = Expression();
                Exit();
                if (inner is null || Peek() != TokenKind.CloseParen)
                {
                    return null;
                }

                _position++;
                return inner;
            case TokenKind.Identifier:
                _position++;
                return Identifier(token.Text!);
            case TokenKind.StateName:
                _position++;
                if (Peek() == TokenKind.Assign)
                {
                    _position++;
                    Enter();
                    var assigned = Expression();
                    Exit();
                    Built(_height + 1);
                    return assigned is null ? null : new BindingAssignmentSyntax(token.Text!, assigned);
                }

                return new StateReferenceSyntax(token.Text!);
            case TokenKind.AtName:
                _position++;
                if (Peek() != TokenKind.OpenParen)
                {
                    return new CallSyntax(CallHeadKind.At, token.Text!, []);
                }

                _position++;
                return List(TokenKind.CloseParen) is { } atArgs ? new CallSyntax(CallHeadKind.At, token.Text!, atArgs) : null;
            default:
                return null;
        }
    }

    // Only an uppercase head makes a call a component call. Built-ins and actions need the @, so
    // Count(x) is the reference Count; a lowercase head is a reference too. Both drop their
    // arguments (section 1.5).
    private ExpressionSyntax? Identifier(string name)
    {
        if (Peek() != TokenKind.OpenParen)
        {
            return new ReferenceSyntax(name);
        }

        if (!ReservedNames.IsUppercaseIdentifier(name)
            || ReservedNames.BuiltinFunctions.Contains(name)
            || ReservedNames.BuiltinActions.Contains(name))
        {
            SkipParenthesizedGroup();
            return new ReferenceSyntax(name);
        }

        _position++;
        return List(TokenKind.CloseParen) is { } args ? new CallSyntax(CallHeadKind.Component, name, args) : null;
    }

    // Arguments and array elements: commas optional, trailing commas ignored. A token that cannot
    // start an expression (such as a stray closer) is skipped.
    private ImmutableArray<ExpressionSyntax>? List(TokenKind closer)
    {
        Enter();
        var items = ImmutableArray.CreateBuilder<ExpressionSyntax>();
        var tallest = 0;
        while (true)
        {
            var kind = Peek();
            if (kind == closer)
            {
                _position++;
                Exit();
                Built(tallest + 1);
                return items.ToImmutable();
            }

            if (kind == TokenKind.End)
            {
                return null;
            }

            if (!StartsExpression(kind))
            {
                _position++;
                continue;
            }

            if (Expression() is not { } item)
            {
                return null;
            }

            tallest = Math.Max(tallest, _height);
            items.Add(item);
        }
    }

    private ObjectSyntax? Object()
    {
        Enter();
        var entries = ImmutableArray.CreateBuilder<ObjectEntrySyntax>();
        var tallest = 0;
        while (true)
        {
            var kind = Peek();
            if (kind == TokenKind.CloseBrace)
            {
                _position++;
                Exit();
                Built(tallest + 1);
                return new ObjectSyntax(entries.ToImmutable());
            }

            if (kind == TokenKind.End)
            {
                return null;
            }

            if (kind == TokenKind.Comma)
            {
                _position++;
                continue;
            }

            var key = KeyText();
            ExpressionSyntax value = new LiteralSyntax(OuiValue.Null);
            if (Peek() == TokenKind.Colon)
            {
                _position++;
                if (Peek() is not (TokenKind.CloseBrace or TokenKind.Comma or TokenKind.End))
                {
                    if (Expression() is not { } parsed)
                    {
                        return null;
                    }

                    tallest = Math.Max(tallest, _height);
                    value = parsed;
                }
            }

            entries.Add(new ObjectEntrySyntax(key, value));
        }
    }

    // Object keys and member fields (section 1.5): names, strings, numbers (their source text),
    // component names and $names without the $. Any other token is the key "?".
    private string KeyText()
    {
        var token = Current;
        if (token.Kind == TokenKind.End)
        {
            return "?";
        }

        _position++;
        return token.Kind switch
        {
            TokenKind.Identifier or TokenKind.String or TokenKind.Number => token.Text!,
            TokenKind.StateName => token.Text![1..],
            _ => "?",
        };
    }

    private void SkipParenthesizedGroup()
    {
        if (Peek() != TokenKind.OpenParen)
        {
            return;
        }

        var depth = 0;
        do
        {
            var kind = Peek();
            if (kind == TokenKind.End)
            {
                return;
            }

            depth += kind == TokenKind.OpenParen ? 1 : kind == TokenKind.CloseParen ? -1 : 0;
            _position++;
        }
        while (depth > 0);
    }

    private static bool StartsExpression(TokenKind kind) => kind is TokenKind.Number or TokenKind.String or TokenKind.True
        or TokenKind.False or TokenKind.Null or TokenKind.OpenBracket or TokenKind.OpenBrace or TokenKind.OpenParen
        or TokenKind.Identifier or TokenKind.StateName or TokenKind.AtName or TokenKind.Bang or TokenKind.Minus;

    private void Enter()
    {
        if (++_depth > _maxDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            throw new TooDeepException();
        }
    }

    private void Exit() => _depth--;

    // Each production reports the height of the node it built in _height, so what a statement's tree
    // costs does not depend on where its parentheses fall.
    private void Built(int height)
    {
        _height = height;
        if (height + _depth > _maxDepth)
        {
            throw new TooDeepException();
        }
    }

    private sealed class TooDeepException : Exception;
}
