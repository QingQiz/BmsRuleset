using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.Scoring;
using RhythmEngine.Core;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

/// <summary>
/// Synchronous framework results and feedback, without ownership of judgement indexes.
/// </summary>
internal interface IBmsJudgementOutput
{
    void ApplyMainResult(BmsHitObject note, HitResult result, in BmsFrameContext frame, bool suppressPenalty);

    void ApplyMainResult(BmsLongNote note, HitResult result, IReadOnlyList<BmsLongNoteEndpointResult> endpoints, in BmsFrameContext frame, bool suppressPenalty);

    void ApplySyntheticEndpoint(BmsLongNoteEndpointResult endpoint, in BmsFrameContext frame, bool suppressPenalty);

    void ApplyBodyTick(in BmsFrameContext frame);

    /// <summary>
    /// Receives immutable engine output. Presentation cannot write back into judgement state.
    /// </summary>
    void PublishState(BmsLongNote note, NoteSnapshot state, in BmsFrameContext frame);

    void PublishState(BmsLandmine note, in BmsLandmineState state, in BmsFrameContext frame);

    void ApplyNonConsumingResult(BmsHitObject note, HitResult result, in BmsFrameContext frame);

    void PlaySample(BmsHitObject note);

    void PlayTailSample(BmsLongNote note);

    void PlayKeySound();

    void PlayLandmineSample(BmsLandmine note);
}

internal readonly record struct BmsFrameContext(double SimulationTime, double GameplayRate, bool IsRewinding = false)
{
    internal JudgementResult? EngineResult { get; init; }
}
