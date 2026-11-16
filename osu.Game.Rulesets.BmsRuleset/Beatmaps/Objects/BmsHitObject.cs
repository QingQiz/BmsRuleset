using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

public class BmsHitObject : HitObject
{
    public IBmsBeatmap Beatmap { get; set; } = null!;

    public int Column { get; set; }

    public int? SampleKey { get; set; }

    public int SampleVolume { get; set; } = 100;

    public IReadOnlyList<int> AdditionalSampleKeys { get; set; } = [];

    internal IEnumerable<BmsSampleEvent> GetSamples()
    {
        if (SampleKey is { } key)
            yield return new BmsSampleEvent(StartTime, key, SampleVolume);
        foreach (var additional in AdditionalSampleKeys)
            yield return new BmsSampleEvent(StartTime, additional, SampleVolume);
    }

    /// <summary>Per-note EXRANK percentage from the chart; null uses the chart's header difficulty.</summary>
    public double? ExRank { get; set; }

    /// <summary>
    ///     Precomputed scroll position at <see cref="HitObject.StartTime"/>.
    ///     Computed once during beatmap loading via <see cref="BmsParser.BmsTimingMap.GetScrollPositionAtTime"/>.
    ///     Eliminates per-frame calls to the full timing-map lookup chain in the drawable hot path.
    /// </summary>
    public double ScrollPositionAtStartTime { get; set; }

    public static BmsHitObject CreateForKind(bool isLongNote, bool isMine)
    {
        if (isMine)
            return new BmsLandmine();

        if (isLongNote)
            return new BmsLongNote();

        return new BmsNote();
    }

    internal BmsHitObject CloneForConversion()
    {
        BmsHitObject clone = this switch
        {
            BmsLongNote => new BmsLongNote(),
            BmsLandmine => new BmsLandmine(),
            BmsInvisibleNote => new BmsInvisibleNote(),
            _ => new BmsNote(),
        };
        CopyTo(clone);
        clone.Samples = Samples.ToList();
        return clone;
    }

    internal BmsInvisibleNote CreateKeySoundState(IBmsBeatmap beatmap)
    {
        var clone = new BmsInvisibleNote();
        CopyTo(clone);
        clone.Beatmap = beatmap;
        return clone;
    }

    public override Judgement CreateJudgement() => new BmsJudgement();

    // Conversion precedes profile selection. Compilation publishes the selected framework windows.
    protected override HitWindows CreateHitWindows() => HitWindows.Empty;

    protected virtual void CopyTo(BmsHitObject target)
    {
        target.StartTime = StartTime;
        target.Column = Column;
        target.SampleKey = SampleKey;
        target.SampleVolume = SampleVolume;
        target.AdditionalSampleKeys = AdditionalSampleKeys;
        target.ExRank = ExRank;
        target.ScrollPositionAtStartTime = ScrollPositionAtStartTime;
        target.Beatmap = Beatmap;
    }
}
