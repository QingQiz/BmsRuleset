using osu.Game.Rulesets.BmsRuleset.Scoring;
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

    public override Judgement CreateJudgement() => new BmsJudgement(IsMine);

    protected override BmsHitWindows CreateHitWindows() => new(BmsRank);
}
