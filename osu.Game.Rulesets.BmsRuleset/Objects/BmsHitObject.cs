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

    public string SourceChannel { get; set; } = string.Empty;

    public string SampleKey { get; set; } = string.Empty;

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

    /// <summary>BMS #RANK value stamped from the beatmap during conversion. 0=Very Hard, 1=Hard, 2=Normal, 3=Easy, 4=Very Easy.</summary>
    public int BmsRank { get; set; } = 2;

    public override Judgement CreateJudgement() => new BmsJudgement(IsMine);

    protected override BmsHitWindows CreateHitWindows() => new(BmsRank);
}
