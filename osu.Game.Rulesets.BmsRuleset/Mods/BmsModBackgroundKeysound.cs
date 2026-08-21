using System.Linq;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModBackgroundKeysound : Mod, IApplicableAfterBeatmapConversion
{
    public override string Name => "Background Keysound";

    public override string Acronym => "BK";

    public override IconUsage? Icon => OsuIcon.ModSimplifiedRhythm;

    public override LocalisableString Description => BmsStrings.ModBackgroundKeysound;

    public override ModType Type => ModType.Fun;

    public void ApplyToBeatmap(IBeatmap beatmap)
    {
        if (beatmap is not BmsBeatmap b) return;

        var notes = b.HitObjects.Where(x => x is not BmsLandmine).ToArray();

        var starts = notes
            .Where(x => x.SampleKey.HasValue)
            .Select(x => new BmsSampleEvent(x.StartTime, 0, x.SampleKey!.Value, x.SampleVolume));

        var tails = notes
            .OfType<BmsLongNote>()
            .Where(x => x.TailSampleKey.HasValue)
            .Select(x => new BmsSampleEvent(x.EndTime, 0, x.TailSampleKey!.Value, x.TailSampleVolume));

        b.BackgroundSampleEvents = [.. b.BackgroundSampleEvents, .. starts, .. tails];

        foreach (var n in notes)
            n.SampleKey = null;

        foreach (var n in notes.OfType<BmsLongNote>())
            n.TailSampleKey = null;
    }
}
