using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModSecondPlayer : Mod, IApplicableAfterBeatmapConversion
{
    public override string Name => "2P";

    public override string Acronym => "2P";

    public override IconUsage? Icon => OsuIcon.ModSwap;

    public override LocalisableString Description => BmsStrings.ModSecondPlayer;

    public override ModType Type => ModType.Conversion;

    public void ApplyToBeatmap(IBeatmap beatmap)
    {
        if (beatmap is BmsBeatmap bms)
        {
            bms.LayoutVariant = BmsLayout.SecondPlayerVariant(bms.LayoutVariant);
        }
    }
}
