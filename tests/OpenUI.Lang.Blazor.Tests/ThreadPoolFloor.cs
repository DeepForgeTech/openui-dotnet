using System.Runtime.CompilerServices;

namespace OpenUI.Lang.Blazor.Tests;

/// <summary>
/// bUnit's waits block a thread, and xUnit runs test classes side by side. On a runner with one or two cores the pool starts
/// with that many threads and adds more slowly, so the sample's in-process API could wait past a test's budget to answer.
/// </summary>
internal static class ThreadPoolFloor
{
    [ModuleInitializer]
    internal static void Raise()
    {
        ThreadPool.GetMinThreads(out var workers, out var io);
        ThreadPool.SetMinThreads(Math.Max(workers, 32), Math.Max(io, 32));
    }
}
