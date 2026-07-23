using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

public class BmsHitObject : HitObject
{
    public IBmsBeatmap Beatmap { get; set; } = null!;

    public int Column { get; set; }

    public ushort SourceChannel { get; set; }

    public ushort? SampleKey { get; set; }

    public int SampleVolume { get; set; } = 100;

    public double JudgementRate { get; set; } = double.NaN;

    public double EffectiveJudgementRate => double.IsNaN(JudgementRate)
        ? Beatmap.ExRank is { } exRank
            ? BmsJudgementProfileProvider.RateForExRank(Beatmap.LayoutVariant, exRank)
            : BmsJudgementProfileProvider.RateForRank(Beatmap.LayoutVariant, Beatmap.Rank)
        : JudgementRate;

    /// <summary>
    ///     Precomputed scroll position at <see cref="HitObject.StartTime"/>.
    ///     Computed once during beatmap loading via <see cref="BmsParser.BmsTimingMap.GetScrollPositionAtTime"/>.
    ///     Eliminates per-frame calls to the full timing-map lookup chain in the drawable hot path.
    /// </summary>
    public double ScrollPositionAtStartTime { get; set; }

    public BmsTickInfo TickInfo { get; set; } = new();

    public static BmsHitObject CreateForKind(bool isLongNote, bool isMine)
    {
        if (isMine)
            return new BmsLandmine();

        if (isLongNote)
            return new BmsLongNote();

        return new BmsNote();
    }

    public BmsHitObject ToTypedHitObject()
    {
        if (GetType() != typeof(BmsHitObject))
            return this;

        var typed = CreateForKind(this is BmsLongNote, this is BmsLandmine);
        CopyTo(typed);
        return typed;
    }

    public override Judgement CreateJudgement() => new BmsJudgement(this);

    protected override BmsHitWindows CreateHitWindows() => new(Beatmap.Rank, Beatmap.LayoutVariant, Column, EffectiveJudgementRate);

    protected virtual void CopyTo(BmsHitObject target)
    {
        target.TickInfo = TickInfo;
        target.StartTime = StartTime;
        target.Column = Column;
        target.SourceChannel = SourceChannel;
        target.SampleKey = SampleKey;
        target.SampleVolume = SampleVolume;
        target.JudgementRate = JudgementRate;
        target.ScrollPositionAtStartTime = ScrollPositionAtStartTime;
        target.Beatmap = Beatmap;
    }
}