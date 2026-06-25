using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;

namespace osu.Game.Rulesets.BmsRuleset.Objects;

public class BmsHitObject : HitObject
{
    public IBmsBeatmap Beatmap { get; set; } = null!;

    public int Column { get; set; }

    public ushort SourceChannel { get; set; }

    public ushort SampleKey { get; set; }

    public string SamplePath { get; set; } = string.Empty;

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

    protected override BmsHitWindows CreateHitWindows() => new(Beatmap.Rank, Beatmap.LayoutVariant, Column);

    protected virtual void CopyTo(BmsHitObject target)
    {
        target.TickInfo = TickInfo;
        target.StartTime = StartTime;
        target.Column = Column;
        target.SourceChannel = SourceChannel;
        target.SampleKey = SampleKey;
        target.SamplePath = SamplePath;
        target.ScrollPositionAtStartTime = ScrollPositionAtStartTime;
        target.Beatmap = Beatmap;
    }
}

public class BmsNote : BmsHitObject;

public class BmsLongNote : BmsHitObject, IHasDuration
{

    public double EndTime => StartTime + Duration;

    public double Duration { get; set; }

    /// <summary>
    ///     Precomputed scroll position at <see cref="EndTime"/>.
    ///     Computed once during beatmap loading.
    /// </summary>
    public double ScrollPositionAtEndTime { get; set; }

    public ushort TailSampleKey { get; set; }

    public string TailSamplePath { get; set; } = string.Empty;

    /// <summary>
    ///     Creates a synthetic short-note endpoint for CN/HCN tail judgement
    ///     as a separate scoring event from the head judgement.
    /// </summary>
    public BmsHitObject CreateSyntheticEndpoint(double endpointTime)
    {
        var endpoint = new BmsNote();
        CopyTo(endpoint);

        endpoint.StartTime = endpointTime;

        return endpoint;
    }

    protected override void CopyTo(BmsHitObject target)
    {
        base.CopyTo(target);
        if (target is BmsLongNote ln)
        {
            ln.Duration = Duration;
            ln.ScrollPositionAtEndTime = ScrollPositionAtEndTime;
            ln.TailSampleKey = TailSampleKey;
            ln.TailSamplePath = TailSamplePath;
        }
    }
}

public class BmsLandmine : BmsHitObject
{
    public double LandmineDamagePercent { get; set; }

    protected override void CopyTo(BmsHitObject target)
    {
        base.CopyTo(target);
        if (target is BmsLandmine mine)
            mine.LandmineDamagePercent = LandmineDamagePercent;
    }
}
