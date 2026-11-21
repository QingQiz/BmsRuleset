using System.Collections.Generic;
using RhythmEngine.Profiles.Bms;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Icons;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;

public class BmsModNoBad : Mod, IApplicableToGameplayProfile, IApplicableToScoreSelection
{
    public override string Name => BmsStrings.ModNoBadName.ToString();

    public override string Acronym => BmsStrings.ModNoBadAcronym.ToString();

    public override LocalisableString Description => BmsStrings.ModNoBad;

    public override ModType Type => ModType.DifficultyReduction;

    public override IconUsage? Icon => BmsIcons.NoBad;

    public IScoreSelectionRule ScoreSelectionRule => BmsModTypeScoreSelectionRule<BmsModNoBad>.REDUCTION;

    GameplayProfile IApplicableToGameplayProfile.ApplyToGameplayProfile(GameplayProfile profile, IEnumerable<Mod> mods)
        => ((BmsGameplayProfile)profile).WithJudgementModifier(BmsJudgementModifiers.NoBad);
}
