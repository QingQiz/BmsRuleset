using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

internal readonly record struct BmsJudgementColumnDefinition(BmsHitObject[] Objects, BmsLayoutVariant Layout, int Index, IBmsJudgementOutput Output);

/// <summary>Maps host input and engine output; it contains no timing or note-lifecycle decisions.</summary>
internal sealed class BmsJudgementEngine
{
    private readonly BmsHitObject[] objects;
    private readonly Dictionary<BmsHitObject, int> ids;
    private readonly Dictionary<int, BmsJudgementView> columns;
    private readonly Dictionary<int, IBmsJudgementOutput> outputs;
    private readonly List<InputEvent> pendingInputs = [];
    private readonly int[] pressed;
    private readonly Chart chart;
    private readonly NoteSnapshot[] snapshots;
    private readonly Dictionary<int, BmsLandmineState> mines = [];
    private readonly Dictionary<int, BmsLongNoteEndpointResult> heads = [];
    private readonly BmsLayoutVariant layout;
    private readonly Func<BmsHitObject[], (ScoreConfiguration Score, HealthConfiguration Health)>? configurationFactory;
    private JudgementSession? session;
    private bool processing;

    internal BmsJudgementEngine(params BmsJudgementColumnDefinition[] definitions)
        : this(definitions, null) { }

    internal BmsJudgementEngine(BmsJudgementColumnDefinition[] definitions,
        Func<BmsHitObject[], (ScoreConfiguration Score, HealthConfiguration Health)>? configurationFactory)
    {
        this.configurationFactory = configurationFactory;
        if (definitions.Length == 0 || definitions.Any(d => d.Index < 0) || definitions.Select(d => d.Index).Distinct().Count() != definitions.Length)
            throw new ArgumentException("Each judgement column needs a unique non-negative index.", nameof(definitions));
        layout = definitions[0].Layout;
        var scratch = layout switch
        {
            BmsLayoutVariant.Bms5K or BmsLayoutVariant.Bms5K2P or BmsLayoutVariant.Bms5KDouble => 5,
            BmsLayoutVariant.Bme7K or BmsLayoutVariant.Bme7K2P or BmsLayoutVariant.Bme7KDouble => 7,
            _ => -1,
        };
        // The host's chart traversal order is observable when simultaneous results clip a gauge.
        objects = definitions.OrderBy(d => scratch < 0 || d.Index > scratch ? d.Index : d.Index == 0 ? scratch : d.Index - 1)
            .SelectMany(d => d.Objects.Where(n => n.Column == d.Index && n is BmsNote or BmsLongNote or BmsLandmine).OrderBy(n => n.StartTime)).ToArray();
        ids = objects.Select((note, id) => (note, id)).ToDictionary(pair => pair.note, pair => pair.id);
        pressed = new int[definitions.Max(d => d.Index) + 1];
        chart = BmsEngineChart.Compile(objects, pressed.Length);
        snapshots = chart.Notes.Select(n => new NoteSnapshot(n.Id, n.Lane, false,
            n.Rule.GetParts(new NoteContext(n, n.Rule.CreateState(n), 0, default)))).ToArray();
        outputs = definitions.ToDictionary(d => d.Index, d => d.Output);
        columns = definitions.ToDictionary(d => d.Index, d => new BmsJudgementView(this, d.Index));
    }

    internal BmsJudgementEngine(BmsHitObject[] notes, BmsLayoutVariant layout, int column, IBmsJudgementOutput output)
        : this(new BmsJudgementColumnDefinition(notes, layout, column, output)) { }

    internal bool HasPendingInputs => pendingInputs.Count > 0;
    internal double CurrentTime => session?.CurrentTime / 1000.0 ?? double.NaN;
    internal BmsJudgementView GetColumn(int index) => columns[index];
    internal NoteSnapshot GetNote(BmsHitObject note) => snapshots[ids[note]];
    internal int GetPressedDirections(int column) => pressed[column];
    internal BmsLandmineState GetLandmineState(BmsLandmine note) => mines.GetValueOrDefault(ids[note]);

    internal void QueueInput(double time, int column, bool down, bool reverseScratch)
        => pendingInputs.Add(new InputEvent(Time.FromMilliseconds(time), column, down, reverseScratch ? 1 : 0));

