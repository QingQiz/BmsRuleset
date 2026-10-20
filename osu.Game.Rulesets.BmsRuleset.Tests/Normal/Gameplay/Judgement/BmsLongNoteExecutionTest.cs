using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsLongNoteExecutionTest
{
    [Test]
    public void StatePublicationPrecedesIndexesAndMainCallbacksPrecedeTail()
    {
        var (context, output, notes) = create();
        var note = (BmsLongNote)notes[0];
        var publications = 0;
        output.StatePublished = (source, state, _) =>
        {
            if (state.TailJudged)
                return;
            publications++;
            Assert.That(state.HeadJudged, Is.True);
            Assert.That(output.GetMainResult(source).HasResult, Is.False);
            Assert.That(context.FirstPendingNote, Is.SameAs(note), "the read-only output still observes the previous index checkpoint");
            Assert.That(context.GetHoldingNotes(), Is.Empty);
        };
        Assert.That(context.HandleNotePress(new BmsPressContext(3000, new BmsFrameContext(3003, 1.25))).Kind, Is.EqualTo(PressOutcomeKind.Hit));
        Assert.That(publications, Is.EqualTo(1));
        Assert.That(context.GetHoldingNotes(), Is.EqualTo(new[] { note }));
        Assert.That(context.FirstPendingNote, Is.Null);
        Assert.That(context.IsNoteCandidate(note), Is.True, "head state does not replace the framework result authority");
        var observed = new List<string>();
        output.BeforeResultCheckpoint = (source, _) => inspect("before", source);
        output.AfterResultCheckpoint = (source, _) => inspect("after", source);
        context.HandleExecutionRelease(3990, new BmsFrameContext(3997, 1.5));
        Assert.That(observed, Is.EqualTo(new[] { "before", "after" }));
        Assert.That(context.GetLongNoteState(note).TailJudged, Is.True);
        Assert.That(context.GetHoldingNotes(), Is.Empty);
        Assert.That(context.IsNoteCandidate(note), Is.False);

        void inspect(string checkpoint, BmsHitObject source)
        {
            observed.Add(checkpoint);
            Assert.That(output.GetMainResult(source).HasResult, Is.True);
            Assert.That(context.GetLongNoteState(note).TailJudged, Is.False);
            Assert.That(context.GetLongNoteState(note).HeadTime, Is.EqualTo(3003));
            Assert.That(context.GetHoldingNotes(), Is.EqualTo(new[] { note }));
            Assert.That(output.GetEndpoints(note).Select(e => e.TimeOffset), Is.EqualTo(new[] { 3d, -10d }));
        }
    }

    [Test]
    public void MixedRegistrationsShareBindingsAndKeepSeparateTypedState()
    {
        var (context, _, notes) = create(2);
        Assert.That(typeof(BmsNoteRegistration).IsValueType, Is.True);
        Assert.That(context.NoteRegistrations[0].Press, Is.SameAs(context.NoteRegistrations[1].Press));
        Assert.That(context.NoteRegistrations[0].Passive, Is.SameAs(context.NoteRegistrations[1].Passive));
        context.TryPressRegisteredNote(notes[0], HitResult.Perfect, new BmsPressContext(3000, new BmsFrameContext(3000, 1)));
        Assert.That(context.GetLongNoteState((BmsLongNote)notes[0]).HeadJudged, Is.True);
        Assert.That(context.GetLongNoteState((BmsLongNote)notes[1]).HeadJudged, Is.False);
        Assert.That(context.NoteRegistrations[2].Press, Is.Not.SameAs(context.NoteRegistrations[0].Press));
        Assert.That(context.NoteRegistrations[0].Object, Is.SameAs(notes[0]));
    }

    [Test]
    public void OrdinaryColumnsDoNotAllocateHoldSlotMaps()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K };
        BmsHitObject[] notes = Enumerable.Range(0, 1024).Select(i => (BmsHitObject)new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 + i }).ToArray();
        var column = new BmsJudgementEngine(notes, beatmap.LayoutVariant, 1, new BmsRecordingJudgementOutput()).GetColumn(1);
        var holds = BmsJudgementStateInspection.GetHolds(column);
        Assert.That((Array)typeof(BmsHoldRegistry).GetField("bindingIndices", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(holds)!, Is.Empty);
        Assert.That(column.GetPendingLongNoteTails(), Is.Empty);
    }

    [Test]
    public void SparseHoldsAllocateOnlyTheirOwnBindings()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, LockedLongNoteMode = BmsLongNoteMode.LongNote };
        BmsHitObject[] notes = Enumerable.Range(0, 1024).Select(i => (BmsHitObject)new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 + i }).ToArray();
        notes[100] = new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1100, Duration = 1000 };
        notes[900] = new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1900, Duration = 1000 };
        var column = new BmsJudgementEngine(notes, beatmap.LayoutVariant, 1, new BmsRecordingJudgementOutput()).GetColumn(1);
        var holds = BmsJudgementStateInspection.GetHolds(column);
        var bindings = (Array)typeof(BmsHoldRegistry).GetField("bindings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(holds)!;
        Assert.That(bindings, Has.Length.EqualTo(2));
        Assert.That(column.NoteRegistrations[100].Press, Is.SameAs(column.NoteRegistrations[900].Press));
        Assert.That(BmsJudgementStateInspection.GetRuntime(column, notes[100]), Is.Not.SameAs(BmsJudgementStateInspection.GetRuntime(column, notes[900])));

        column.TryPressRegisteredNote(notes[900], HitResult.Perfect, new BmsPressContext(1900, new BmsFrameContext(1900, 1)));
        Assert.That(column.GetLongNoteState((BmsLongNote)notes[900]).HeadJudged, Is.True);
        Assert.That(column.GetLongNoteState((BmsLongNote)notes[100]).HeadJudged, Is.False);
    }


    [TestCase((BmsLongNoteMode)999)]
    public void UncoveredModesFailAtComposition(BmsLongNoteMode mode)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, LockedLongNoteMode = mode, Rank = 2 };
        var note = new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 3000, Duration = 1000 };
        Assert.Throws<InvalidOperationException>(() => new BmsJudgementEngine([note], beatmap.LayoutVariant, 1, new BmsRecordingJudgementOutput()));
    }

    private static (BmsColumnJudgementRuntime Context, BmsRecordingJudgementOutput Output, BmsHitObject[] Notes) create(int count = 1)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, LockedLongNoteMode = BmsLongNoteMode.LongNote, Rank = 2 };
        var notes = Enumerable.Range(0, count).Select(i => (BmsHitObject)new BmsLongNote
            { Beatmap = beatmap, Column = 1, StartTime = 3000 + i, Duration = 1000 }).Append(new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 6000 }).ToArray();
        var output = new BmsRecordingJudgementOutput();
        var context = new BmsJudgementEngine(notes, beatmap.LayoutVariant, 1, output).GetColumn(1);

        return (context, output, notes);
    }
}
