using System.Runtime.CompilerServices;

namespace OpenUI.Lang.Blazor.Chat.Tests;

/// <summary>
/// bUnit's waits block a thread, and xUnit runs test classes side by side. On a runner with one or two cores the pool starts
/// with that many threads and adds more slowly, so a reply the test streams could wait past a test's budget to arrive.
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