    internal void ProcessFrame(in BmsFrameContext frame, double elapsed, BmsJudgementAlgorithm? algorithm = BmsJudgementAlgorithm.Combo,
                               Func<double, BmsFrameContext>? contextAtTime = null)
    {
        if (processing) throw new InvalidOperationException("Result observers cannot recursively process a frame.");
        if (frame.IsRewinding || elapsed < 0) { pendingInputs.Clear(); return; }
        session ??= createSession(algorithm);
        var count = pendingInputs.Count;
        processing = true;
        try
        {
            var inputs = pendingInputs.Take(count).ToArray();
            var start = session.TransitionCount;
            var current = session.CurrentTime;
            var split = current.HasValue ? Array.FindIndex(inputs, i => i.Time > current) : 0;
            if (split < 0) split = inputs.Length;
            var dispatched = split > 0 ? session.AppendInputs(inputs.AsSpan(0, split)) : [];
            dispatched = dispatched.AddRange(session.AdvanceTo(Time.FromMilliseconds(frame.SimulationTime), inputs.AsSpan(split)));
            var transitions = session.GetTransitionsSince(start);
            var cursor = 0;
            foreach (var dispatch in dispatched)
            {
                var first = dispatch.FirstTransitionIndex - start;
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
        finally
        {
            pendingInputs.RemoveRange(0, count);
            processing = false;
        }
    }

    internal void CompleteLongNotesAtEnd(in BmsFrameContext frame, Func<double, BmsFrameContext>? contextAtTime = null)
    {
        session ??= createSession(null);
        var start = session.TransitionCount;
        session.EndAt(Time.FromMilliseconds(frame.SimulationTime));
        publish(session.GetTransitionsSince(start), frame, contextAtTime);
    }

    private JudgementSession createSession(BmsJudgementAlgorithm? algorithm)
    {
        var selected = BmsJudgementProfileProvider.GetBehaviour(layout).Algorithm ?? algorithm;
        var selection = selected switch
        {
            BmsJudgementAlgorithm.Duration => BmsSelection.Nearest,
            BmsJudgementAlgorithm.Lowest => BmsSelection.Oldest,
            BmsJudgementAlgorithm.Score => BmsSelection.Score,
            _ => BmsSelection.Combo,
        };
        var configuration = configurationFactory?.Invoke(objects)
            ?? (BmsProfiles.CreateScore(BmsJudgementProfileProvider.EmptyPoorBreaksCombo(layout)), BmsEngineGauge.CreateDefault(objects));
        return new JudgementSession(chart, new SessionConfiguration("bms-ruleset-v1", configuration.Item1, configuration.Item2,
            context => BmsProfiles.SelectCandidates(context, chart, selection)));
    }

    private void publish(ImmutableArray<NoteTransition> transitions, BmsFrameContext frame, Func<double, BmsFrameContext>? contextAtTime)
    {
        foreach (var transition in transitions)
        {
            var note = objects[transition.After.Id];
            var output = outputs[note.Column];
            var step = contextAtTime?.Invoke(transition.Time / 1000.0) ?? frame with { SimulationTime = transition.Time / 1000.0 };
            snapshots[transition.After.Id] = transition.After;
            foreach (var judgement in transition.Judgements)
            {
                step = step with { EngineResult = judgement };
                var result = judgement.Event;
                var type = BmsEngineChart.HitResultFor(result.Result);
                if (result.Part == "empty")
                {
                    if (result.Result == "bad") { output.ApplyNonConsumingBad(note, step); output.PlaySample(note); }
                    else { output.ApplyEmptyPoor(note.StartTime, note.Column, step); output.PlayKeySound(); }
                }
                else if (note is BmsLandmine mine)
                {
                    mines[result.NoteId] = new BmsLandmineState(step.SimulationTime, result.Result == "mine-hit");
                    if (result.Result == "mine-hit") { output.PlayLandmineSample(mine); output.ApplyMainResult(mine, type, step, false); }
                    output.PublishState(mine, mines[result.NoteId], step);
                }
                else if (note is BmsLongNote hold)
                {
                    if (result.Part == "body") { output.ApplyBodyTick(hold, result.Result == "sustain-gain", 0.5, step); continue; }
                    var kind = result.Part == "head" ? BmsLongNoteEndpointKind.Head : BmsLongNoteEndpointKind.Tail;
                    var part = transition.After.Parts.Single(p => p.Part == result.Part);
                    var expected = kind == BmsLongNoteEndpointKind.Head ? hold.StartTime : hold.EndTime;
                    var endpoint = new BmsLongNoteEndpointResult(hold, kind,
                        part.Offset is { } offset ? expected + offset / 1000.0 : step.SimulationTime,
                        step.GameplayRate, BmsEngineChart.HitResultFor(part.Result), step.SimulationTime);
                    if (kind == BmsLongNoteEndpointKind.Head)
                    {
                        heads[result.NoteId] = endpoint;
                        if (hold.EffectiveMode != BmsLongNoteMode.LongNote) output.ApplyMainResult(hold, type, [endpoint], step, result.SuppressPenalty);
                        if (transition.Input is { Pressed: true }) output.PlaySample(hold);
                    }
                    else if (hold.EffectiveMode == BmsLongNoteMode.LongNote)
                    {
                        var head = heads[result.NoteId];
                        output.ApplyMainResult(hold, type, part.Reason is "MissedHead" or "Head" ? [head] : [head, endpoint], step, result.SuppressPenalty);
                    }
                    else output.ApplySyntheticEndpoint(endpoint, step, result.SuppressPenalty);
                    if (kind == BmsLongNoteEndpointKind.Tail && transition.Input != null && part.Reason is "Release" or "Drop")
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
