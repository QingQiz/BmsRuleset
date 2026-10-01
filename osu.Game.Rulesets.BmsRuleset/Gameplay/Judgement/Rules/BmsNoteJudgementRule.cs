using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Rules;

/// <summary>
/// Shared ordinary-note decisions, independent of pooled representations.
/// </summary>
internal sealed class BmsNoteJudgementRule
{
    internal HitResult JudgePress(HitResult selectedResult, bool judged) => judged ? HitResult.None : selectedResult;

    internal HitResult JudgePassive(BmsHitObject target, in BmsNoteJudgementWindows windows, double simulationTime, bool judged)
        // Subtraction and the strict boundary retain the framework's original time-offset comparison.
        => !judged && simulationTime - target.StartTime > windows.PassivePoorOffset ? HitResult.Meh : HitResult.None;

    internal bool TryPress(BmsHitObject note, in BmsJudgementExecutionContext execution, HitResult selectedResult, in BmsPressContext press)
    {
        var result = JudgePress(selectedResult, execution.GetMainResult(note).HasResult);
        if (result == HitResult.None || press.Frame.IsRewinding)
            return false;

        execution.ApplyMainResult(note, result, press.Frame);
        return true;
    }

    internal void CheckPassive(BmsHitObject note, in BmsNoteJudgementWindows windows, in BmsJudgementExecutionContext execution, in BmsFrameContext frame)
    {
        if (frame.IsRewinding)
            return;

        var result = JudgePassive(note, windows, frame.SimulationTime, execution.GetMainResult(note).HasResult);
        if (result != HitResult.None)
            execution.ApplyMainResult(note, result, frame);
    }
}

internal readonly struct BmsNoteJudgementWindows(BmsHitObject note)
{
    internal readonly double PassivePoorOffset = BmsJudgementProfileProvider.GetTable(
        note.Beatmap.LayoutVariant, note.Column, note.EffectiveJudgementRate, tail: false).SlowWindowFor(HitResult.Ok);

    internal double NextPassiveJudgementTime(BmsHitObject target) => target.StartTime + PassivePoorOffset;
}
