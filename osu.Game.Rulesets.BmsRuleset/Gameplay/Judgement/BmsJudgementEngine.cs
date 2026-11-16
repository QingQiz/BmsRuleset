using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

internal readonly record struct BmsJudgementColumnDefinition(int Index, IBmsJudgementOutput Output);

/// <summary>Maps host input and engine output; it contains no timing or note-lifecycle decisions.</summary>
internal sealed class BmsJudgementEngine
{
    private readonly BmsHitObject[] objects;
    private readonly IReadOnlyDictionary<BmsHitObject, int> ids;
    private readonly Dictionary<int, BmsJudgementView> columns;
    private readonly Dictionary<int, IBmsJudgementOutput> outputs;
    private readonly List<InputEvent> pendingInputs = [];
    private readonly int[] pressed;
    private readonly Chart chart;
    private readonly NoteSnapshot[] snapshots;
    private readonly ImmutableArray<NoteSnapshot> initialSnapshots;
    private readonly Dictionary<int, BmsLandmineState> mines = [];
    private readonly Dictionary<int, BmsLongNoteEndpointResult> heads = [];
    private readonly BmsEngineChart compiled;
    private readonly Func<BmsEngineChart, HealthConfiguration>? healthFactory;
    private JudgementSession? session;
    private readonly Action<JudgementSession>? stateChanged;
    private bool processing;

    internal BmsJudgementEngine(BmsEngineChart compiled, BmsJudgementColumnDefinition[] definitions,
                                Func<BmsEngineChart, HealthConfiguration>? healthFactory = null, Action<JudgementSession>? stateChanged = null)
    {
        this.compiled = compiled;
        this.healthFactory = healthFactory;
        this.stateChanged = stateChanged;
        if (definitions.Length == 0 || definitions.Any(d => d.Index < 0) || definitions.Select(d => d.Index).Distinct().Count() != definitions.Length)
            throw new ArgumentException(@"Each judgement column needs a unique non-negative index.", nameof(definitions));

        objects = compiled.Objects;
        ids = compiled.Ids;
        chart = compiled.Chart;
        pressed = new int[chart.LaneCount];
        initialSnapshots =
        [
            .. chart.Notes.Select(n => new NoteSnapshot(n.Id, n.Lane, false,
                n.Rule.GetParts(new NoteContext(n, n.Rule.CreateState(n), 0, default))))
        ];
        snapshots = initialSnapshots.ToArray();
        outputs = definitions.ToDictionary(d => d.Index, d => d.Output);
        columns = definitions.ToDictionary(d => d.Index, d => new BmsJudgementView(this, d.Index));
    }

    internal GameplayProfile Profile => compiled.Profile;

    internal NoteTiming GetTiming(BmsHitObject note) => compiled.TimingFor(note);

    internal double EarliestInputTime => objects.Select(note => note.StartTime + GetTiming(note).EarliestInputOffset / 1000.0).DefaultIfEmpty(0).Min();

    internal bool ScoresHead(BmsHitObject note) => chart.ScorePlan.OpportunitiesByPart.ContainsKey((ids[note], NoteParts.HEAD));

    internal bool HasPendingInputs => pendingInputs.Count > 0;

    internal double CurrentTime => session?.CurrentTime / 1000.0 ?? double.NaN;

    internal BmsJudgementView GetColumn(int index) => columns[index];

    internal NoteSnapshot GetNote(BmsHitObject note) => snapshots[ids[note]];

    internal int GetPressedDirections(int column) => pressed[column];

    internal BmsLandmineState GetLandmineState(BmsLandmine note) => mines.GetValueOrDefault(ids[note]);

    internal void Reset()
    {
        session = null;
        pendingInputs.Clear();
        Array.Clear(pressed);
        initialSnapshots.CopyTo(snapshots);
        mines.Clear();
        heads.Clear();
    }

    internal void QueueInput(double time, int column, bool down, bool reverseScratch)
        => pendingInputs.Add(new InputEvent(Time.FromMilliseconds(time), column, down, reverseScratch ? 1 : 0));

    internal void ProcessFrame(in BmsFrameContext frame, double elapsed, BmsJudgementAlgorithm? algorithm = BmsJudgementAlgorithm.Combo,
                               Func<double, BmsFrameContext>? contextAtTime = null)
    {
        if (processing) throw new InvalidOperationException("Result observers cannot recursively process a frame.");

        // A live seek rebuilds at the master clock's target before the frame-stable clock catches up.
        if (frame.IsRewinding || elapsed < 0 || Time.FromMilliseconds(frame.SimulationTime) < session?.CurrentTime)
        {
            pendingInputs.Clear();
            return;
        }

        session ??= createSession(algorithm);
        var count = pendingInputs.Count;
        processing = true;

        try
        {
            var inputs = pendingInputs.Take(count).ToArray();
            var current = session.CurrentTime;
            var split = current.HasValue ? Array.FindIndex(inputs, i => i.Time > current) : 0;
            if (split < 0) split = inputs.Length;
            var appended = split > 0 ? session.AppendInputs(inputs.AsSpan(0, split)) : null;
            var advanced = session.AdvanceTo(Time.FromMilliseconds(frame.SimulationTime), inputs.AsSpan(split));
            if (appended != null)
                publishBatch(appended, frame, contextAtTime);
            publishBatch(advanced, frame, contextAtTime);
            stateChanged?.Invoke(session);
        }
        finally
        {
            pendingInputs.RemoveRange(0, count);
            processing = false;
        }
    }

