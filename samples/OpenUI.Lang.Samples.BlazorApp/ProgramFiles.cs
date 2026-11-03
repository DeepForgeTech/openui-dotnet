namespace OpenUI.Lang.Samples.BlazorApp;

/// <summary>One embedded program: its file name and its text.</summary>
public sealed record ProgramFile(string Name, string Text);

/// <summary>Reads the OpenUI Lang programs embedded in the assembly.</summary>
public static class ProgramFiles
{
    /// <summary>The program with this file name, such as <c>spray-planner.oui</c>.</summary>
    public static string Read(string name)
    {
        using var stream = typeof(ProgramFiles).Assembly.GetManifestResourceStream($"Programs/{name}")
            ?? throw new InvalidOperationException($"There is no embedded program named {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Every program in a folder under Programs, such as <c>gallery</c>, in file name order.</summary>
    public static IReadOnlyList<ProgramFile> ReadFolder(string folder)
    {
        var prefix = $"Programs/{folder}/";
        return
        [
            .. typeof(ProgramFiles).Assembly.GetManifestResourceNames()
                .Where(resource => resource.StartsWith(prefix, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .Select(resource => resource[prefix.Length..])
                .Select(name => new ProgramFile(name, Read($"{folder}/{name}"))),
        ];
    }
}
