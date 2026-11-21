using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;

internal interface IApplicableToGameplayProfile
{
    GameplayProfile ApplyToGameplayProfile(GameplayProfile profile, IEnumerable<Mod> mods);
}