    internal void CompleteLongNotesAtEnd(in BmsFrameContext frame, Func<double, BmsFrameContext>? contextAtTime = null)
    {
        session ??= createSession(null);
        publish(session.EndAt(Time.FromMilliseconds(frame.SimulationTime)).Transitions.AsSpan(), frame, contextAtTime);
        stateChanged?.Invoke(session);
    }

    private void publishBatch(JudgementBatch batch, BmsFrameContext frame, Func<double, BmsFrameContext>? contextAtTime)
    {
        var transitions = batch.Transitions.AsSpan();
        var cursor = 0;

        foreach (var dispatch in batch.Inputs)
        {
            var first = dispatch.FirstTransitionIndex;
            publish(transitions.Slice(cursor, first - cursor), frame, contextAtTime);
            var input = dispatch.Input;
            if (input.Pressed) pressed[input.Lane] |= 1 << input.Control;
            else pressed[input.Lane] &= ~(1 << input.Control);
            publish(transitions.Slice(first, dispatch.TransitionCount), frame, contextAtTime);

            if (input.Pressed && dispatch.TransitionCount == 0)
            {
                contextAtTime?.Invoke(input.Time / 1000.0);
                outputs[input.Lane].PlayKeySound();
            }

            cursor = first + dispatch.TransitionCount;
        }

        publish(transitions[cursor..], frame, contextAtTime);
    }

    private JudgementSession createSession(BmsJudgementAlgorithm? algorithm)
    {
        var health = healthFactory?.Invoke(compiled) ?? compiled.Configuration.Health;
        return new JudgementSession(chart, compiled.Profile.ConfigureSession(compiled, algorithm, health));
    }

    private void publish(ReadOnlySpan<NoteTransition> transitions, BmsFrameContext frame, Func<double, BmsFrameContext>? contextAtTime)
    {
        foreach (var transition in transitions)
        {
            var note = objects[transition.After.Id];
            var output = outputs[note.Column];
            // Microsecond rounding must not make the framework revert a result as being ahead of its clock.
            var eventTime = Math.Min(transition.Time / 1000.0, frame.SimulationTime);
            var step = contextAtTime?.Invoke(eventTime) ?? frame with { SimulationTime = eventTime };
            snapshots[transition.After.Id] = transition.After;

            foreach (var judgement in transition.Judgements)
            {
                step = step with { EngineResult = judgement };
                var result = judgement.Event;
                var type = compiled.Profile.HitResultFor(result.Result);

                var eventKind = compiled.Profile.EventKind(result);
                if (eventKind is GameplayEventKind.EmptyInput or GameplayEventKind.NonConsumingHit)
                {
                    output.ApplyNonConsumingResult(note, type, step);
                    if (eventKind == GameplayEventKind.NonConsumingHit)
                        output.PlaySample(note);
                    else
                        output.PlayKeySound();
                }
                else if (note is BmsLandmine mine)
                {
                    mines[result.NoteId] = new BmsLandmineState(step.SimulationTime, eventKind == GameplayEventKind.MineHit);

                    if (eventKind == GameplayEventKind.MineHit)
                    {
                        output.PlayLandmineSample(mine);
                        output.ApplyMainResult(mine, type, step, false);
                    }

                    output.PublishState(mine, mines[result.NoteId], step);
                }
                else if (eventKind == GameplayEventKind.Auxiliary)
                    output.ApplyBodyTick(step);
                else if (note is BmsLongNote hold)
                {

                    var kind = result.Part == NoteParts.HEAD ? BmsLongNoteEndpointKind.Head : BmsLongNoteEndpointKind.Tail;
                    var part = transition.After.Parts.Single(p => p.Part == result.Part);
                    var expected = kind == BmsLongNoteEndpointKind.Head ? hold.StartTime : hold.EndTime;
                    var endpoint = new BmsLongNoteEndpointResult(hold, kind,
                        part.Offset is { } offset ? expected + offset / 1000.0 : step.SimulationTime,
                        step.GameplayRate, type, step.SimulationTime);

                    var scoresHead = chart.ScorePlan.OpportunitiesByPart.ContainsKey((result.NoteId, NoteParts.HEAD));
                    if (kind == BmsLongNoteEndpointKind.Head)
                    {
                        if (scoresHead) output.ApplyMainResult(hold, type, [endpoint], step, result.SuppressPenalty);
                        else heads[result.NoteId] = endpoint;
                        if (transition.Input is { Pressed: true }) output.PlaySample(hold);
                    }
                    else if (!scoresHead)
                    {
                        var head = heads[result.NoteId];
                        output.ApplyMainResult(hold, type, compiled.Profile.IncludeHeadOnly(part) ? [head] : [head, endpoint], step, result.SuppressPenalty);
                    }
                    else output.ApplySyntheticEndpoint(endpoint, step, result.SuppressPenalty);

                    if (kind == BmsLongNoteEndpointKind.Tail && compiled.Profile.PlayTailSample(part, transition.Input))
                        output.PlayTailSample(hold);
                }
                else
                {
                    output.ApplyMainResult(note, type, step, result.SuppressPenalty);
                    if (transition.Input is { Pressed: true }) output.PlaySample(note);
                }
            }

            if (note is BmsLongNote longNote) output.PublishState(longNote, transition.After, step);
        }
    }
}
