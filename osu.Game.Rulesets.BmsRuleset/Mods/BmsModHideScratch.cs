using System;
using System.Linq;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModHideScratch : Mod, IApplicableToDrawableRuleset<BmsHitObject>, IApplicableAfterBeatmapConversion
{
    public override string Name => "Hide Scratch";

    public override string Acronym => "HS";

    public override LocalisableString Description => "Remove scratch notes and scratch columns.";

    public override ModType Type => ModType.DifficultyReduction;

    public override double ScoreMultiplier => 1;

    public override Type[] IncompatibleMods => [typeof(BmsModSecondPlayer), typeof(BmsModAutoScratch)];

    private BmsPlayfield playfield = null!;

    public void ApplyToDrawableRuleset(DrawableRuleset<BmsHitObject> drawableRuleset)
    {
        playfield = (BmsPlayfield)drawableRuleset.Playfield;

        foreach (var col in playfield.Stage.Columns)
        {
            if (col.IsScratch)
                col.Hidden = true;
        }
    }

    public void ApplyToBeatmap(IBeatmap beatmap)
    {
        if (beatmap is not BmsBeatmap b) return;

        var scratchNotes = b.HitObjects
            .Where(x => BmsLayout.IsScratchColumn(x.Column, b.LayoutVariant)).ToArray();

        var scratchStartSamples = scratchNotes
            .Select(x => new BmsSampleEvent(x.StartTime, x.TickInfo.Tick, x.SampleKey));

        var scratchEndSamples = scratchNotes
            .Where(x => x.IsLongNote && x.TailSampleKey != 0) // have a tail sample
            .Select(x => new BmsSampleEvent(x.EndTime, x.TickInfo.EndTick, x.TailSampleKey));

        b.BackgroundSampleEvents = b.BackgroundSampleEvents.Concat(scratchStartSamples).Concat(scratchEndSamples).ToArray();
        b.HitObjects = b.HitObjects.Where(x => !BmsLayout.IsScratchColumn(x.Column, b.LayoutVariant)).ToList();
    }
}
