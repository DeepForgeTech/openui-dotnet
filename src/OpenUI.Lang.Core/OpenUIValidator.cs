using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;

namespace OpenUI.Lang;

/// <summary>A server-side check of one program: its document and its evaluation with default state and no tool data (language.md, sections 8.1 and 8.4).</summary>
/// <param name="Document">The parsed document, final.</param>
/// <param name="Render">The evaluation with state defaults, queries at their defaults and mutations idle.</param>
public sealed record ValidationReport(OpenUIDocument Document, RenderResult Render)
{
    /// <summary>True when an error is fatal: nothing would render (language.md, section 8.2).</summary>
    public bool IsFatal => Render.Errors.HasFatal;

    /// <summary>Every visible parse, bind and evaluation error, each once (section 8).</summary>
    public OpenUIErrorList Errors => Render.Errors;
}

/// <summary>
/// Validates programs where no tools run, such as a server checking model output before sending it
/// on (language.md, section 8). Stateless and thread-safe. A function with no implementation
/// evaluates to null with no error.
/// </summary>
public sealed class OpenUIValidator
{
    private readonly OpenUIParser _parser;
    private readonly OpenUILimits _limits;

    /// <summary>Creates a validator for <paramref name="library"/>.</summary>
    /// <param name="library">The component library the program is written against (prompt.md, section 2).</param>
    /// <param name="options">Limits; null for the defaults.</param>
    public OpenUIValidator(OpenUILibrary library, OpenUIParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        _parser = new OpenUIParser(library, options);
        _limits = (options ?? OpenUIParserOptions.Default).Limits;
    }

    /// <summary>Parses and evaluates a complete program (language.md, sections 2 to 8).</summary>
    public ValidationReport Validate(string text)
    {
        var document = _parser.Parse(text);
        return new ValidationReport(document, OpenUIEvaluator.Evaluate(document, limits: _limits));
    }

    /// <summary>Parses a program as it streams in and evaluates the final document (language.md, section 4).</summary>
    public async ValueTask<ValidationReport> ValidateAsync(IAsyncEnumerable<string> chunks, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        OpenUIDocument? last = null;
        await foreach (var document in _parser.ParseStreamAsync(chunks, ct).ConfigureAwait(false))
        {
            last = document;
        }

        last ??= _parser.Parse("");
        return new ValidationReport(last, OpenUIEvaluator.Evaluate(last, limits: _limits));
    }
}
