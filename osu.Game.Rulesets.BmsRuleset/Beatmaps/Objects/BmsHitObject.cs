using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;

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

    public double JudgementRate { get; set; } = double.NaN;

    public double EffectiveJudgementRate => double.IsNaN(JudgementRate)
        ? BmsJudgementProfileProvider.RateForDifficulty(Beatmap.LayoutVariant, Beatmap.Rank, Beatmap.ExRank)
        : JudgementRate;

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

    public override Judgement CreateJudgement() => new BmsJudgement(this);

    protected override BmsHitWindows CreateHitWindows() => new(this);

    protected virtual void CopyTo(BmsHitObject target)
    {
        target.StartTime = StartTime;
        target.Column = Column;
        target.SampleKey = SampleKey;
        target.SampleVolume = SampleVolume;
        target.AdditionalSampleKeys = AdditionalSampleKeys;
        target.JudgementRate = JudgementRate;
        target.ScrollPositionAtStartTime = ScrollPositionAtStartTime;
        target.Beatmap = Beatmap;
    }
}
