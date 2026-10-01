using System;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal sealed class BmsDrawableJudgementOutput : IBmsJudgementOutput
{
    private readonly BmsPlayfield playfield;
    private readonly BmsColumn column;
    private readonly Dictionary<BmsHitObject, Binding> representations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DrawableBmsHitObject, Binding> boundObjects = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BmsHitObject, BmsJudgementResult> results = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BmsLongNote, BmsLongNoteState> presentationStates = new(ReferenceEqualityComparer.Instance);
    private Action<BmsHitObject> resultCheckpoint = null!;

    internal BmsDrawableJudgementOutput(BmsPlayfield playfield, BmsColumn column)
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
        if (drawable.HitObject is BmsLongNote longNote)
            presentationStates[longNote] = drawable.LongNoteJudgement!.State;
        drawable.OnNewResult += binding.ResultHandler;
    }

    internal void Unbind(DrawableBmsHitObject drawable, long generation)
    {
        // Pool return may have cleared HitObject; only the binding owns the old identity.
        if (!boundObjects.TryGetValue(drawable, out var binding) || binding.Generation != generation)
            return;

        boundObjects.Remove(drawable);
        representations.Remove(binding.Note);
        if (binding.Note is BmsLongNote longNote)
            presentationStates.Remove(longNote);
        drawable.OnNewResult -= binding.ResultHandler;
    }

    internal DrawableBmsHitObject GetRepresentation(BmsHitObject note)
        => representations.TryGetValue(note, out var binding) && isCurrent(binding) ? binding.Drawable
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

        internal Binding(BmsHitObject note, DrawableBmsHitObject drawable, long generation, BmsDrawableJudgementOutput output)
        {
            Note = note;
            Drawable = drawable;
            Generation = generation;
            // A captured callback can outlive OnFree when the same drawable already represents another object.
            ResultHandler = (_, _) => { if (output.isCurrent(this)) output.resultCheckpoint(Note); };
        }
    }

    public BmsMainResultState GetMainResult(BmsHitObject note)
    {
        if (!results.TryGetValue(note, out var result))
            throw new InvalidOperationException("The framework has not supplied this note's result.");
        return new BmsMainResultState(result.Type, result.TimeAbsolute, result.TimeOffset, result.GameplayRate, result.SuppressPenalty);
    }

    public void SetResultCheckpoint(Action<BmsHitObject> checkpoint) => resultCheckpoint = checkpoint;

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
        var previous = presentationStates[note];
        var drawable = GetRepresentation(note);
        // CompleteAtPause freezes gameplay after endpoint feedback without retiring the old presentation.
        if (!frame.IsPauseCompletion)
            BmsLongNotePresentation.Apply(drawable, previous, state);
        presentationStates[note] = state;
    }

    public void ApplySyntheticEndpoint(BmsLongNoteEndpointResult endpoint, in BmsFrameContext frame)
    {
        var drawable = GetRepresentation(endpoint.Source);
        if (drawable.Time.Current != frame.SimulationTime || playfield.Time.Current != frame.SimulationTime)
            throw new InvalidOperationException("Supplementary endpoints require the current representation and application time.");

        playfield.ApplySyntheticLongNoteEndpoint(endpoint);
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
}
