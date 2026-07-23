namespace osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

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
