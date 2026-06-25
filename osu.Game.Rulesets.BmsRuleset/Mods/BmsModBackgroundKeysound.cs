using System.Linq;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModBackgroundKeysound : Mod, IApplicableAfterBeatmapConversion
{
    public override string Name => "Background Keysound";

    public override string Acronym => "BK";

    public override LocalisableString Description => "Play all keysounds as background audio instead of on key press.";

    public override ModType Type => ModType.Fun;

    public void ApplyToBeatmap(IBeatmap beatmap)
    {
        if (beatmap is not BmsBeatmap b) return;

        var notes = b.HitObjects.Where(x => x is not BmsLandmine).ToArray();

        var starts = notes
            .Select(x => new BmsSampleEvent(x.StartTime, x.TickInfo.Tick, x.SampleKey));

        var tails = notes
            .OfType<BmsLongNote>()
            .Where(x => x.TailSampleKey != 0)
            .Select(x => new BmsSampleEvent(x.EndTime, x.TickInfo.EndTick, x.TailSampleKey));

        b.BackgroundSampleEvents = b.BackgroundSampleEvents.Concat(starts).Concat(tails).ToArray();

        foreach (var n in notes)
        {
            n.SampleKey = 0;
            n.SamplePath = string.Empty;
        }

        foreach (var n in notes.OfType<BmsLongNote>())
        {
            n.TailSampleKey = 0;
            n.TailSamplePath = string.Empty;
        }
    }
}
