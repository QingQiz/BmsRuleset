namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

/// <summary>
/// Restores rule state once per rewind step, even when several representations observe the same step.
/// </summary>
internal sealed class BmsCoreJudgementSchedule(BmsColumnJudgementRuntime column)
{
    private (long Epoch, long Step) currentStep;
    private (long Epoch, long Step) lastRewind = (-1, -1);

    internal void BeginStep(long epoch, long step) => currentStep = (epoch, step);

    internal bool BeginRewindStep(in BmsFrameContext frame)
    {
        if (lastRewind == currentStep) return false;

        lastRewind = currentStep;
        column.RewindLongNotes(frame.SimulationTime);
        column.RewindLandmines(frame.SimulationTime);
        return true;
    }
}
