using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModMirror : Mod, IApplicableToBeatmapConverter
{
    public override string Name => "Mirror";

    public override string Acronym => "MR";

    public override LocalisableString Description => "Mirrors the key layout.";

    public override ModType Type => ModType.Conversion;

    public override double ScoreMultiplier => 1;

    public void ApplyToBeatmapConverter(IBeatmapConverter beatmapConverter)
    {
        if (beatmapConverter is BmsBeatmapConverter bmsConverter)
            bmsConverter.Mirror = true;
    }
}
