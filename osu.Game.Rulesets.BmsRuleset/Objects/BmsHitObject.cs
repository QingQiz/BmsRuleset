using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;

namespace osu.Game.Rulesets.BmsRuleset.Objects;

public class BmsHitObject : HitObject, IHasDuration
{
    public double EndTime => StartTime + Duration;

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

    /// <summary>
    ///     Landmine gauge damage in percentage points. For BMS D/E channels this is base36 value / 2.
    /// </summary>
    public double LandmineDamagePercent { get; set; }

    /// <summary>
    ///     Sample played when this landmine explodes. BMS defines this through <c>#WAV00</c>.
    /// </summary>
    public string LandmineExplosionSamplePath { get; set; } = string.Empty;

    /// <summary>
    ///     The BMS sample key of the LN terminating cell (e.g. "01", "AZ").
    ///     Empty when the tail has no distinct sample (LNTYPE 2 or no #WAV for the terminating value).
    /// </summary>
    public ushort TailSampleKey { get; set; }

    /// <summary>
    ///     The resolved sample path for the LN tail's key sound.
    ///     Empty when the tail should play no sound (no fallback to the head's sample).
    /// </summary>
    public string TailSamplePath { get; set; } = string.Empty;

    /// <summary>BMS #RANK value stamped from the beatmap during conversion. 0=Very Hard, 1=Hard, 2=Normal, 3=Easy, 4=Very Easy.</summary>
    public int BmsRank { get; set; } = 2;

    public BmsLayoutVariant LayoutVariant { get; set; } = BmsLayoutVariant.Bme7K;

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
        target.LandmineExplosionSamplePath = LandmineExplosionSamplePath;
        target.TailSampleKey = TailSampleKey;
        target.TailSamplePath = TailSamplePath;
        target.BmsRank = BmsRank;
        target.LayoutVariant = LayoutVariant;
        target.ScrollPositionAtStartTime = ScrollPositionAtStartTime;
        target.ScrollPositionAtEndTime = ScrollPositionAtEndTime;
    }

    public override Judgement CreateJudgement() => new BmsJudgement(IsMine);

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
