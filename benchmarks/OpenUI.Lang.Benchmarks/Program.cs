using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;

namespace OpenUI.Lang.Benchmarks;

/// <summary>Entry point. BenchmarkSwitcher takes <c>--filter</c>, <c>--list</c>, <c>--job</c> and the other BenchmarkDotNet arguments.</summary>
public static class Program
{
    public static int Main(string[] args)
    {
        // The full JSON report is the file ResultsComparer reads to compare two runs.
        var config = DefaultConfig.Instance
            .AddExporter(JsonExporter.Full)
            .WithArtifactsPath(ArtifactsPath());

        var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        return summaries.Any(s => s.HasCriticalValidationErrors || s.Reports.Any(r => !r.Success)) ? 1 : 0;
    }

    // benchmarks/artifacts next to the solution, whatever the working directory; --artifacts overrides it.
    private static string ArtifactsPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUI.Lang.sln")))
            {
                return Path.Combine(directory.FullName, "benchmarks", "artifacts");
            }
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "artifacts");
    }
}
