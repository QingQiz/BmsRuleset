using System.Collections.Immutable;

namespace RhythmEngine.Core;

public readonly record struct TimingWindow(string Result, long Early, long Late, bool IncludeEarly = true, bool IncludeLate = true)
{
    public bool Contains(long offset) => (IncludeEarly ? offset >= Early : offset > Early)
                                         && (IncludeLate ? offset <= Late : offset < Late);
}

/// <summary>Ordered best-first; windows may overlap and need not be symmetric or nested.</summary>
public sealed class TimingWindows
{
    public ImmutableArray<TimingWindow> Entries { get; }

    public TimingWindows(IEnumerable<TimingWindow> entries)
    {
        Entries = [..entries];
        if (Entries.IsEmpty || Entries.Any(w => string.IsNullOrWhiteSpace(w.Result) || w.Early > w.Late))
            throw new ArgumentException("Nonempty, named, ordered time ranges are required.", nameof(entries));
    }

    public string? ResultFor(long offset)
    {
        foreach (var window in Entries)
        {
            if (window.Contains(offset))
                return window.Result;
        }

        return null;
    }

    public int RankOf(string result)
    {
        for (var i = 0; i < Entries.Length; i++)
        {
            if (Entries[i].Result == result)
                return i;
        }

        return Entries.Length;
    }
}
