using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

internal readonly record struct BmsPracticeDensityWindow(double StartTime, double EndTime, int NoteCount)
{
    internal double NotesPerSecond => NoteCount;
}

internal sealed class BmsPracticeDensity
{
    private const double window_duration = 1000;
    private readonly Dictionary<long, int> counts = [];
    private readonly double minimum;
    private readonly double maximum;

    internal IReadOnlyList<BmsPracticeDensityWindow> Windows { get; }
    internal double MaximumNotesPerSecond { get; }

    internal BmsPracticeDensity(IEnumerable<BmsHitObject> notes, double minimum, double maximum)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum <= minimum)
            throw new ArgumentOutOfRangeException(nameof(maximum));

        this.minimum = minimum;
        this.maximum = maximum;
        foreach (var note in notes)
        {
            if (note is BmsLandmine or BmsInvisibleNote || !double.IsFinite(note.StartTime)
                || note.StartTime < minimum || note.StartTime >= maximum)
                continue;

            var index = indexAt(note.StartTime);
            counts[index] = counts.GetValueOrDefault(index) + 1;
        }

        // Sparse fixed real-time windows keep long empty sections cheap and independent of BPM, STOPs and viewport zoom.
        Windows = counts.OrderBy(entry => entry.Key).Select(entry => windowAt(entry.Key)).ToArray();
        MaximumNotesPerSecond = Windows.Select(window => window.NotesPerSecond).DefaultIfEmpty(0).Max();
    }

    internal BmsPracticeDensityWindow GetWindow(double time)
    {
        if (!double.IsFinite(time))
            time = minimum;
        if (time >= maximum)
            time = Math.BitDecrement(maximum);
        return windowAt(indexAt(Math.Max(minimum, time)));
    }

    private long indexAt(double time) => (long)Math.Floor((time - minimum) / window_duration);

    private BmsPracticeDensityWindow windowAt(long index)
    {
        var start = minimum + index * window_duration;
        return new BmsPracticeDensityWindow(start, start + window_duration, counts.GetValueOrDefault(index));
    }
}
