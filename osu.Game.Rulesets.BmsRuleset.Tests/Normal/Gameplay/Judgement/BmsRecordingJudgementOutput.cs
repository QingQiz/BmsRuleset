#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

/// <summary>
/// A test authority for main results. All candidate maintenance stays in the real column context.
/// </summary>
internal sealed class BmsRecordingJudgementOutput : IBmsJudgementOutput
{
    private readonly Dictionary<BmsHitObject, BmsMainResultState> results = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BmsHitObject, double?> rawTimes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BmsLongNote, IReadOnlyList<BmsLongNoteEndpointResult>> endpoints = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<BmsHitObject> appliedResults = new();
    private Action<BmsHitObject> checkpoint = null!;
    internal Action<BmsHitObject, BmsFrameContext>? BeforeResultCheckpoint;
    internal Action<BmsHitObject, BmsFrameContext>? AfterResultCheckpoint;
    internal Action<string>? Feedback;
    internal Action<BmsLongNote, BmsLongNoteState, BmsFrameContext>? StatePublished;
    internal Action<BmsLongNoteEndpointResult, bool>? SyntheticEndpoint;
    internal List<BmsLongNoteEndpointResult> SyntheticEndpoints { get; } = [];
    internal List<double> SyntheticApplicationTimes { get; } = [];
    internal Action<BmsLongNote, bool, double, BmsFrameContext>? BodyTick;
    internal List<(BmsLongNote Note, bool Holding, double Scale, double Time)> BodyTicks { get; } = [];
    internal Action<BmsHitObject>? Reverting;
    internal Action<BmsHitObject>? AfterReset;
    internal int MainResults { get; private set; }
    internal int NonConsumingResults { get; private set; }

    public BmsMainResultState GetMainResult(BmsHitObject note)
        => results.TryGetValue(note, out var result) ? result : new BmsMainResultState(HitResult.None, note.GetEndTime(), 0, null, false);

    internal double? GetRawTime(BmsHitObject note) => rawTimes.GetValueOrDefault(note);
    internal IReadOnlyList<BmsLongNoteEndpointResult> GetEndpoints(BmsLongNote note) => endpoints.GetValueOrDefault(note) ?? [];

    public void SetResultCheckpoint(Action<BmsHitObject> resultCheckpoint) => checkpoint = resultCheckpoint;

    public void ApplyMainResult(BmsHitObject note, HitResult result, in BmsFrameContext frame, bool suppressPenalty)
    {
        if (GetMainResult(note).HasResult)
            throw new InvalidOperationException("The main result already exists.");

        rawTimes[note] = frame.SimulationTime;
        results[note] = new BmsMainResultState(result, Math.Min(frame.SimulationTime, note.GetEndTime() + note.MaximumJudgementOffset),
            Math.Min(frame.SimulationTime - note.GetEndTime(), note.MaximumJudgementOffset), frame.GameplayRate, suppressPenalty);
        MainResults++;
        appliedResults.Push(note);
        BeforeResultCheckpoint?.Invoke(note, frame);
        checkpoint(note);
        AfterResultCheckpoint?.Invoke(note, frame);
    }

    public void ApplyMainResult(BmsLongNote note, HitResult result, IReadOnlyList<BmsLongNoteEndpointResult> endpointResults, in BmsFrameContext frame, bool suppressPenalty)
    {
        endpoints[note] = endpointResults.ToArray();
        ApplyMainResult(note, result, frame, suppressPenalty);
    }

    public void PublishState(BmsLongNote note, in BmsLongNoteState state, in BmsFrameContext frame)
        => StatePublished?.Invoke(note, state, frame);

    public void ApplySyntheticEndpoint(BmsLongNoteEndpointResult endpoint, in BmsFrameContext frame)
    {
        SyntheticEndpoint?.Invoke(endpoint, false);
        SyntheticEndpoints.Add(endpoint);
        SyntheticApplicationTimes.Add(frame.SimulationTime);
        SyntheticEndpoint?.Invoke(endpoint, true);
    }

    public void ApplyBodyTick(BmsLongNote note, bool holding, double scale, in BmsFrameContext frame)
    {
        BodyTicks.Add((note, holding, scale, frame.SimulationTime));
        BodyTick?.Invoke(note, holding, scale, frame);
    }

    internal void RevertSyntheticAfter(double time)
    {
        // The submission stack is authoritative even when a legacy endpoint stamps input time.
        while (SyntheticApplicationTimes.Count > 0 && SyntheticApplicationTimes[^1] > time)
        {
            SyntheticApplicationTimes.RemoveAt(SyntheticApplicationTimes.Count - 1);
            SyntheticEndpoints.RemoveAt(SyntheticEndpoints.Count - 1);
        }
    }

    internal void RevertAfter(double time)
    {
        while (appliedResults.TryPeek(out var note))
        {
            var result = results[note];
            if (rawTimes[note] <= time)
                break;
            appliedResults.Pop();
            Reverting?.Invoke(note);
            // Framework Reset preserves GameplayRate, supplementary data and endpoint arrays.
            results[note] = result with { Type = HitResult.None, TimeAbsolute = note.GetEndTime(), TimeOffset = 0 };
            rawTimes[note] = null;
            AfterReset?.Invoke(note);
        }
    }

    internal void Reapply(BmsHitObject note)
    {
        // DrawableHitObject.onDefaultsApplied assigns TimeOffset after Apply, preserving the existing result object.
        var result = GetMainResult(note);
        results[note] = result with { TimeAbsolute = note.GetEndTime(), TimeOffset = 0 };
        rawTimes[note] = note.GetEndTime();
    }

    public void ApplyNonConsumingBad(BmsHitObject note, in BmsFrameContext frame)
    {
        NonConsumingResults++;
        Feedback?.Invoke($"bad:{note.StartTime}:{note.Column}");
    }

    public void PlaySample(BmsHitObject note) => Feedback?.Invoke($"sample:{note.SampleKey}:{note.SampleVolume}");
    public void PlayTailSample(BmsLongNote note) => Feedback?.Invoke($"sample:{note.TailSampleKey}:{note.TailSampleVolume}");
    public void PlayKeySound() => Feedback?.Invoke("keysound");
    public void ApplyEmptyPoor(double expectedTime, int column, in BmsFrameContext frame) => Feedback?.Invoke($"empty-poor:{expectedTime}:{column}");
    public void PlayLandmineSample(BmsLandmine note) => Feedback?.Invoke("landmine");
    public void PublishState(BmsLandmine note, in BmsLandmineState state, in BmsFrameContext frame) { }
}
