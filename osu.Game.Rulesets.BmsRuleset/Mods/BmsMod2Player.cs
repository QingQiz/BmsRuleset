using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModSecondPlayer : Mod, IApplicableToBeatmapConverter
{
    public override string Name => "2P";

    public override string Acronym => "2P";

    public override LocalisableString Description => "Switches to second-player cabinet layout (scratch on right).";

    public override ModType Type => ModType.Conversion;

    public override double ScoreMultiplier => 1;

    public void ApplyToBeatmapConverter(IBeatmapConverter beatmapConverter)
    {
        if (beatmapConverter is BmsBeatmapConverter bmsConverter)
            bmsConverter.SecondPlayerMode = true;
    }
}
