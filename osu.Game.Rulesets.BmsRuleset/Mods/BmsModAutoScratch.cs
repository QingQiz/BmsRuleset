using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public partial class BmsModAutoScratch : Mod, IApplicableToDrawableRuleset<BmsHitObject>, IUpdatableByPlayfield
{
    public override string Name => "Auto Scratch";

    public override string Acronym => "AS";

    public override LocalisableString Description => "Automatically hits scratch notes.";

    public override ModType Type => ModType.DifficultyReduction;

    public override double ScoreMultiplier => 1;

    [SettingSource("Hide scratch", "Hides the scratch column while auto-scratching")]
    public Bindable<bool> HideScratch { get; } = new();

    private readonly HashSet<DrawableBmsHitObject> autoScratchLnHeads = [];

    private BmsPlayfield playfield = null!;

    public void ApplyToDrawableRuleset(DrawableRuleset<BmsHitObject> drawableRuleset)
    {
        playfield = (BmsPlayfield)drawableRuleset.Playfield;

        ((BmsDrawableRuleset)drawableRuleset).KeyBindingInputManager.Add(new ScratchInputInterceptor(playfield));

        if (HideScratch.Value)
        {
            foreach (var col in playfield.Stage.Columns)
            {
                if (col.IsScratch)
                    col.Hidden = true;
            }
        }
    }

    public void Update(Playfield _)
    {
        var now = playfield.Time.Current;

        autoScratchLnHeads.RemoveWhere(d => d.Judged);

        foreach (var drawable in playfield.HitObjectContainer.AliveObjects.OfType<DrawableBmsHitObject>())
        {
            if (drawable.Judged || drawable.HitObject.IsMine)
                continue;

            if (!BmsLayout.IsScratchColumn(drawable.HitObject.Column, playfield.LayoutVariant))
                continue;

            var note = drawable.HitObject;

            if (note.IsLongNote)
            {
                if (!autoScratchLnHeads.Contains(drawable))
                {
                    if (now >= note.StartTime)
                    {
                        playfield.PlayScratchSample(note);
                        if (drawable.TryHit())
                            autoScratchLnHeads.Add(drawable);
                    }
                }
                else if (now >= note.EndTime)
                {
                    drawable.TryRelease();
                }
            }
            else
            {
                if (now >= note.StartTime)
                {
                    playfield.PlayScratchSample(note);
                    drawable.TryHit();
                }
            }
        }
    }

    private sealed partial class ScratchInputInterceptor(BmsPlayfield playfield)
        : Component, IKeyBindingHandler<BmsAction>
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
