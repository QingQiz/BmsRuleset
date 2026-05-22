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

    public override Judgement CreateJudgement() => new BmsJudgement();

    protected override BmsHitWindows CreateHitWindows() => new();
}
