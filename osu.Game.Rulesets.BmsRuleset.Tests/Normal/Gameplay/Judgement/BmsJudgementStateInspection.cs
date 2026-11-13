using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

internal static class BmsJudgementStateInspection
{
    internal static object GetRuntime(BmsJudgementView column, BmsHitObject note) => column.GetNote(note);
}
