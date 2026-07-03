using System.ComponentModel;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

public enum BmsReferenceBpmMode
{
    [Description("Start BPM")]
    StartBpm = 1,

    [Description("Max BPM")]
    MaxBpm = 2,

    [Description("Main BPM")]
    MainBpm = 3,

    [Description("Min BPM")]
    MinBpm = 4,
}
