using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Icons;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModNoMine : Mod, IApplicableAfterBeatmapConversion, IApplicableToScoreSelection
{
    public override string Name => BmsStrings.ModNoMineName.ToString();

    public override string Acronym => "NM";

    public override IconUsage? Icon => BmsIcons.NoMine;

    public override LocalisableString Description => BmsStrings.ModNoMine;

    public override ModType Type => ModType.DifficultyReduction;

    public IApplicableToScoreSelection.ScoreSelectionDifficulty Difficulty => IApplicableToScoreSelection.ScoreSelectionDifficulty.Reduction;

    public void ApplyToBeatmap(IBeatmap beatmap)
    {
        if (beatmap is BmsBeatmap bmsBeatmap)
            bmsBeatmap.HitObjects.RemoveAll(hitObject => hitObject is BmsLandmine);
    }
}
