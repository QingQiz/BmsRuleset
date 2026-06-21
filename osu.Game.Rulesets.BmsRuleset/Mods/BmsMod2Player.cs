using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModSecondPlayer : Mod, IApplicableAfterBeatmapConversion
{
    public override string Name => "2P";

    public override string Acronym => "2P";

    public override LocalisableString Description => "Switches to second-player cabinet layout (scratch on right).";

    public override ModType Type => ModType.Conversion;

    public void ApplyToBeatmap(IBeatmap beatmap)
    {
        if (beatmap is BmsBeatmap bms)
        {
            bms.LayoutVariant = BmsLayout.SecondPlayerVariant(bms.LayoutVariant);
        }
    }
}
