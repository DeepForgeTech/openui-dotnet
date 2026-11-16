namespace OpenUI.Lang.Blazor.Mud;

/// <summary>
/// What tells apart the entries of a list a program named: the name, and which time it came up. A program can repeat
/// a name or leave it out, so names alone do not identify anything, and repeats are told apart by position.
/// </summary>
internal readonly record struct OccurrenceKey(string Name, int Occurrence)
{
    public static OccurrenceKey[] Of(IEnumerable<string> names)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        return [.. names.Select(name =>
        {
            var occurrence = seen.GetValueOrDefault(name);
            seen[name] = occurrence + 1;
            return new OccurrenceKey(name, occurrence);
        })];
    }
}
