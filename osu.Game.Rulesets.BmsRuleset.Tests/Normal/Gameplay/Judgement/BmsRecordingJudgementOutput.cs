#nullable enable
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

/// <summary>
/// Records engine output without feeding framework or presentation state back into judgement.
/// </summary>
internal sealed class BmsRecordingJudgementOutput : IBmsJudgementOutput
{
    private readonly Dictionary<BmsHitObject, BmsMainResultState> results = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BmsLongNote, IReadOnlyList<BmsLongNoteEndpointResult>> endpoints = new(ReferenceEqualityComparer.Instance);
    internal Action<BmsHitObject, BmsFrameContext>? ResultApplied;
    internal Action<BmsHitObject, BmsFrameContext>? AfterResultApplied;
    internal Action<string>? Feedback;
    internal Action<double, int, HitResult>? NonConsumingJudgement;
    internal Action<BmsLongNote, BmsLongNoteState, BmsFrameContext>? StatePublished;
    internal Action<BmsLongNoteEndpointResult, bool>? SyntheticEndpoint;
    internal List<BmsLongNoteEndpointResult> SyntheticEndpoints { get; } = [];
    internal Action<BmsFrameContext>? BodyTick;
    internal List<(int Column, bool Holding, double Time)> BodyTicks { get; } = [];
    internal int MainResults { get; private set; }
    internal int NonConsumingResults { get; private set; }

    internal void Reset()
    {
        results.Clear();
        endpoints.Clear();
        SyntheticEndpoints.Clear();
        BodyTicks.Clear();
        MainResults = NonConsumingResults = 0;
    }

    public BmsMainResultState GetMainResult(BmsHitObject note)
        => results.TryGetValue(note, out var result) ? result : new BmsMainResultState(HitResult.None, 0);

    internal IReadOnlyList<BmsLongNoteEndpointResult> GetEndpoints(BmsLongNote note) => endpoints.GetValueOrDefault(note) ?? [];

    public void ApplyMainResult(BmsHitObject note, HitResult result, in BmsFrameContext frame, bool suppressPenalty)
    {
        if (GetMainResult(note).HasResult)
            throw new InvalidOperationException("The main result already exists.");

        results[note] = new BmsMainResultState(result, Math.Min(frame.SimulationTime - note.GetEndTime(), note.MaximumJudgementOffset));
        MainResults++;
        ResultApplied?.Invoke(note, frame);
        AfterResultApplied?.Invoke(note, frame);
    }

    public void ApplyMainResult(BmsLongNote note, HitResult result, IReadOnlyList<BmsLongNoteEndpointResult> endpointResults, in BmsFrameContext frame, bool suppressPenalty)
    {
        endpoints[note] = endpointResults.ToArray();
        ApplyMainResult(note, result, frame, suppressPenalty);
    }

    public void PublishState(BmsLongNote note, NoteSnapshot state, in BmsFrameContext frame)
        => StatePublished?.Invoke(note, BmsLongNoteState.From(note, state, frame.SimulationTime, BmsGameplayProfile.INSTANCE), frame);

    public void ApplySyntheticEndpoint(BmsLongNoteEndpointResult endpoint, in BmsFrameContext frame, bool suppressPenalty)
    {
        SyntheticEndpoint?.Invoke(endpoint, false);
        SyntheticEndpoints.Add(endpoint);
        SyntheticEndpoint?.Invoke(endpoint, true);
    }

    public void ApplyBodyTick(in BmsFrameContext frame)
    {
        var judgement = frame.EngineResult!.Event;
        BodyTicks.Add((judgement.Lane, judgement.Result == BmsResults.SUSTAIN_GAIN, frame.SimulationTime));
        BodyTick?.Invoke(frame);
    }

    public void ApplyNonConsumingResult(BmsHitObject note, HitResult result, in BmsFrameContext frame)
    {
        NonConsumingJudgement?.Invoke(note.StartTime, note.Column, result);
        if (BmsGameplayProfile.INSTANCE.EventKind(frame.EngineResult!.Event) == GameplayEventKind.NonConsumingHit)
        {
            NonConsumingResults++;
            Feedback?.Invoke($"bad:{note.StartTime}:{note.Column}");
        }
        else
            Feedback?.Invoke($"empty-poor:{note.StartTime}:{note.Column}");
    }

    public void PlaySample(BmsHitObject note) => Feedback?.Invoke($"sample:{note.SampleKey}:{note.SampleVolume}");
    public void PlayTailSample(BmsLongNote note) => Feedback?.Invoke($"sample:{note.TailSampleKey}:{note.TailSampleVolume}");
    public void PlayKeySound() => Feedback?.Invoke("keysound");

    public void PlayLandmineSample(BmsLandmine note) => Feedback?.Invoke("landmine");

    public void PublishState(BmsLandmine note, in BmsLandmineState state, in BmsFrameContext frame)
    {
    }
}
