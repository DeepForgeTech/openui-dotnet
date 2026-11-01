namespace OpenUI.Lang.Samples.BlazorApp;

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
}
