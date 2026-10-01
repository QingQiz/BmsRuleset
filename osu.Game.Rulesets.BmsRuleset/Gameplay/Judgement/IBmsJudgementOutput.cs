using System;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Rules;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

/// <summary>
/// Synchronous framework results and feedback, without ownership of judgement indexes.
/// </summary>
internal interface IBmsJudgementOutput
{
    BmsMainResultState GetMainResult(BmsHitObject note);

    /// <summary>
    /// Keeps history notification and derived-index refresh distinct so framework callbacks retain
    /// their intermediate observations. Each main result invokes both at its existing callback boundary.
    /// </summary>
    void SetResultCheckpoints(Action<BmsHitObject, double> recordHistory, Action<BmsHitObject> refreshIndexes);

    void ApplyMainResult(BmsHitObject note, HitResult result, in BmsFrameContext frame, bool suppressPenalty);

    void ApplyMainResult(BmsLongNote note, HitResult result, IReadOnlyList<BmsLongNoteEndpointResult> endpoints, in BmsFrameContext frame, bool suppressPenalty);

    void ApplySyntheticEndpoint(BmsLongNoteEndpointResult endpoint, in BmsFrameContext frame, bool suppressPenalty);

    void ApplyBodyTick(BmsLongNote note, bool holding, double scale, in BmsFrameContext frame);

    void ObserveSimulationStep(BmsHitObject note, in BmsFrameContext frame);

    /// <summary>
    /// Observes rule state synchronously before the execution context refreshes derived indexes.
    /// Main results are queried separately because publishing a head need not submit a main result.
    /// </summary>
    void PublishState(BmsLongNote note, in BmsLongNoteState state, in BmsFrameContext frame);

    void PublishState(BmsLandmine note, in BmsLandmineState state, in BmsFrameContext frame);

    void ApplyNonConsumingBad(BmsHitObject note, in BmsFrameContext frame);

    void ApplyEmptyPoor(double expectedTime, int column, in BmsFrameContext frame);

    void PlaySample(BmsHitObject note);

    void PlayTailSample(BmsLongNote note);

    void PlayKeySound();

    void PlayLandmineSample(BmsLandmine note);
}

internal readonly record struct BmsMainResultState(HitResult Type, double TimeAbsolute, double TimeOffset, double? GameplayRate, bool SuppressPenalty)
{
    internal bool HasResult => Type != HitResult.None;
}

internal readonly record struct BmsFrameContext(double SimulationTime, double GameplayRate, bool IsRewinding = false, bool IsPauseCompletion = false);

internal readonly record struct BmsPressContext(double InputTime, BmsFrameContext Frame, bool ReverseScratch = false, bool IsResumeRewinding = false);
