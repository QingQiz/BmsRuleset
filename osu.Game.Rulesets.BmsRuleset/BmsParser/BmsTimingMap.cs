using System.Collections.Generic;
using System.Linq;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

public sealed class BmsTimingMap
{
    public int TickResolution { get; }

    public IReadOnlyList<BmsMeasureInfo> Measures { get; }

    public IReadOnlyList<BmsBpmEvent> BpmEvents { get; }

    public IReadOnlyList<BmsStopEvent> StopEvents { get; }

    public BmsTimingMap(int tickResolution, IEnumerable<BmsMeasureInfo> measures, IEnumerable<BmsBpmEvent> bpmEvents, IEnumerable<BmsStopEvent> stopEvents)
    {
        TickResolution = tickResolution;
        Measures = measures.OrderBy(m => m.Index).ToArray();
        BpmEvents = bpmEvents.OrderBy(e => e.Tick).ThenBy(e => e.Sequence).ToArray();
        StopEvents = stopEvents.OrderBy(e => e.Tick).ThenBy(e => e.Sequence).ToArray();
    }
}

public readonly record struct BmsMeasureInfo(int Index, long StartTick, long LengthTicks, double LengthRatio);

public readonly record struct BmsBpmEvent(long Tick, double Bpm, double Time, int Sequence = 0);

public readonly record struct BmsStopEvent(long Tick, double Duration, double StopValue, double Bpm, int Sequence);
