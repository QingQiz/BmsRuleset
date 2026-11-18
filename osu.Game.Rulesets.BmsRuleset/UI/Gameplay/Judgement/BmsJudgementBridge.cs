using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.Presentation;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using KeySound = RhythmEngine.Core.KeySound;
using NoteSnapshot = RhythmEngine.Core.NoteSnapshot;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Judgement;

internal sealed partial class BmsJudgementBridge : IBmsJudgementOutput
{
    private readonly BmsPlayfield playfield;
    private readonly BmsColumn column;
    private readonly Dictionary<BmsHitObject, Binding> representations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BmsLandmine, DrawableBmsHitObject> landmineRepresentations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DrawableBmsHitObject, Binding> boundObjects = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DrawableBmsHitObject, (long Generation, BmsLongNoteStateObserver Observer)> presentationObservers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DrawableBmsHitObject, (long Generation, BmsLandmineStateObserver Observer)> landmineObservers = new(ReferenceEqualityComparer.Instance);
    private Dictionary<BmsHitObject, BmsHitObjectLifetimeEntry>? replayEntries;

    internal void PrepareReplayResults()
    {
        replayEntries ??= container.Entries.OfType<BmsHitObjectLifetimeEntry>()
            .ToDictionary(entry => (BmsHitObject)entry.HitObject, (IEqualityComparer<BmsHitObject>)ReferenceEqualityComparer.Instance);
    }

