using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;

internal interface IApplicableToGameplayProfile
{
    GameplayProfile ApplyToGameplayProfile(GameplayProfile profile);
}
