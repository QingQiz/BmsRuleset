#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using RhythmEngine.Profiles.Common;
using osu.Framework.Graphics;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsJudgementEngineTest
{
    [TestCase(3000.3779999999997, false)]
    [TestCase(3000.3779999999997, true)]
    [TestCase(1000.00075, false)]
    [TestCase(1000.00075, true)]
    public void RoundedResultsDoNotPublishBeyondTheHostFrame(double inputTime, bool historicalContext)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2 };
        var note = new BmsNote { Beatmap = beatmap, Column = 1, StartTime = inputTime };
        double? publishedTime = null;
        var output = new BmsRecordingJudgementOutput { ResultApplied = (_, frame) => publishedTime = frame.SimulationTime };
        var engine = new BmsJudgementEngine([note], beatmap.LayoutVariant, 1, output);
        engine.QueueInput(inputTime, 1, true, false);
        engine.ProcessFrame(new BmsFrameContext(inputTime, 1), 1,
            contextAtTime: historicalContext ? time => new BmsFrameContext(time, 1) : null);

        Assert.That(output.GetMainResult(note).Type, Is.EqualTo(HitResult.Perfect));
        Assert.That(publishedTime, Is.EqualTo(inputTime));
    }

    [Test]
    public void DisabledBadWindowPreservesPassivePoorDeadline()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2, JudgementModifiers = BmsJudgementModifiers.NoBad };
        var note = new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 };
        var output = new BmsRecordingJudgementOutput();
        var engine = new BmsJudgementEngine([note], beatmap.LayoutVariant, 1, output);

        engine.QueueInput(1150, 1, true, false);
        engine.ProcessFrame(new BmsFrameContext(1150, 1), 150);
        Assert.That(output.MainResults, Is.Zero);
        engine.ProcessFrame(new BmsFrameContext(1210, 1), 60);
        Assert.That(output.MainResults, Is.Zero);
        engine.ProcessFrame(new BmsFrameContext(1210.001, 1), 0.001);
        Assert.That(output.GetMainResult(note).Type, Is.EqualTo(HitResult.Meh));
        Assert.That(output.MainResults, Is.EqualTo(1));
    }

    [Test]
    public void ResultMappingDistinguishesUnresolvedAndSilentResultsFromUnknownIds()
    {
        Assert.That(BmsEngineChart.HitResultFor(null), Is.EqualTo(HitResult.None));
        Assert.That(BmsEngineChart.HitResultFor(BmsResults.MINE_AVOIDED), Is.EqualTo(HitResult.None));
        Assert.That(BmsEngineChart.HitResultFor(BmsResults.SUSTAIN_GAIN), Is.EqualTo(HitResult.None));
        Assert.That(BmsEngineChart.HitResultFor(BmsResults.SUSTAIN_LOSS), Is.EqualTo(HitResult.None));
        Assert.That(BmsEngineChart.HitResultFor(BmsResults.MINE_HIT), Is.EqualTo(HitResult.Meh));
        Assert.That(BmsEngineChart.HitResultFor(new ResultId("pgreat")), Is.EqualTo(HitResult.Perfect));
        Assert.Throws<ArgumentOutOfRangeException>(() => BmsEngineChart.HitResultFor(new ResultId("custom-result")));
    }

    private static IEnumerable<Type> coreTypes => typeof(BmsJudgementEngine).Assembly.GetTypes()
        .Where(t => (t.Namespace == typeof(BmsJudgementEngine).Namespace
                     || t.Namespace?.StartsWith(typeof(BmsJudgementEngine).Namespace + ".", StringComparison.Ordinal) == true)
                    && !t.IsDefined(typeof(CompilerGeneratedAttribute)));

    [TestCaseSource(nameof(coreTypes))]
    public void CoreMembersDoNotDependOnDrawablesOrClocks(Type coreType)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var types = coreType.GetFields(members).Select(f => f.FieldType)
            .Concat(coreType.GetMethods(members).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)))
            .Concat(coreType.GetConstructors(members).SelectMany(c => c.GetParameters().Select(p => p.ParameterType)));
        Assert.That(types.SelectMany(expand).Where(t => typeof(Drawable).IsAssignableFrom(t) || typeof(IClock).IsAssignableFrom(t)
                                                                                             || t.Namespace?.StartsWith("osu.Game.Rulesets.BmsRuleset.UI", StringComparison.Ordinal) == true),
            Is.Empty);

        static IEnumerable<Type> expand(Type type)
        {
            yield return type;

            if (type.GetElementType() is { } elementType)
            {
                foreach (var item in expand(elementType))
                    yield return item;
            }

            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var item in expand(argument)) yield return item;
            }
        }
    }

    [Test]
    public void ElapsedBodyTicksPrecedeCurrentMinesAndInputs()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2, LockedLongNoteMode = BmsLongNoteMode.HellChargeNote };
        var hold = new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 1000 };
        var mine = new BmsLandmine { Beatmap = beatmap, Column = 2, StartTime = 1201 };
        var note = new BmsNote { Beatmap = beatmap, Column = 3, StartTime = 1201 };
        BmsHitObject[] objects = [hold, mine, note];
        var events = new List<string>();
        var outputs = Enumerable.Range(1, 3).Select(_ => new BmsRecordingJudgementOutput()).ToArray();

        foreach (var output in outputs)
        {
            output.ResultApplied = (source, _) => events.Add($"result:{source.Column}");
            output.BodyTick = frame => events.Add($"body:{frame.EngineResult!.Event.Lane}");
        }

        var engine = new BmsJudgementEngine(objects, beatmap.LayoutVariant, Enumerable.Range(1, 3).Select(i =>
            new BmsJudgementColumnDefinition(i, outputs[i - 1])).ToArray());

        engine.QueueInput(1000, 1, true, false);
        engine.ProcessFrame(new BmsFrameContext(1000, 1), 1);
        events.Clear();
        engine.QueueInput(1201, 2, true, false);
        engine.QueueInput(1201, 3, true, false);
        engine.ProcessFrame(new BmsFrameContext(1201, 1), 201);
        Assert.That(events, Is.EqualTo(new[] { "body:1", "result:3", "result:2" }));
        Assert.That(outputs[0].BodyTicks.Single().Holding, Is.True);
        Assert.That(engine.GetColumn(2).GetLandmineState(mine).Detonated, Is.True);
    }

    [TestCase(-0.001, true, false, false)]
    [TestCase(0, true, true, true)]
    [TestCase(99.999, true, true, false)]
    [TestCase(100, true, true, false)]
    [TestCase(0, false, true, false)]
    public void LandmineUsesChartTimeAndIsNeverAPressCandidate(double offset, bool held, bool handled, bool detonated)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2 };
        var mine = new BmsLandmine { Beatmap = beatmap, Column = 1, StartTime = 1000 };
        var output = new BmsRecordingJudgementOutput();
        var engine = new BmsJudgementEngine([mine], beatmap.LayoutVariant, 1, output);
        var column = engine.GetColumn(1);

        if (held)
            engine.QueueInput(1000 + offset, 1, true, false);
        engine.ProcessFrame(new BmsFrameContext(1000 + offset, 1), 1);
        Assert.That(column.GetLandmineState(mine).HandledTime.HasValue, Is.EqualTo(handled));
        Assert.That(column.GetLandmineState(mine).Detonated, Is.EqualTo(detonated));
        Assert.That(output.MainResults, Is.EqualTo(detonated ? 1 : 0));
        Assert.That(column.GetNoteCandidates(), Is.Empty);
        engine.ProcessFrame(new BmsFrameContext(1000 + offset, 1), 1);
        Assert.That(output.MainResults, Is.EqualTo(detonated ? 1 : 0));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ResetRestoresLandmineAndInputState(bool held)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K };
        var mine = new BmsLandmine { Beatmap = beatmap, Column = 1, StartTime = 1000 };
        var output = new BmsRecordingJudgementOutput();
        var engine = new BmsJudgementEngine([mine], beatmap.LayoutVariant, 1, output);
        var column = engine.GetColumn(1);

        if (held) engine.QueueInput(1000, 1, true, false);
        engine.ProcessFrame(new BmsFrameContext(1007, 1), 7);

        Assert.That(column.GetLandmineState(mine).HandledTime, Is.EqualTo(1000));
        output.Reset();
        engine.Reset();
        Assert.That(column.GetLandmineState(mine).HandledTime, Is.Null);
        Assert.That(column.PressedDirections, Is.Zero);
        Assert.That(column.GetNote(mine).Completed, Is.False);

        engine.QueueInput(1000, 1, true, false);
        engine.ProcessFrame(new BmsFrameContext(1007, 1), 7);
        Assert.That(column.GetLandmineState(mine).Detonated, Is.True);
        Assert.That(output.MainResults, Is.EqualTo(1));
    }

    [Test]
    public void CallbackInputFormsAnotherBatchAtTheSameTimestamp()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2 };
        var first = new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 };
        var second = new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 };
        var output = new BmsRecordingJudgementOutput();
        var engine = new BmsJudgementEngine([first, second], beatmap.LayoutVariant, 1, output);

        output.ResultApplied = (note, _) =>
        {
            if (ReferenceEquals(note, first))
            {
                engine.QueueInput(1000, 1, false, false);
                engine.QueueInput(1000, 1, true, false);
            }
        };
        engine.QueueInput(1000, 1, true, false);
        engine.ProcessFrame(new BmsFrameContext(1000, 1), 1);
        Assert.That(output.MainResults, Is.EqualTo(1));
        Assert.That(engine.HasPendingInputs, Is.True);
        engine.ProcessFrame(new BmsFrameContext(1000, 1), 1);
        Assert.That(output.MainResults, Is.EqualTo(2));
        Assert.That(engine.HasPendingInputs, Is.False);
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void EndSettlesHoldsThroughTheSameEngine(BmsLongNoteMode mode)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2, LockedLongNoteMode = mode };
        var note = new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 1000 };
        var output = new BmsRecordingJudgementOutput();
        var engine = new BmsJudgementEngine([note], beatmap.LayoutVariant, 1, output);
        var column = engine.GetColumn(1);

        engine.QueueInput(1000, 1, true, false);
        engine.ProcessFrame(new BmsFrameContext(1000, 1), 1);
        engine.CompleteLongNotesAtEnd(new BmsFrameContext(1200, 1.5));
        Assert.That(column.GetLongNoteState(note).TailJudged, Is.True);
        Assert.That(column.GetLongNoteState(note).LongNoteStarted, Is.False);
        Assert.That(column.GetHoldingNotes(), Is.Empty);
        Assert.That(column.GetActiveBodies(), Is.Empty);
        engine.ProcessFrame(new BmsFrameContext(1500, 1.5), 300);
        Assert.That(output.BodyTicks, Is.Empty);
        Assert.That(mode == BmsLongNoteMode.LongNote ? output.GetEndpoints(note).Last().GameplayRate : output.SyntheticEndpoints.Last().GameplayRate, Is.EqualTo(1.5));
    }

    [Test]
    public void BatchFinalReleasePreventsMineAndSameTimestampHeadCannotAccumulateBody()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2, LockedLongNoteMode = BmsLongNoteMode.HellChargeNote };
        var note = new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 1000 };
        var mine = new BmsLandmine { Beatmap = beatmap, Column = 1, StartTime = 1000 };
        var output = new BmsRecordingJudgementOutput();
        var engine = new BmsJudgementEngine([note, mine], beatmap.LayoutVariant, 1, output);
        var column = engine.GetColumn(1);

        engine.QueueInput(1000, 1, true, false);
        engine.QueueInput(1000, 1, false, false);
        engine.ProcessFrame(new BmsFrameContext(1000, 1), 500);
        engine.ProcessFrame(new BmsFrameContext(1000, 1), 500);
        Assert.That(column.GetLandmineState(mine).Detonated, Is.False);
        Assert.That(output.BodyTicks, Is.Empty);
        Assert.That(column.GetLongNoteState(note).TailJudged, Is.True);
        Assert.That(column.PressedDirections, Is.Zero);
    }

    [Test]
    public void UnmatchedAudioKeepsItsPlaceBetweenIdenticalInputEdges()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Popn9K, Rank = 2 };
        var note = new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000, SampleKey = 7 };
        var feedback = new List<string>();
        var output = new BmsRecordingJudgementOutput { Feedback = feedback.Add };
        var engine = new BmsJudgementEngine([note], beatmap.LayoutVariant, 1, output);
        engine.QueueInput(0, 1, true, false);
        engine.QueueInput(0, 1, false, false);
        engine.QueueInput(1000, 1, true, false);
        engine.QueueInput(1000, 1, false, false);
        engine.QueueInput(1000, 1, true, false);
        engine.QueueInput(1000, 1, true, false);
        engine.ProcessFrame(new BmsFrameContext(2000, 1), 2000);
        Assert.That(feedback, Is.EqualTo(new[] { "keysound", "sample:7:100", "keysound" }));
        Assert.That(output.MainResults, Is.EqualTo(1));
    }

    [Test]
    public void PresentationReadsAndReplacementCannotChangeJudgement()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2, LockedLongNoteMode = BmsLongNoteMode.HellChargeNote };
        var note = new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 1000 };
        var output = new BmsRecordingJudgementOutput();
        var engine = new BmsJudgementEngine([note], beatmap.LayoutVariant, 1, output);
        var view = engine.GetColumn(1);
        var observed = new List<(bool Head, bool Tail, bool Held)>();
        output.StatePublished = (_, _, _) =>
        {
            var snapshot = view.GetNote(note);
            observed.Add((snapshot.Parts[0].Resolved, snapshot.Parts[2].Resolved, snapshot.Held));

            for (var i = 0; i < 10; i++)
            {
                var detached = snapshot with { Completed = true, Parts = [] };
                Assert.That(detached.Completed, Is.True);
                Assert.That(view.GetNote(note), Is.SameAs(snapshot));
            }

            output.StatePublished = null;
        };
        engine.QueueInput(1000, 1, true, false);
        engine.QueueInput(2000, 1, false, false);
        engine.ProcessFrame(new BmsFrameContext(3000, 1), 3000);
        Assert.That(observed, Is.EqualTo(new[] { (true, false, true) }));
        Assert.That(view.GetNote(note).Completed, Is.True);
        Assert.That(output.MainResults, Is.EqualTo(1));
        Assert.That(output.SyntheticEndpoints, Has.Count.EqualTo(1));
        Assert.That(engine.Session().Score.Score, Is.EqualTo(4));
    }

    [Test]
    public void CoincidentHoldEndpointsRetainBothDecisions(
        [Values(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)]
        BmsLongNoteMode mode,
        [Values(0, .0001)] double duration)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2, LockedLongNoteMode = mode };
        var note = new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = duration };
        var output = new BmsRecordingJudgementOutput();
        var engine = new BmsJudgementEngine([note], beatmap.LayoutVariant, 1, output);
        engine.QueueInput(1000, 1, true, false);
        engine.QueueInput(1000 + duration, 1, false, false);
        engine.ProcessFrame(new BmsFrameContext(2000, 1), 2000);
        Assert.That(engine.GetColumn(1).GetNote(note).Parts.Where(p => p.Part == NoteParts.HEAD || p.Part == NoteParts.TAIL)
            .All(p => p.Resolved), Is.True);
        Assert.That(engine.Session().Score.Score, Is.EqualTo(mode == BmsLongNoteMode.LongNote ? 2 : 4));
        Assert.That(engine.GetColumn(1).GetNote(note).Completed, Is.True);

        if (mode == BmsLongNoteMode.LongNote)
        {
            Assert.That(output.GetEndpoints(note).Select(endpoint => endpoint.Kind),
                Is.EqualTo(new[] { BmsLongNoteEndpointKind.Head, BmsLongNoteEndpointKind.Tail }));
        }
    }
}
