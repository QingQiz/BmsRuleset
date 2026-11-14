using System.Collections.Immutable;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;

namespace RhythmEngine.Profiles.Etterna;

internal sealed class EtternaSearchTimeline
{
    private readonly ImmutableArray<long> times;
    private readonly ImmutableArray<double> beats;
    private readonly ImmutableArray<double> tempos;

    internal EtternaSearchTimeline(KeyChart chart)
    {
        // KeyChart's initial tempo also applies before its first explicit point.
        // long.MinValue is the existing initial-tempo sentinel, not a beat anchor.
        times = [.. chart.Tempos.Where(t => t.Time != long.MinValue).Select(t => t.Time).Append(0).Distinct().Order()];
        var tempoValues = ImmutableArray.CreateBuilder<double>(times.Length);
        var tempoIndex = 0;

        foreach (var time in times)
        {
            while (tempoIndex + 1 < chart.Tempos.Length && chart.Tempos[tempoIndex + 1].Time <= time)
                tempoIndex++;
            tempoValues.Add(chart.Tempos[tempoIndex].BeatsPerMinute);
        }

        tempos = tempoValues.MoveToImmutable();
        var accumulated = new double[times.Length];
        for (var i = 1; i < times.Length; i++)
            accumulated[i] = accumulated[i - 1] + Time.ToMilliseconds(times[i] - times[i - 1]) * tempos[i - 1] / 60000;
        var zero = accumulated[times.IndexOf(0)];
        beats = [.. accumulated.Select(b => b - zero)];
    }

    internal int RowAt(long time)
    {
        var index = times.BinarySearch(time);
        if (index < 0)
            index = Math.Max(0, ~index - 1);
        var beat = beats[index] + Time.ToMilliseconds(time - times[index]) * tempos[index] / 60000;
        return checked((int)MathF.Round((float)beat * 48, MidpointRounding.AwayFromZero));
    }

    internal (int Row, int Radius) SearchAt(long time)
    {
        var row = RowAt(time);
        var radius = Math.Max(RowAt(time + 180_000) - row, row - RowAt(time - 180_000)) + 48;
        return (row, radius);
    }
}
