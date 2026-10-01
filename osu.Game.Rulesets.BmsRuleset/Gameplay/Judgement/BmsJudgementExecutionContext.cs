using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

/// <summary>
/// Borrows the column and output for one operation; no second copy of framework result state.
/// </summary>
internal readonly struct BmsJudgementExecutionContext(BmsColumnJudgementRuntime column, IBmsJudgementOutput output)
{
    internal BmsMainResultState GetMainResult(BmsHitObject note) => output.GetMainResult(note);

    internal void ApplyMainResult(BmsHitObject note, HitResult result, in BmsFrameContext frame)
        => output.ApplyMainResult(note, result, frame, result == HitResult.Meh && column.HasPmsMistake(note));

    internal void ApplyMainResult(BmsLongNote note, HitResult result, IReadOnlyList<BmsLongNoteEndpointResult> endpoints, in BmsFrameContext frame)
        => output.ApplyMainResult(note, result, endpoints, frame, result == HitResult.Meh && column.HasPmsMistake(note));

    internal void PublishState(BmsLongNote note, in BmsLongNoteState state, in BmsFrameContext frame)
    {
        // Synchronous observers see the new rule state with the preceding index checkpoint.
        output.PublishState(note, state, frame);
        column.RefreshExecutionState(note);
    }

    internal void ApplySyntheticEndpoint(BmsLongNoteEndpointResult endpoint, in BmsFrameContext frame)
        => output.ApplySyntheticEndpoint(endpoint, frame,
            endpoint.Kind == BmsLongNoteEndpointKind.Head && endpoint.Result == HitResult.Meh && column.HasPmsMistake(endpoint.Source));

    internal void ApplyBodyTick(BmsLongNote note, bool holding, double scale, in BmsFrameContext frame)
        => output.ApplyBodyTick(note, holding, scale, frame);
}
