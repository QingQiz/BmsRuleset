using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using System;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Rules;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Judgement;

internal delegate void BmsSimulationStepObserver(in BmsFrameContext frame);

internal sealed partial class BmsJudgementBridge : IBmsJudgementOutput
{
    private readonly BmsPlayfield playfield;
    private readonly BmsColumn column;
    private readonly Dictionary<BmsHitObject, Binding> representations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DrawableBmsHitObject, Binding> boundObjects = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BmsHitObject, BmsJudgementResult> results = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DrawableBmsHitObject, (long Generation, BmsLongNoteStateObserver Observer)> presentationObservers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DrawableBmsHitObject, (long Generation, BmsLandmineStateObserver Observer)> landmineObservers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DrawableBmsHitObject, (long Generation, BmsSimulationStepObserver Observer)> simulationObservers = [];
    private Action<BmsHitObject> resultCheckpoint = null!;
    private Action<BmsHitObject, double> resultHistoryCheckpoint = null!;

    internal BmsJudgementBridge(BmsPlayfield playfield, BmsColumn column)
    {
        this.playfield = playfield;
        this.column = column;
    }

    internal void Bind(DrawableBmsHitObject drawable)
    {
        var binding = new Binding(drawable.HitObject, drawable, drawable.BindingGeneration, this);
        representations.Add(binding.Note, binding);
        boundObjects.Add(drawable, binding);
        results[drawable.HitObject] = (BmsJudgementResult)drawable.Result;
        drawable.OnNewResult += binding.ResultHandler;
    }

    internal void ObserveLongNoteState(DrawableBmsHitObject drawable, BmsLongNoteStateObserver observer)
        => presentationObservers[drawable] = (drawable.BindingGeneration, observer);

    internal void ObserveSimulationSteps(DrawableBmsHitObject drawable, BmsSimulationStepObserver observer)
        => simulationObservers[drawable] = (drawable.BindingGeneration, observer);

    internal void ObserveLandmineState(DrawableBmsHitObject drawable, BmsLandmineStateObserver observer)
        => landmineObservers[drawable] = (drawable.BindingGeneration, observer);

    internal void Unbind(DrawableBmsHitObject drawable, long generation)
    {
        if (boundObjects.TryGetValue(drawable, out var binding) && binding.Generation == generation && binding.ScheduledObject is { } scheduled)
            context.Schedule.Unbind(scheduled);

        container.RemoveVisualObservation(drawable, generation);
        if (simulationObservers.TryGetValue(drawable, out var stepObserver) && stepObserver.Generation == generation)
            simulationObservers.Remove(drawable);
        if (presentationObservers.TryGetValue(drawable, out var observer) && observer.Generation == generation)
            presentationObservers.Remove(drawable);
        if (landmineObservers.TryGetValue(drawable, out var mineObserver) && mineObserver.Generation == generation)
            landmineObservers.Remove(drawable);

        // Pool return may have cleared HitObject; only the binding owns the old identity.
        if (binding == null || binding.Generation != generation)
            return;

        boundObjects.Remove(drawable);
        representations.Remove(binding.Note);
        drawable.OnNewResult -= binding.ResultHandler;
    }

    internal DrawableBmsHitObject GetRepresentation(BmsHitObject note)
        => representations.TryGetValue(note, out var binding) && isCurrent(binding)
            ? binding.Drawable
            : throw new InvalidOperationException("A main result requires the current representation.");

    private bool isCurrent(Binding binding)
        => binding.Drawable.BindingGeneration == binding.Generation && ReferenceEquals(binding.Drawable.HitObject, binding.Note)
                                                                    && boundObjects.TryGetValue(binding.Drawable, out var current) && ReferenceEquals(current, binding);

    private sealed class Binding
    {
        internal readonly BmsHitObject Note;
        internal readonly DrawableBmsHitObject Drawable;
        internal readonly long Generation;
        internal readonly Action<DrawableHitObject, JudgementResult> ResultHandler;
        internal BmsScheduledObject? ScheduledObject;

        internal Binding(BmsHitObject note, DrawableBmsHitObject drawable, long generation, BmsJudgementBridge output)
        {
            Note = note;
            Drawable = drawable;
            Generation = generation;
            // A captured callback can outlive OnFree when the same drawable already represents another object.
            ResultHandler = (_, _) =>
            {
                if (output.isCurrent(this)) output.resultCheckpoint(Note);
            };
        }
    }

    public BmsMainResultState GetMainResult(BmsHitObject note)
    {
        if (!results.TryGetValue(note, out var result))
            throw new InvalidOperationException("The framework has not supplied this note's result.");

        return new BmsMainResultState(result.Type, result.TimeAbsolute, result.TimeOffset, result.GameplayRate, result.SuppressPenalty);
    }

