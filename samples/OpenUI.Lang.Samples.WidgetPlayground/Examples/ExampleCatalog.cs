namespace OpenUI.Lang.Samples.WidgetPlayground.Examples;

/// <summary>An example program.</summary>
/// <param name="Title">Its first line's <c># comment</c>, or its file name.</param>
/// <param name="Source">The program text.</param>
public sealed record Example(string Title, string Source);

/// <summary>The example programs embedded from <c>Examples/Widgets</c> and <c>Examples/Pages</c>, sorted by file name.</summary>
/// <param name="Widgets">Custom widget programs.</param>
/// <param name="Pages">Page programs.</param>
public sealed record ExampleCatalog(IReadOnlyList<Example> Widgets, IReadOnlyList<Example> Pages)
{
    /// <summary>Reads the embedded examples.</summary>
    /// <exception cref="InvalidOperationException">A folder has no examples, so the build lost them.</exception>
    public static ExampleCatalog Load() => new(Read("Examples/Widgets/"), Read("Examples/Pages/"));

    private static List<Example> Read(string folder)
    {
        var assembly = typeof(ExampleCatalog).Assembly;
        var examples = assembly.GetManifestResourceNames()
            .Where(name => name.Replace('\\', '/').StartsWith(folder, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(name =>
            {
                using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
                var source = reader.ReadToEnd().ReplaceLineEndings("\n");
                var firstLine = source.Split('\n', 2)[0];
                var title = firstLine.StartsWith("# ", StringComparison.Ordinal) ? firstLine[2..].Trim() : Path.GetFileNameWithoutExtension(name);
                return new Example(title, source);
            })
            .ToList();
        return examples.Count > 0 ? examples : throw new InvalidOperationException($"No embedded examples under {folder}.");
    }
}
