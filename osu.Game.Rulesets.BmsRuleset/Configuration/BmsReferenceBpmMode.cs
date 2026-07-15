using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Localisation;

namespace osu.Game.Rulesets.BmsRuleset.Configuration;

public enum BmsReferenceBpmMode
{
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.StartBpm))]
    StartBpm = 1,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.MaxBpm))]
    MaxBpm = 2,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.MainBpm))]
    MainBpm = 3,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.MinBpm))]
    MinBpm = 4,
}
