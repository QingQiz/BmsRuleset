using System;
using RhythmEngine.Profiles.Bms;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Icons;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;

public class BmsModNoGreat : Mod, IApplicableToGameplayProfile
{
    public override string Name => "No Great";

    public override string Acronym => "NE";

    public override LocalisableString Description => BmsStrings.ModNoGreat;

    public override ModType Type => ModType.DifficultyIncrease;

    public override IconUsage? Icon => BmsIcons.NoGreat;

    public override Type[] IncompatibleMods => [typeof(BmsModNoGood)];

    GameplayProfile IApplicableToGameplayProfile.ApplyToGameplayProfile(GameplayProfile profile)
        => ((BmsGameplayProfile)profile).WithJudgementModifier(BmsJudgementModifiers.NoGreat);
}
