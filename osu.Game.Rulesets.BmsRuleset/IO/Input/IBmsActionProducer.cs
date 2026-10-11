namespace osu.Game.Rulesets.BmsRuleset.IO.Input;

internal interface IBmsActionProducer
{
    /// <summary>
    /// Runs at simulation time. The implementation owns its schedule and emits due framework inputs.
    /// </summary>
    void UpdateActions(double time);
}
