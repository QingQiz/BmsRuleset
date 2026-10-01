using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

internal readonly record struct BmsLandmineState(double? HandledTime, bool Detonated);
internal delegate void BmsLandmineStateObserver(in BmsLandmineState state, in BmsFrameContext frame);

internal sealed class BmsLandmineRuntime(BmsLandmine note)
{
    internal BmsLandmine Note { get; } = note;
    internal BmsLandmineState State;
}

internal sealed class BmsLandmineJudgementRule
{
    internal const double PASSING_LIFETIME = 100;

    internal void CheckPassing(BmsLandmineRuntime state, IBmsJudgementOutput output, in BmsFrameContext frame, bool holding)
    {
        var note = state.Note;
        if (frame.IsRewinding || state.State.HandledTime.HasValue || frame.SimulationTime < note.StartTime || output.GetMainResult(note).HasResult)
            return;
        var detonated = holding && frame.SimulationTime < note.StartTime + PASSING_LIFETIME;
        state.State = new BmsLandmineState(frame.SimulationTime, detonated);
        if (detonated)
        {
            output.PlayLandmineSample(note);
            output.ApplyMainResult(note, HitResult.Meh, frame, false);
        }
        output.PublishState(note, state.State, frame);
    }

    internal void Rewind(BmsLandmineRuntime state, double time)
    {
        if (state.State.HandledTime > time)
            state.State = default;
    }
}
