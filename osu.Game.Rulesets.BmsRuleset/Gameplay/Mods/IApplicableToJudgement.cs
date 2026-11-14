using RhythmEngine.Profiles.Bms;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;

public interface IApplicableToJudgement : IApplicableToBeatmapConverter
{
    BmsJudgementModifiers JudgementModifier { get; }

    void IApplicableToBeatmapConverter.ApplyToBeatmapConverter(IBeatmapConverter converter)
    {
        if (converter is BmsBeatmapConverter bmsConverter)
            bmsConverter.JudgementModifiers |= JudgementModifier;
    }
}
