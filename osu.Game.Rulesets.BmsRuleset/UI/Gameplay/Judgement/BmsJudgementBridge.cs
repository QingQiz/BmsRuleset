using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Rules;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Judgement;

internal sealed partial class BmsJudgementBridge : IBmsJudgementOutput
{
    private readonly BmsPlayfield playfield;
    private readonly BmsColumn column;
    private readonly Dictionary<BmsHitObject, Binding> representations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DrawableBmsHitObject, Binding> boundObjects = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BmsHitObject, BmsJudgementResult> results = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DrawableBmsHitObject, (long Generation, BmsLongNoteStateObserver Observer)> presentationObservers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DrawableBmsHitObject, (long Generation, BmsLandmineStateObserver Observer)> landmineObservers = new(ReferenceEqualityComparer.Instance);
    private Action<BmsHitObject> resultCheckpoint = null!;
    private Action<BmsHitObject, double> resultHistoryCheckpoint = null!;

    internal BmsJudgementBridge(BmsPlayfield playfield, BmsColumn column)
    {
        this.playfield = playfield;
        this.column = column;
    }

    internal void Bind(DrawableBmsHitObject drawable)
    {
        if (boundObjects.TryGetValue(drawable, out var previous) && isCurrent(previous))
            return;

        var binding = new Binding(drawable.HitObject, drawable, drawable.BindingGeneration, this);
        representations.Add(binding.Note, binding);
        boundObjects.Add(drawable, binding);
        results[drawable.HitObject] = (BmsJudgementResult)drawable.Result;
        drawable.OnNewResult += binding.ResultHandler;
    }

    internal void ObserveLongNoteState(DrawableBmsHitObject drawable, BmsLongNoteStateObserver observer)
        => presentationObservers[drawable] = (drawable.BindingGeneration, observer);

    internal void ObserveLandmineState(DrawableBmsHitObject drawable, BmsLandmineStateObserver observer)
        => landmineObservers[drawable] = (drawable.BindingGeneration, observer);

    internal void Unbind(DrawableBmsHitObject drawable, long generation)
    {
        boundObjects.TryGetValue(drawable, out var binding);
        container.RemoveVisualObservation(drawable, generation);
        if (presentationObservers.TryGetValue(drawable, out var observer) && observer.Generation == generation)
            presentationObservers.Remove(drawable);
        if (landmineObservers.TryGetValue(drawable, out var mineObserver) && mineObserver.Generation == generation)
            landmineObservers.Remove(drawable);

        // Pool return may have cleared HitObject; only the binding owns the old identity.
        if (binding == null || binding.Generation != generation)
            return;

        binding.Bound = false;
        boundObjects.Remove(drawable);
        representations.Remove(binding.Note);
        drawable.OnNewResult -= binding.ResultHandler;
    }

    internal DrawableBmsHitObject GetRepresentation(BmsHitObject note)
        => representations.TryGetValue(note, out var binding) && isCurrent(binding)
            ? binding.Drawable
            : throw new InvalidOperationException("A main result requires the current representation.");

    private bool isCurrent(Binding binding)
        => binding.Bound && binding.Drawable.BindingGeneration == binding.Generation && ReferenceEquals(binding.Drawable.HitObject, binding.Note);

    private sealed class Binding
    {
        internal readonly BmsHitObject Note;
        internal readonly DrawableBmsHitObject Drawable;
        internal readonly long Generation;
        internal readonly Action<DrawableHitObject, JudgementResult> ResultHandler;

        // Captured callbacks retain their own binding identity even if the same drawable
        // is rebound without advancing its local generation.
        internal bool Bound = true;

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
            return new BmsMainResultState(HitResult.None, note.GetEndTime(), 0, null, false);

        return new BmsMainResultState(result.Type, result.EventTime, result.EventTime - note.GetEndTime(), result.GameplayRate, result.SuppressPenalty);
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
        resultHistoryCheckpoint(note, ((BmsJudgementResult)result).EventTime);
        if (BmsColumn.ShouldTriggerHitExplosion(result.Type))
            column.TriggerHitExplosion(note is BmsLongNote ? BmsHitLightStyle.Hold : BmsHitLightStyle.Note);
    }

    public void ApplyMainResult(BmsHitObject note, HitResult result, in BmsFrameContext frame, bool suppressPenalty)
        => applyMainResult(note, result, frame, suppressPenalty);

