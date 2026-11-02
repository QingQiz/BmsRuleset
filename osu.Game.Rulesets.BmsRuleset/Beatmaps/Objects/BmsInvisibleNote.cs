using osu.Game.Rulesets.Judgements;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

// Kept outside Beatmap.HitObjects so keysound changes cannot affect scoring or autoplay.
public sealed class BmsInvisibleNote : BmsHitObject
{
    public override Judgement CreateJudgement() => new IgnoreJudgement();
}
