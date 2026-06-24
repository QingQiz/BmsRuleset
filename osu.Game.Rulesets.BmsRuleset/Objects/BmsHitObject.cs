using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;

namespace osu.Game.Rulesets.BmsRuleset.Objects;

public class BmsHitObject : HitObject, IHasDuration
{
    public double EndTime => StartTime + Duration;

    public int BmsRank => Beatmap?.Rank ?? 2;

    public BmsLayoutVariant LayoutVariant => Beatmap?.LayoutVariant ?? BmsLayoutVariant.Bme7K;

    public BmsLongNoteMode LongNoteMode => IsLongNote ? Beatmap?.LockedLongNoteMode ?? BmsLongNoteMode.Undefined : BmsLongNoteMode.Undefined;

    public string LandmineExplosionSamplePath
        => Beatmap?.SampleDefinitions.TryGetValue(0, out var path) == true ? path : string.Empty;

    public BmsTickInfo TickInfo { get; set; } = new();

    /// <summary>
    ///     Projected osu! duration used by framework lifetime, judgement, and sorting systems.
    /// </summary>
    public double Duration { get; set; }

    public int Column { get; set; }

    public ushort SourceChannel { get; set; }

    public ushort SampleKey { get; set; }

    public string SamplePath { get; set; } = string.Empty;

    public bool IsLongNote { get; set; }

    public bool IsMine { get; set; }

    public double LandmineDamagePercent { get; set; }

    public ushort TailSampleKey { get; set; }

    public string TailSamplePath { get; set; } = string.Empty;

    /// <summary>
    ///     Precomputed scroll position at <see cref="HitObject.StartTime"/>.
    ///     Computed once during beatmap loading via <see cref="BmsParser.BmsTimingMap.GetScrollPositionAtTime"/>.
    ///     Eliminates per-frame calls to the full timing-map lookup chain in the drawable hot path.
    /// </summary>
    public double ScrollPositionAtStartTime { get; set; }

    /// <summary>
    ///     Precomputed scroll position at <see cref="EndTime"/>.
    ///     Computed once during beatmap loading for all objects (equals <see cref="ScrollPositionAtStartTime"/> for non-LN notes).
    /// </summary>
    public double ScrollPositionAtEndTime { get; set; }

    public IBmsBeatmap? Beatmap { get; set; }

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

        var typed = CreateForKind(IsLongNote, IsMine);
        CopyTo(typed);
        return typed;
    }

    /// <summary>
    ///     Creates a synthetic short-note endpoint for CN/HCN tail judgement
    ///     as a separate scoring event from the head judgement.
    /// </summary>
    public BmsHitObject CreateSyntheticEndpoint(double endpointTime)
    {
        var endpoint = new BmsNote();
        CopyTo(endpoint);

        endpoint.StartTime = endpointTime;
        endpoint.Duration = 0;
        endpoint.IsLongNote = false;
        endpoint.TailSampleKey = 0;
        endpoint.TailSamplePath = string.Empty;

        return endpoint;
    }

    public override Judgement CreateJudgement() => new BmsJudgement(IsMine);

    protected void CopyTo(BmsHitObject target)
    {
        target.TickInfo = TickInfo;
        target.StartTime = StartTime;
        target.Duration = Duration;
        target.Column = Column;
        target.SourceChannel = SourceChannel;
        target.SampleKey = SampleKey;
        target.SamplePath = SamplePath;
        target.IsLongNote = IsLongNote;
        target.IsMine = IsMine;
        target.LandmineDamagePercent = LandmineDamagePercent;
        target.TailSampleKey = TailSampleKey;
        target.TailSamplePath = TailSamplePath;
        target.ScrollPositionAtStartTime = ScrollPositionAtStartTime;
        target.ScrollPositionAtEndTime = ScrollPositionAtEndTime;
        target.Beatmap = Beatmap;
    }

    protected override BmsHitWindows CreateHitWindows() => new(BmsRank, LayoutVariant, Column);
}

public class BmsNote : BmsHitObject
{
    public BmsNote()
    {
        IsLongNote = false;
        IsMine = false;
    }
}

public class BmsLongNote : BmsHitObject
{
    public BmsLongNote()
    {
        IsLongNote = true;
        IsMine = false;
    }
}

public class BmsLandmine : BmsHitObject
{
    public BmsLandmine()
    {
        IsLongNote = false;
        IsMine = true;
    }
}
