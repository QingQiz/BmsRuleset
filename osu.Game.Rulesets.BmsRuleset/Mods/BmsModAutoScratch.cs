using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.BmsRuleset.UI.Icons;
using osu.Game.Rulesets.BmsRuleset.UI.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public partial class BmsModAutoScratch : Mod, IApplicableToDrawableRuleset<BmsHitObject>, IUpdatableByPlayfield
{
    public override string Name => "Auto Scratch";

    public override string Acronym => "AS";

    public override IconUsage? Icon => BmsIcons.AutoScratch;

    public override LocalisableString Description => BmsStrings.ModAutoScratch;

    public override ModType Type => ModType.Automation;

    private BmsPlayfield playfield = null!;

    public void ApplyToDrawableRuleset(DrawableRuleset<BmsHitObject> drawableRuleset)
    {
        playfield = (BmsPlayfield)drawableRuleset.Playfield;

        ((BmsDrawableRuleset)drawableRuleset).KeyBindingInputManager.Add(new ScratchInputInterceptor(playfield));
    }

    public void Update(Playfield _)
    {
        var now = playfield.Time.Current;

        foreach (var drawable in playfield.Stage.Columns
                     .SelectMany(c => c.HitObjectContainer.AliveObjects)
                     .OfType<DrawableBmsHitObject>())
        {
            if (drawable.HitObject is BmsLandmine)
                continue;

            if (!BmsLayout.IsScratchColumn(drawable.HitObject.Column, playfield.LayoutVariant))
                continue;

            var note = drawable.HitObject;

            if (drawable is ILongNoteHolder longNote)
            {
                if (longNote.IsAutomaticallyHeld && now < note.StartTime)
                    longNote.IsAutomaticallyHeld = false;

                if (!longNote.IsAutomaticallyHeld)
                {
                    if (!drawable.Judged && now >= note.StartTime)
                    {
                        var headTable = BmsJudgementProfileProvider.GetTable(playfield.LayoutVariant, drawable.HitObject.Column, drawable.HitObject.EffectiveJudgementRate, tail: false);
                        var headResult = headTable.ResultForOffset(now - drawable.HitObject.StartTime);
                        if (headResult != HitResult.None && drawable.TryHit(headResult))
                        {
                            playfield.Stage.Columns[note.Column].PlaySample(note.SampleKey, note.SampleVolume);
                            longNote.IsAutomaticallyHeld = longNote.IsHoldingLongNote;
                        }
                    }
                }
                else if (now >= ((BmsLongNote)note).EndTime)
                {
                    var tailTable = BmsJudgementProfileProvider.GetTable(playfield.LayoutVariant, note.Column, note.EffectiveJudgementRate, tail: true);
                    if (longNote.TryRelease(now - ((BmsLongNote)note).EndTime, tailTable) && note is BmsLongNote ln)
                    {
                        longNote.IsAutomaticallyHeld = false;
                        playfield.Stage.Columns[note.Column].PlaySample(ln.TailSampleKey, ln.TailSampleVolume);
                    }
                    else if (!longNote.IsHoldingLongNote)
                    {
                        longNote.IsAutomaticallyHeld = false;
                    }
                }
            }
            else
            {
                if (!drawable.Judged && now >= note.StartTime)
                {
                    var table = BmsJudgementProfileProvider.GetTable(playfield.LayoutVariant, drawable.HitObject.Column, drawable.HitObject.EffectiveJudgementRate, tail: false);
                    var result = table.ResultForOffset(now - drawable.HitObject.StartTime);
                    if (result != HitResult.None)
                    {
                        playfield.Stage.Columns[note.Column].PlaySample(note.SampleKey, note.SampleVolume);
                        drawable.TryHit(result);
                    }
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
