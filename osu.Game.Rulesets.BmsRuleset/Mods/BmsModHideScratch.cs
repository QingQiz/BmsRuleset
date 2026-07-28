using System;
using System.Linq;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModHideScratch : Mod, IApplicableToDrawableRuleset<BmsHitObject>, IApplicableAfterBeatmapConversion
{
    public override string Name => "Hide Scratch";

    public override string Acronym => "HS";

    public override IconUsage? Icon => OsuIcon.ModHoldOff;

    public override LocalisableString Description => BmsStrings.ModHideScratch;

    public override ModType Type => ModType.DifficultyReduction;

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
            .Where(x => x is not BmsLandmine && x.SampleKey.HasValue)
            .Select(x => new BmsSampleEvent(x.StartTime, 0, x.SampleKey!.Value, x.SampleVolume));

        var scratchEndSamples = scratchNotes
            .OfType<BmsLongNote>()
            .Where(x => x.TailSampleKey.HasValue)
            .Select(x => new BmsSampleEvent(x.EndTime, 0, x.TailSampleKey!.Value, x.TailSampleVolume));

        b.BackgroundSampleEvents = b.BackgroundSampleEvents.Concat(scratchStartSamples).Concat(scratchEndSamples).ToArray();
        b.HitObjects = b.HitObjects.Where(x => !BmsLayout.IsScratchColumn(x.Column, b.LayoutVariant)).ToList();
    }
}
