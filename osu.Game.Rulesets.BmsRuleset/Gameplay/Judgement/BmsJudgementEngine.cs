using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Patterns;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

internal readonly record struct BmsJudgementColumnDefinition(int Index, IBmsJudgementOutput Output);

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
    private readonly ImmutableArray<NoteSnapshot> initialSnapshots;
    private readonly Dictionary<int, BmsLandmineState> mines = [];
    private readonly Dictionary<int, BmsLongNoteEndpointResult> heads = [];
    private readonly BmsJudgementRules rules;
    private readonly Func<BmsHitObject[], (ScoreConfiguration Score, HealthConfiguration Health)>? configurationFactory;
    private JudgementSession? session;
    private bool processing;

    internal BmsJudgementEngine(BmsHitObject[] notes, BmsLayoutVariant layout, BmsJudgementColumnDefinition[] definitions,
                                Func<BmsHitObject[], (ScoreConfiguration Score, HealthConfiguration Health)>? configurationFactory = null)
    {
        this.configurationFactory = configurationFactory;
        if (definitions.Length == 0 || definitions.Any(d => d.Index < 0) || definitions.Select(d => d.Index).Distinct().Count() != definitions.Length)
            throw new ArgumentException(@"Each judgement column needs a unique non-negative index.", nameof(definitions));

        rules = notes.Length > 0 ? BmsEngineRules.ForBeatmap(notes[0].Beatmap) : BmsEngineRules.ForLayout(layout);
        var scratch = layout switch
        {
            BmsLayoutVariant.Bms5K or BmsLayoutVariant.Bms5K2P or BmsLayoutVariant.Bms5KDouble => 5,
            BmsLayoutVariant.Bme7K or BmsLayoutVariant.Bme7K2P or BmsLayoutVariant.Bme7KDouble => 7,
            _ => -1,
        };
        var notesByColumn = notes.Where(n => n is BmsNote or BmsLongNote or BmsLandmine).ToLookup(n => n.Column);
        // The host's chart traversal order is observable when simultaneous results clip a gauge.
        objects = definitions.OrderBy(d => scratch < 0 || d.Index > scratch ? d.Index : d.Index == 0 ? scratch : d.Index - 1)
            .SelectMany(d => notesByColumn[d.Index].OrderBy(n => n.StartTime)).ToArray();
        ids = objects.Select((note, id) => (note, id)).ToDictionary(pair => pair.note, pair => pair.id);
        pressed = new int[definitions.Max(d => d.Index) + 1];
        chart = BmsEngineChart.Compile(objects, pressed.Length);
        initialSnapshots =
        [
            .. chart.Notes.Select(n => new NoteSnapshot(n.Id, n.Lane, false,
                n.Rule.GetParts(new NoteContext(n, n.Rule.CreateState(n), 0, default))))
        ];
        snapshots = initialSnapshots.ToArray();
        outputs = definitions.ToDictionary(d => d.Index, d => d.Output);
        columns = definitions.ToDictionary(d => d.Index, d => new BmsJudgementView(this, d.Index));
    }

    internal BmsJudgementEngine(BmsHitObject[] notes, BmsLayoutVariant layout, int column, IBmsJudgementOutput output)
        : this(notes, layout, [new BmsJudgementColumnDefinition(column, output)])
    {
    }

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

        if (frame.IsRewinding || elapsed < 0)
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
        var selection = rules.SelectionOverride ?? (algorithm switch
        {
            BmsJudgementAlgorithm.Duration => BmsSelection.Nearest,
            BmsJudgementAlgorithm.Lowest => BmsSelection.Oldest,
            BmsJudgementAlgorithm.Score => BmsSelection.Score,
            _ => BmsSelection.Combo,
        });
        var configuration = configurationFactory?.Invoke(objects)
                            ?? (BmsProfiles.CreateScore(rules.EmptyPoorBreaksCombo), BmsEngineGauge.CreateDefault(objects));
        return new JudgementSession(chart, new SessionConfiguration("bms-ruleset-v1", configuration.Item1, configuration.Item2,
            context => BmsProfiles.SelectCandidates(context, chart, selection)));
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
                var type = BmsEngineChart.HitResultFor(result.Result);

                if (result.Part == NoteParts.EMPTY)
                {
                    if (result.Result == BmsResults.BAD)
                    {
                        output.ApplyNonConsumingBad(note, step);
                        output.PlaySample(note);
                    }
                    else
                    {
                        output.ApplyEmptyPoor(note.StartTime, note.Column, step);
                        output.PlayKeySound();
                    }
                }
                else if (note is BmsLandmine mine)
                {
                    mines[result.NoteId] = new BmsLandmineState(step.SimulationTime, result.Result == BmsResults.MINE_HIT);

                    if (result.Result == BmsResults.MINE_HIT)
                    {
                        output.PlayLandmineSample(mine);
                        output.ApplyMainResult(mine, type, step, false);
                    }

                    output.PublishState(mine, mines[result.NoteId], step);
                }
                else if (note is BmsLongNote hold)
                {
                    if (result.Part == NoteParts.BODY)
                    {
                        output.ApplyBodyTick(step);
                        continue;
                    }

                    var kind = result.Part == NoteParts.HEAD ? BmsLongNoteEndpointKind.Head : BmsLongNoteEndpointKind.Tail;
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
                        output.ApplyMainResult(hold, type, (part.Reason == HoldReasons.MISSED_HEAD || part.Reason == HoldReasons.HEAD) ? [head] : [head, endpoint], step, result.SuppressPenalty);
                    }
                    else output.ApplySyntheticEndpoint(endpoint, step, result.SuppressPenalty);

                    if (kind == BmsLongNoteEndpointKind.Tail && transition.Input != null && (part.Reason == HoldReasons.RELEASE || part.Reason == HoldReasons.DROP))
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
