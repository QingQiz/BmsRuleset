using System.Collections.Generic;
using System;
using RhythmEngine.Profiles.Bms;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Icons;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;

public class BmsModNoGood : Mod, IApplicableToGameplayProfile
{
    public override string Name => "No Good";

    public override string Acronym => "NG";

    public override LocalisableString Description => BmsStrings.ModNoGood;

    public override ModType Type => ModType.DifficultyIncrease;

    public override IconUsage? Icon => BmsIcons.NoGood;

    public override Type[] IncompatibleMods => [typeof(BmsModNoGreat)];

    GameplayProfile IApplicableToGameplayProfile.ApplyToGameplayProfile(GameplayProfile profile, IEnumerable<Mod> mods)
        => ((BmsGameplayProfile)profile).WithJudgementModifier(BmsJudgementModifiers.NoGood);
}
