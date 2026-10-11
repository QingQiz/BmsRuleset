namespace osu.Game.Rulesets.BmsRuleset.IO.Input;

internal interface IBmsActionFilter
{
    bool AllowPhysicalAction(BmsAction action);
    bool AllowReplayAction(BmsAction action);
}
