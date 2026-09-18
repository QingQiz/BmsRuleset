using osu.Game.Rulesets.Judgements;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

// Kept outside Beatmap.HitObjects so keysound changes cannot affect scoring or autoplay.
public sealed class BmsInvisibleNote : BmsHitObject
{
    public override Judgement CreateJudgement() => new IgnoreJudgement();

    internal BmsInvisibleNote CloneForBeatmap(IBmsBeatmap beatmap)
    {
        var clone = new BmsInvisibleNote();
        CopyTo(clone);
        clone.Beatmap = beatmap;
        return clone;
    }
}