    public void SetResultCheckpoints(Action<BmsHitObject, double> recordHistory, Action<BmsHitObject> refreshIndexes)
    {
        resultHistoryCheckpoint = recordHistory;
        resultCheckpoint = refreshIndexes;
    }

    internal void OnFrameworkNewResult(DrawableHitObject drawable, JudgementResult result)
    {
        if (drawable is not DrawableBmsHitObject || result.HitObject is not BmsHitObject note || note is BmsLandmine)
            return;

        // The column notification precedes the bound result callback; retain both observation boundaries.
        // An earlier observer may have returned the drawable to its pool, so the result owns the identity.
        resultHistoryCheckpoint(note, column.Time.Current);
        if (BmsColumn.ShouldTriggerHitExplosion(result.Type))
            column.TriggerHitExplosion(note is BmsLongNote ? BmsHitLightStyle.Hold : BmsHitLightStyle.Note);
    }

    public void ApplyMainResult(BmsHitObject note, HitResult result, in BmsFrameContext frame, bool suppressPenalty)
    {
        var drawable = GetRepresentation(note);
        var rate = (drawable.Clock as IGameplayClock)?.GetTrueGameplayRate() ?? drawable.Clock.Rate;
        if (drawable.Time.Current != frame.SimulationTime || rate != frame.GameplayRate)
            throw new InvalidOperationException("The main result must use the framework's application time and gameplay rate.");

        drawable.ApplyBmsResult(result, suppressPenalty);
    }

    public void ApplyMainResult(BmsLongNote note, HitResult result, IReadOnlyList<BmsLongNoteEndpointResult> endpoints, in BmsFrameContext frame, bool suppressPenalty)
    {
        ((BmsLongNoteJudgementResult)GetRepresentation(note).Result).SetEndpointResults(endpoints);
        ApplyMainResult(note, result, frame, suppressPenalty);
    }

    public void PublishState(BmsLongNote note, in BmsLongNoteState state, in BmsFrameContext frame)
    {
        var drawable = GetRepresentation(note);
        var observer = presentationObservers[drawable];
        if (observer.Generation == drawable.BindingGeneration)
            observer.Observer(state, frame);
    }

    public void ObserveSimulationStep(BmsHitObject note, in BmsFrameContext frame)
    {
        // Feedback keeps its synchronous checkpoint while concrete representations opt in at binding time.
        var drawable = GetRepresentation(note);
        if (simulationObservers.TryGetValue(drawable, out var observer) && observer.Generation == drawable.BindingGeneration)
            observer.Observer(frame);
    }

    public void ApplySyntheticEndpoint(BmsLongNoteEndpointResult endpoint, in BmsFrameContext frame, bool suppressPenalty)
    {
        var drawable = GetRepresentation(endpoint.Source);
        if (drawable.Time.Current != frame.SimulationTime || playfield.Time.Current != frame.SimulationTime)
            throw new InvalidOperationException("Supplementary endpoints require the current representation and application time.");

        playfield.ApplySyntheticLongNoteEndpoint(endpoint, suppressPenalty);
    }

    public void PublishState(BmsLandmine note, in BmsLandmineState state, in BmsFrameContext frame)
    {
        var drawable = GetRepresentation(note);
        var observer = landmineObservers[drawable];
        if (observer.Generation == drawable.BindingGeneration)
            observer.Observer(state, frame);
    }

    public void ApplyBodyTick(BmsLongNote note, bool holding, double scale, in BmsFrameContext frame)
    {
        var drawable = GetRepresentation(note);
        if (drawable.Time.Current != frame.SimulationTime || playfield.Time.Current != frame.SimulationTime)
            throw new InvalidOperationException("Body ticks require the current representation and simulation time.");

        playfield.ApplyHellChargeTick(holding, scale);
    }

    public void ApplyNonConsumingBad(BmsHitObject note, in BmsFrameContext frame)
    {
        if (playfield.Time.Current != frame.SimulationTime)
            throw new InvalidOperationException("Supplementary results must use the current application time.");

        playfield.RegisterNonConsumingBad(note.StartTime, note.Column);
    }

    public void PlaySample(BmsHitObject note) => column.PlaySample(note.SampleKey, note.SampleVolume);

    public void PlayTailSample(BmsLongNote note) => column.PlaySample(note.TailSampleKey, note.TailSampleVolume);

    public void PlayKeySound() => column.PlayKeySound();

    public void ApplyEmptyPoor(double expectedTime, int columnIndex, in BmsFrameContext frame) => playfield.RegisterEmptyPoor(expectedTime, columnIndex);

    public void PlayLandmineSample(BmsLandmine note) => column.DetonateLandmine(note);
}
