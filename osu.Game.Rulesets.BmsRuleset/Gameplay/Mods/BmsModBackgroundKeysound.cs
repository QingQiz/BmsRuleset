using System.Collections.Generic;
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

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;

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

        var starts = new List<BmsSampleEvent>();
        foreach (var note in notes)
        {
            if (note.SampleKey is { } key)
                starts.Add(new BmsSampleEvent(note.StartTime, key, note.SampleVolume));
            var additional = note.AdditionalSampleKeys;
            for (var i = 0; i < additional.Count; i++)
                starts.Add(new BmsSampleEvent(note.StartTime, additional[i], note.SampleVolume));
        }

        var tails = notes
            .OfType<BmsLongNote>()
            .Where(x => x.TailSampleKey.HasValue)
            .Select(x => new BmsSampleEvent(x.EndTime, x.TailSampleKey!.Value, x.TailSampleVolume));

        b.BackgroundSampleEvents = [.. b.BackgroundSampleEvents, .. starts, .. tails];

        foreach (var n in notes)
        {
            n.SampleKey = null;
            n.AdditionalSampleKeys = [];
        }

        foreach (var n in notes.OfType<BmsLongNote>())
            n.TailSampleKey = null;
    }
}
