using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModMirror : Mod, IApplicableAfterBeatmapConversion
{
    public override string Name => "Mirror";

    public override string Acronym => "MR";

    public override LocalisableString Description => BmsStrings.ModMirror;

    public override ModType Type => ModType.Conversion;

    public void ApplyToBeatmap(IBeatmap beatmap)
    {
        if (beatmap is BmsBeatmap bmsBeatmap)
            applyMirrorConversion(bmsBeatmap);
    }

    private static void applyMirrorConversion(BmsBeatmap beatmap)
    {
        foreach (var hitObject in beatmap.HitObjects)
            hitObject.Column = mirrorColumn(hitObject.Column, beatmap.LayoutVariant);
    }

    private static int mirrorColumn(int column, BmsLayoutVariant layoutVariant) => layoutVariant switch
    {
        BmsLayoutVariant.Bms5K or BmsLayoutVariant.Bms5K2P => column switch
        {
            0 => 0,
            1 => 5, 2 => 4, 3 => 3, 4 => 2, 5 => 1,
            _ => column,
        },
        BmsLayoutVariant.Bme7K or BmsLayoutVariant.Bme7K2P => column switch
        {
            0 => 0,
            1 => 7, 2 => 6, 3 => 5, 4 => 4, 5 => 3, 6 => 2, 7 => 1,
            _ => column,
        },
        BmsLayoutVariant.Bms5KDouble => column switch
        {
            0 => 0, 1 => 5, 2 => 4, 3 => 3, 4 => 2, 5 => 1,
            6 => 11, 7 => 10, 8 => 9, 9 => 8, 10 => 7, 11 => 6,
            _ => column,
        },
        BmsLayoutVariant.Bme7KDouble => column switch
        {
            0 => 0, 1 => 7, 2 => 6, 3 => 5, 4 => 4, 5 => 3, 6 => 2, 7 => 1,
            8 => 15, 9 => 14, 10 => 13, 11 => 12, 12 => 11, 13 => 10, 14 => 9, 15 => 8,
            _ => column,
        },
        BmsLayoutVariant.Pms9K => 8 - column,
        BmsLayoutVariant.Pms9KDouble => column <= 8 ? 8 - column : 26 - column,
        _ => column,
    };
}
