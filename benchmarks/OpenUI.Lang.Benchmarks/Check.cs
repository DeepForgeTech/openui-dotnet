namespace OpenUI.Lang.Benchmarks;

/// <summary>Setup checks, so a benchmark that stops doing the work it names fails instead of reporting a fast time.</summary>
internal static class Check
{
    public static void That(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"The benchmark setup is wrong: {what}");
        }
    }
}
