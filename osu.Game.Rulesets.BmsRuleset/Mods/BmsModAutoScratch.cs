using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Icons;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public partial class BmsModAutoScratch : Mod, IApplicableToDrawableRuleset<BmsHitObject>, IApplicableToScoreSelection
{
    public override string Name => BmsStrings.ModAutoScratchName.ToString();

    public override string Acronym => BmsStrings.ModAutoScratchAcronym.ToString();

    public override IconUsage? Icon => BmsIcons.AutoScratch;

    public override LocalisableString Description => BmsStrings.ModAutoScratch;

    public override ModType Type => ModType.Automation;

    public IScoreSelectionRule ScoreSelectionRule => BmsModTypeScoreSelectionRule<BmsModAutoScratch>.REDUCTION;

    public void ApplyToDrawableRuleset(DrawableRuleset<BmsHitObject> drawableRuleset)
    {
        var playfield = (BmsPlayfield)drawableRuleset.Playfield;
        playfield.EnableAutoScratch();
        ((BmsDrawableRuleset)drawableRuleset).KeyBindingInputManager.Add(new ScratchInputInterceptor(playfield));
    }

    private sealed partial class ScratchInputInterceptor(BmsPlayfield playfield) : Component, IKeyBindingHandler<BmsAction>
    {
        public bool OnPressed(KeyBindingPressEvent<BmsAction> e)
        {
            var column = BmsKeyBindingConfiguration.ActionToColumn(e.Action, playfield.LayoutVariant);
            return column != null && BmsLayout.IsScratchColumn(column.Value, playfield.LayoutVariant);
        }

        public void OnReleased(KeyBindingReleaseEvent<BmsAction> e)
        {
        }
    }
}