    internal void ResetReplayResults()
    {
        // Rebuilt results replace the stack atomically; emitting undo notifications would
        // apply stale deltas to the freshly reset processors.
        judgedEntries(column).Clear();
        PrepareReplayResults();

        foreach (var entry in replayEntries!.Values) entry.ResetReplayResult();
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "judgedEntries")]
    private static extern ref Stack<HitObjectLifetimeEntry> judgedEntries(Playfield playfield);

    internal BmsJudgementBridge(BmsPlayfield playfield, BmsColumn column)
    {
        this.playfield = playfield;
        this.column = column;
    }

    internal void Bind(DrawableBmsHitObject drawable)
    {
        if (boundObjects.TryGetValue(drawable, out var previous) && isCurrent(previous))
            return;

        var binding = new Binding(drawable.HitObject, drawable, drawable.BindingGeneration);
        representations.Add(binding.Note, binding);
        boundObjects.Add(drawable, binding);
    }

    internal void ObserveLongNoteState(DrawableBmsHitObject drawable, BmsLongNoteStateObserver observer)
        => presentationObservers[drawable] = (drawable.BindingGeneration, observer);

    internal void ObserveLandmineState(DrawableBmsHitObject drawable, BmsLandmineStateObserver observer)
        => landmineObservers[drawable] = (drawable.BindingGeneration, observer);

    internal void BindLandmine(DrawableBmsHitObject drawable)
    {
        if (drawable.HitObject is BmsLandmine mine)
            landmineRepresentations[mine] = drawable;
    }

    internal void Unbind(DrawableBmsHitObject drawable, long generation)
    {
        boundObjects.TryGetValue(drawable, out var binding);
        container.RemoveVisualObservation(drawable, generation);
        if (presentationObservers.TryGetValue(drawable, out var observer) && observer.Generation == generation)
            presentationObservers.Remove(drawable);
        if (landmineObservers.TryGetValue(drawable, out var mineObserver) && mineObserver.Generation == generation)
            landmineObservers.Remove(drawable);

        if (drawable.HitObject is BmsLandmine mine && landmineRepresentations.TryGetValue(mine, out var representation)
                                                   && ReferenceEquals(representation, drawable))
            landmineRepresentations.Remove(mine);

        // Pool return may have cleared HitObject; only the binding owns the old identity.
        if (binding == null || binding.Generation != generation)
            return;

        boundObjects.Remove(drawable);
        representations.Remove(binding.Note);
    }

    private bool isCurrent(Binding binding)
        => binding.Drawable.BindingGeneration == binding.Generation && ReferenceEquals(binding.Drawable.HitObject, binding.Note);

    private sealed class Binding
    {
        internal readonly BmsHitObject Note;
        internal readonly DrawableBmsHitObject Drawable;
        internal readonly long Generation;

        internal Binding(BmsHitObject note, DrawableBmsHitObject drawable, long generation)
        {
            Note = note;
            Drawable = drawable;
            Generation = generation;
        }
    }

    internal void OnFrameworkNewResult(DrawableHitObject drawable, JudgementResult result)
    {
        if (drawable is not DrawableBmsHitObject || result.HitObject is not BmsHitObject note || note is BmsLandmine)
            return;

        if (column.Judgements.Profile.ShowHitEffect(result.Type))
            column.TriggerHitExplosion(note is BmsLongNote ? BmsHitLightStyle.Hold : BmsHitLightStyle.Note);
    }

    public void ApplyMainResult(BmsHitObject note, HitResult result, in BmsFrameContext frame, bool suppressPenalty)
        => applyMainResult(note, result, frame, suppressPenalty);

    private void applyMainResult(BmsHitObject note, HitResult result, in BmsFrameContext frame, bool suppressPenalty,
                                 IReadOnlyList<BmsLongNoteEndpointResult>? endpoints = null)
    {
        if (playfield.IsRebuildingReplay)
        {
            var entry = replayEntries![note];
            var rebuilt = entry.GetReplayResult();
            if (endpoints != null)
                ((BmsLongNoteJudgementResult)rebuilt).SetEndpointResults(endpoints);
            rebuilt.Type = result;
            rebuilt.SuppressPenalty = suppressPenalty;
            rebuilt.EngineResult = frame.EngineResult;
            rebuilt.SetTiming(frame.SimulationTime, frame.GameplayRate);
            playfield.ApplyRebuiltResult(rebuilt);
            if (note is not BmsLongNote)
                entry.LifetimeEnd = frame.SimulationTime;
            return;
        }

        var drawable = representations.TryGetValue(note, out var binding) && isCurrent(binding)
            ? binding.Drawable
            : note is BmsLandmine mine && landmineRepresentations.TryGetValue(mine, out var mineDrawable)
                                       && ReferenceEquals(mineDrawable.HitObject, mine)
                ? mineDrawable
                : null;
        var temporary = drawable == null;
        // A delayed update can pass the entire visible lifetime. Borrow its ordinary framework
        // entry so score notification and rewind still use the same path as visible notes.
        drawable ??= (DrawableBmsHitObject)getPooledRepresentation(column, note, null);
        var generation = drawable.BindingGeneration;
        Action<DrawableHitObject, JudgementResult>? temporaryNotification = null;

        if (temporary)
        {
            Bind(drawable);
            // Framework notification must capture the entry before an index observer can return it.
            temporaryNotification = (sender, judgement) =>
            {
                if (drawable.BindingGeneration == generation && ReferenceEquals(drawable.HitObject, note))
                    notifyNewResult(column, sender, judgement);
            };
            drawable.OnNewResult += temporaryNotification;
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

    public void PublishState(BmsLongNote note, NoteSnapshot state, in BmsFrameContext frame)
    {
        if (playfield.IsRebuildingReplay) return;
        if (!representations.TryGetValue(note, out var binding) || !isCurrent(binding)) return;

        var drawable = binding.Drawable;
        if (presentationObservers.TryGetValue(drawable, out var observer) && observer.Generation == drawable.BindingGeneration)
            observer.Observer(state, frame);
    }

    public void ApplySyntheticEndpoint(BmsLongNoteEndpointResult endpoint, in BmsFrameContext frame, bool suppressPenalty)
    {
        playfield.ApplySyntheticLongNoteEndpoint(endpoint, suppressPenalty, frame.EngineResult!);
    }

    public void PublishState(BmsLandmine note, in BmsLandmineState state, in BmsFrameContext frame)
    {
        if (playfield.IsRebuildingReplay) return;
        if (!landmineRepresentations.TryGetValue(note, out var drawable)
            || !ReferenceEquals(drawable.HitObject, note))
            return;

        if (landmineObservers.TryGetValue(drawable, out var observer) && observer.Generation == drawable.BindingGeneration)
            observer.Observer(state, frame);
    }

    public void ApplyBodyTick(in BmsFrameContext frame)
        => playfield.ApplyEngineResult(frame.EngineResult!);

    public void ApplyNonConsumingResult(BmsHitObject note, HitResult result, in BmsFrameContext frame)
        => playfield.RegisterNonConsumingJudgement(result, note.StartTime, note.Column, frame);

    public void PlayKeySound(BmsHitObject note, KeySound sound)
    {
        if (!playfield.IsRebuildingReplay)
            column.KeySound.PlayKeySound(note, sound);
    }

    public void PlayLandmineSample(BmsLandmine note)
    {
        if (!playfield.IsRebuildingReplay) column.DetonateLandmine(note);
    }
}
