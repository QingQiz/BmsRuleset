using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsJudgementEngineTest
{
    private static IEnumerable<Type> CoreTypes => typeof(BmsJudgementEngine).Assembly.GetTypes()
        .Where(t => t.Namespace == typeof(BmsJudgementEngine).Namespace && !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)));

    [TestCaseSource(nameof(CoreTypes))]
    public void CoreMembersDoNotDependOnDrawablesOrClocks(Type coreType)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var types = coreType.GetFields(members).Select(f => f.FieldType)
            .Concat(coreType.GetMethods(members).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)))
            .Concat(coreType.GetConstructors(members).SelectMany(c => c.GetParameters().Select(p => p.ParameterType)));
        Assert.That(types.SelectMany(expand).Where(t => typeof(Drawable).IsAssignableFrom(t) || typeof(IClock).IsAssignableFrom(t)
            || t.Namespace?.StartsWith("osu.Game.Rulesets.BmsRuleset.UI", StringComparison.Ordinal) == true), Is.Empty);

        static IEnumerable<Type> expand(Type type)
        {
            yield return type;
            if (type.HasElementType)
            {
                foreach (var item in expand(type.GetElementType()!)) yield return item;
            }
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var item in expand(argument)) yield return item;
            }
        }
    }

    [Test]
    public void EveryColumnMinePrecedesEveryBodyAndEveryInput()
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
            output.BeforeResultCheckpoint = (source, _) => events.Add($"result:{source.Column}");
            output.BodyTick = (source, _, _, _) => events.Add($"body:{source.Column}");
        }
        var engine = new BmsJudgementEngine(Enumerable.Range(1, 3).Select(i =>
            new BmsJudgementColumnDefinition(objects, beatmap.LayoutVariant, i, false, outputs[i - 1])).ToArray());
        engine.GetColumn(1).ActivateNote(hold);
        engine.GetColumn(2).ActivateLandmine(mine);
        engine.GetColumn(3).ActivateNote(note);
        engine.QueueInput(1000, 1, true, false);
        engine.ProcessFrame(new BmsFrameContext(1000, 1), 1);
        events.Clear();
        engine.QueueInput(1201, 2, true, false);
        engine.QueueInput(1201, 3, true, false);
        engine.ProcessFrame(new BmsFrameContext(1201, 1), 201);
        Assert.That(events, Is.EqualTo(new[] { "result:2", "body:1", "result:3" }));
        Assert.That(outputs[0].BodyTicks.Single().Holding, Is.True);
        Assert.That(engine.GetColumn(2).GetLandmineState(mine).Detonated, Is.True);
        Assert.That(engine.GetColumn(1).Rules, Is.SameAs(engine.GetColumn(3).Rules));
    }

    [TestCase(-0.001, true, false, false)]
    [TestCase(0, true, true, true)]
    [TestCase(99.999, true, true, true)]
    [TestCase(100, true, true, false)]
    [TestCase(0, false, true, false)]
    public void LandmineUsesPassingBoundaryAndIsNeverAPressCandidate(double offset, bool held, bool handled, bool detonated)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2 };
        var mine = new BmsLandmine { Beatmap = beatmap, Column = 1, StartTime = 1000 };
        var output = new BmsRecordingJudgementOutput();
        var engine = new BmsJudgementEngine([mine], beatmap.LayoutVariant, 1, false, output);
        var column = engine.GetColumn(1);
        column.ActivateLandmine(mine);
        if (held)
            engine.QueueInput(1000 + offset, 1, true, false);
        engine.ProcessFrame(new BmsFrameContext(1000 + offset, 1), 1);
        Assert.That(column.GetLandmineState(mine).HandledTime.HasValue, Is.EqualTo(handled));
        Assert.That(column.GetLandmineState(mine).Detonated, Is.EqualTo(detonated));
        Assert.That(output.MainResults, Is.EqualTo(detonated ? 1 : 0));
        Assert.That(column.NoteRegistrations, Is.Empty);
        Assert.That(column.GetNoteCandidates(), Is.Empty);
        engine.ProcessFrame(new BmsFrameContext(1000 + offset, 1), 1);
        Assert.That(output.MainResults, Is.EqualTo(detonated ? 1 : 0));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LandmineHistorySurvivesDeactivationAndRewindsAtProcessingTime(bool held)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K };
        var mine = new BmsLandmine { Beatmap = beatmap, Column = 1, StartTime = 1000 };
        var output = new BmsRecordingJudgementOutput();
        var engine = new BmsJudgementEngine([mine], beatmap.LayoutVariant, 1, false, output);
        var column = engine.GetColumn(1);
        column.ActivateLandmine(mine);
        column.UpdateLandmines(new BmsFrameContext(1007, 1), held);
        column.RemoveLandmine(mine);
        column.RewindLandmines(1007);
        Assert.That(column.GetLandmineState(mine).HandledTime, Is.EqualTo(1007));
        output.RevertAfter(1006);
        column.RewindLandmines(1006);
        Assert.That(column.GetLandmineState(mine).HandledTime, Is.Null);
        column.ActivateLandmine(mine);
        column.UpdateLandmines(new BmsFrameContext(1007, 1), true);
        Assert.That(column.GetLandmineState(mine).Detonated, Is.True);
        Assert.That(output.MainResults, Is.EqualTo(held ? 2 : 1));
    }

    [Test]
    public void CallbackInputFormsAnotherBatchAtTheSameTimestamp()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2 };
        var first = new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 };
        var second = new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 };
        var output = new BmsRecordingJudgementOutput();
        var engine = new BmsJudgementEngine([first, second], beatmap.LayoutVariant, 1, true, output);
        var column = engine.GetColumn(1);
        column.ActivateNote(first);
        column.ActivateNote(second);
        output.BeforeResultCheckpoint = (note, _) =>
        {
            if (ReferenceEquals(note, first))
                engine.QueueInput(1000, 1, true, false);
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
    public void PauseCompletesHoldsThroughTheSameEngine(BmsLongNoteMode mode)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2, LockedLongNoteMode = mode };
        var note = new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 1000 };
        var output = new BmsRecordingJudgementOutput();
        var engine = new BmsJudgementEngine([note], beatmap.LayoutVariant, 1, true, output);
        var column = engine.GetColumn(1);
        column.ActivateNote(note);
        engine.QueueInput(1000, 1, true, false);
        engine.ProcessFrame(new BmsFrameContext(1000, 1), 1);
        var publications = new List<bool>();
        output.StatePublished = (_, _, frame) => publications.Add(frame.IsPauseCompletion);
        engine.CompleteLongNotesAtPause(new BmsFrameContext(1200, 1.5));
        Assert.That(publications, Is.EqualTo(mode switch
        {
            BmsLongNoteMode.LongNote => new[] { false },
            BmsLongNoteMode.ChargeNote => new[] { false, true },
            _ => new[] { true },
        }));
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
        var engine = new BmsJudgementEngine([note, mine], beatmap.LayoutVariant, 1, false, output);
        var column = engine.GetColumn(1);
        column.ActivateNote(note);
        column.ActivateLandmine(mine);
        engine.QueueInput(1000, 1, true, false);
        engine.QueueInput(1000, 1, false, false);
        engine.ProcessFrame(new BmsFrameContext(1000, 1), 500);
        engine.ProcessFrame(new BmsFrameContext(1000, 1), 500);
        Assert.That(column.GetLandmineState(mine).Detonated, Is.False);
        Assert.That(output.BodyTicks, Is.Empty);
        Assert.That(column.GetLongNoteState(note).TailJudged, Is.True);
        Assert.That(column.PressedDirections, Is.Zero);
    }
}
