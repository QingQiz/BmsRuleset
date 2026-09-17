using System;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModConstant : Mod, IApplicableToDrawableRuleset<BmsHitObject>, IApplicableToScoreSelection
{
    public override string Name => "Constant";

    public override string Acronym => "CN";

    public override IconUsage? Icon => OsuIcon.ModConstantSpeed;

    public override LocalisableString Description => BmsStrings.ModConstant;

    public override ModType Type => ModType.DifficultyReduction;

    public IApplicableToScoreSelection.ScoreSelectionDifficulty Difficulty => IApplicableToScoreSelection.ScoreSelectionDifficulty.Reduction;

    public override Type[] IncompatibleMods => [];

    public void ApplyToDrawableRuleset(DrawableRuleset<BmsHitObject> drawableRuleset)
    {
        if (drawableRuleset.Playfield is BmsPlayfield playfield)
            playfield.ScrollController.ConstantScrollActive = true;
    }
}
