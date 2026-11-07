using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;

public interface IApplicableToGaugeProfile : IApplicableMod
{
    BmsGaugeProfile ApplyToGaugeProfile(BmsGaugeType type, BmsGaugeProfile profile);
}