    private void applyMainResult(BmsHitObject note, HitResult result, in BmsFrameContext frame, bool suppressPenalty,
                                 IReadOnlyList<BmsLongNoteEndpointResult>? endpoints = null)
    {
        var temporary = !representations.TryGetValue(note, out var binding) || !isCurrent(binding);
        // A delayed update can pass the entire visible lifetime. Borrow its ordinary framework
        // entry so score notification and rewind still use the same path as visible notes.
        var drawable = temporary
            ? (DrawableBmsHitObject)getPooledRepresentation(column, note, null)
            : binding!.Drawable;
        var generation = drawable.BindingGeneration;
        Action<DrawableHitObject, JudgementResult>? temporaryNotification = null;
        if (temporary)
        {
            Bind(drawable);
            var resultHandler = boundObjects[drawable].ResultHandler;
            drawable.OnNewResult -= resultHandler;
            // Framework notification must capture the entry before an index observer can return it.
            temporaryNotification = (sender, judgement) =>
            {
                if (drawable.BindingGeneration == generation && ReferenceEquals(drawable.HitObject, note))
                    notifyNewResult(column, sender, judgement);
            };
            drawable.OnNewResult += temporaryNotification;
            drawable.OnNewResult += resultHandler;
        }

        try
        {
            if (endpoints != null)
                ((BmsLongNoteJudgementResult)drawable.Result).SetEndpointResults(endpoints);
            drawable.ApplyBmsResult(result, suppressPenalty, frame);
        }
        finally
        {
            if (temporaryNotification != null)
                drawable.OnNewResult -= temporaryNotification;
            if (temporary && drawable.IsInUse && drawable.BindingGeneration == generation && ReferenceEquals(drawable.HitObject, note))
                drawable.Return();
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "onNewResult")]
    private static extern void notifyNewResult(Playfield playfield, DrawableHitObject drawable, JudgementResult result);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "osu.Game.Rulesets.UI.IPooledHitObjectProvider.GetPooledDrawableRepresentation")]
    private static extern DrawableHitObject getPooledRepresentation(Playfield playfield, HitObject note, DrawableHitObject? parent);

    public void ApplyMainResult(BmsLongNote note, HitResult result, IReadOnlyList<BmsLongNoteEndpointResult> endpoints, in BmsFrameContext frame, bool suppressPenalty)
        => applyMainResult(note, result, frame, suppressPenalty, endpoints);

    public void PublishState(BmsLongNote note, in BmsLongNoteState state, in BmsFrameContext frame)
    {
        if (!representations.TryGetValue(note, out var binding) || !isCurrent(binding)) return;

        var drawable = binding.Drawable;
        if (presentationObservers.TryGetValue(drawable, out var observer) && observer.Generation == drawable.BindingGeneration)
            observer.Observer(state, frame);
    }

    public void ApplySyntheticEndpoint(BmsLongNoteEndpointResult endpoint, in BmsFrameContext frame, bool suppressPenalty)
    {
        playfield.ApplySyntheticLongNoteEndpoint(endpoint, suppressPenalty);
    }

    public void PublishState(BmsLandmine note, in BmsLandmineState state, in BmsFrameContext frame)
    {
        if (!representations.TryGetValue(note, out var binding) || !isCurrent(binding)) return;

        var drawable = binding.Drawable;
        if (landmineObservers.TryGetValue(drawable, out var observer) && observer.Generation == drawable.BindingGeneration)
            observer.Observer(state, frame);
    }

    public void ApplyBodyTick(BmsLongNote note, bool holding, double scale, in BmsFrameContext frame)
        => playfield.ApplyHellChargeTick(holding, scale, frame.SimulationTime);

    public void ApplyNonConsumingBad(BmsHitObject note, in BmsFrameContext frame)
    {
        playfield.RegisterNonConsumingBad(note.StartTime, note.Column, frame.SimulationTime);
    }

    public void PlaySample(BmsHitObject note) => column.PlaySample(note.SampleKey, note.SampleVolume);

    public void PlayTailSample(BmsLongNote note) => column.PlaySample(note.TailSampleKey, note.TailSampleVolume);

    public void PlayKeySound() => column.PlayKeySound();

    public void ApplyEmptyPoor(double expectedTime, int columnIndex, in BmsFrameContext frame) => playfield.RegisterEmptyPoor(expectedTime, columnIndex, frame.SimulationTime);

    public void PlayLandmineSample(BmsLandmine note) => column.DetonateLandmine(note);
}
