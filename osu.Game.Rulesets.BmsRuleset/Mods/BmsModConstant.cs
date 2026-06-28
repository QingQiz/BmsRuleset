using System;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModConstant : Mod, IApplicableToDrawableRuleset<BmsHitObject>
{
    public override string Name => "Constant";

    public override string Acronym => "CN";

    public override LocalisableString Description => "Disables BPM-based scroll speed changes.";

    public override ModType Type => ModType.DifficultyReduction;

    public override Type[] IncompatibleMods => [];

    public void ApplyToDrawableRuleset(DrawableRuleset<BmsHitObject> drawableRuleset)
    {
        if (drawableRuleset.Playfield is BmsPlayfield playfield)
            playfield.ScrollController.ConstantScrollActive = true;
    }
}
