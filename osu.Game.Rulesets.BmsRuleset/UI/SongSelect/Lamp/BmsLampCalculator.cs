using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Lamp;

public static class BmsLampCalculator
{
    public static BmsLamp Calculate(ScoreInfo? score)
        => score == null ? BmsLamp.NoPlay : GameplayProfile.FromMods(score.Mods).LampFor(score);
}
