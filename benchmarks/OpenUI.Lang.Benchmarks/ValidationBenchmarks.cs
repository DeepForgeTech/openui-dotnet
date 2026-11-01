using BenchmarkDotNet.Attributes;
using OpenUI.Lang.Runtime;

namespace OpenUI.Lang.Benchmarks;

/// <summary>
/// Checking a form's fields against their rules: <see cref="OpenUISession.ValidateForm"/>. This is the
/// only public way into the ECMAScript regex engine, through the <c>email</c> and <c>pattern</c> rules.
/// </summary>
[MemoryDiagnoser]
public class ValidationBenchmarks
{
    private OpenUISession _email = null!;
    private OpenUISession _pattern = null!;
    private OpenUISession _distinct = null!;

    [ParamsAllValues]
    public Size Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var fields = Corpus.FormFields(Size);
        _email = Mount(fields, "{required: true, email: true}", i => $"person{i}@example.com");
        _pattern = Mount(fields, "{pattern: \"^[A-Z]{2}-[0-9]{4}$\"}", i => $"AB-{1000 + i}");
        _distinct = Corpus.Mount(Corpus.Form(fields, i => $"{{pattern: \"^AB-{1000 + i}$\"}}"), rows: 0);
        Fill(_distinct, fields, i => $"AB-{1000 + i}");
        Check.That(ValidateEmail().IsValid && ValidatePattern().IsValid && ValidatePatternDistinct().IsValid, "the fields do not pass their rules");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _email.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _pattern.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _distinct.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Benchmark]
    public FormValidation ValidateEmail() => _email.ValidateForm("signup");

    /// <summary>Every field shares one pattern, the best case for compiling a pattern once per pass.</summary>
    [Benchmark]
    public FormValidation ValidatePattern() => _pattern.ValidateForm("signup");

    /// <summary>Every field has its own pattern, the worst case: most are compiled for their one match.</summary>
    [Benchmark]
    public FormValidation ValidatePatternDistinct() => _distinct.ValidateForm("signup");

    private OpenUISession Mount(int fields, string rules, Func<int, string> value)
    {
        var session = Corpus.Mount(Corpus.Form(fields, rules), rows: 0);
        Fill(session, fields, value);
        return session;
    }

    private static void Fill(OpenUISession session, int fields, Func<int, string> value)
    {
        for (var i = 1; i <= fields; i++)
        {
            session.Forms.Set("signup", $"field{i}", value(i), "input");
        }
    }
}
