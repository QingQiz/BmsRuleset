using System.Collections.Immutable;

namespace RhythmEngine.Profiles;

public enum KeyNoteKind
{
    Tap,
    Hold,
    ReleaseHold,
    SustainHold,
    Roll,
    Lift,
    Mine,
}

public sealed record KeyNote(
    int Id,
    int Lane,
    long StartTime,
    long EndTime,
    KeyNoteKind Kind = KeyNoteKind.Tap,
    bool Directional = false,
    double MineDamage = 10);

public readonly record struct TempoPoint(long Time, double BeatsPerMinute);

public readonly record struct ChartBreak(long StartTime, long EndTime);

/// <summary>Source-neutral input to the key-mode profile compilers. All times are microseconds.</summary>
public sealed class KeyChart
{
    public int LaneCount { get; }

    public ImmutableArray<KeyNote> Notes { get; }

    public ImmutableArray<TempoPoint> Tempos { get; }

    public ImmutableArray<ChartBreak> Breaks { get; }

    public KeyChart(int laneCount, IEnumerable<KeyNote> notes, IEnumerable<TempoPoint>? tempos = null, IEnumerable<ChartBreak>? breaks = null)
    {
        LaneCount = laneCount;
        Notes = [..notes];
        Tempos = [..(tempos ?? [new TempoPoint(long.MinValue, 120)])];
        Breaks = [..(breaks ?? [])];
        if (laneCount <= 0 || Tempos.IsEmpty || Tempos.Any(t => !double.IsFinite(t.BeatsPerMinute) || t.BeatsPerMinute <= 0)
            || Tempos.Zip(Tempos.Skip(1)).Any(p => p.First.Time >= p.Second.Time)
            || Notes.Any(n => n.StartTime < Tempos[0].Time || !double.IsFinite(n.MineDamage) || n.MineDamage < 0)
            || Breaks.Any(b => b.EndTime < b.StartTime) || Breaks.Zip(Breaks.Skip(1)).Any(b => b.First.EndTime > b.Second.StartTime))
            throw new ArgumentException("A chart requires positive tempos in strict time order covering every note.");
    }

    internal long MoveByBeats(long time, double beats)
    {
        var forward = beats >= 0;
        var remaining = Math.Abs(beats);
        var position = (double)time;
        var index = Tempos.Select((tempo, i) => (tempo, i)).Last(t => t.tempo.Time <= time).i;

        while (remaining > 0)
        {
            var boundary = forward
                ? index + 1 < Tempos.Length ? Tempos[index + 1].Time : double.PositiveInfinity
                : Tempos[index].Time;
            var available = Math.Abs(boundary - position) * Tempos[index].BeatsPerMinute / 60000000;
            if (remaining <= available || (!forward && index == 0))
                return checked((long)Math.Round(position + (forward ? 1 : -1) * remaining * 60000000 / Tempos[index].BeatsPerMinute));

            remaining -= available;
            position = boundary;
            index += forward ? 1 : -1;
        }

        return time;
    }

    internal Chart Compile(Func<KeyNote, INoteRule> createRule)
        => new(LaneCount, Notes.Select(n => new ChartNote(n.Id, n.Lane, n.StartTime, n.EndTime, createRule(n))));
}
