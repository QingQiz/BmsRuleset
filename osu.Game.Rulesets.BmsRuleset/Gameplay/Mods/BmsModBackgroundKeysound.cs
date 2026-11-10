using System.Linq;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
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

        var starts = notes.SelectMany(note => note.GetSamples());

        var tails = notes
            .OfType<BmsLongNote>()
            .SelectMany(x => x.GetTailSamples());

        b.BackgroundSampleEvents = [.. b.BackgroundSampleEvents, .. starts, .. tails];

        foreach (var n in notes)
        {
            n.SampleKey = null;
            n.AdditionalSampleKeys = [];
        }

        foreach (var n in notes.OfType<BmsLongNote>())
        {
            n.TailSampleKey = null;
            n.AdditionalTailSampleKeys = [];
        }
    }
}
