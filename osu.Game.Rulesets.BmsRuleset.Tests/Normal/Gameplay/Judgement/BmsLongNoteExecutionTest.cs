using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
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
    public void SameApplicationTimeRevertsInReverseSubmissionOrder()
    {
        var (context, output, notes) = create(2);
        foreach (var note in notes.OfType<BmsLongNote>())
            context.TryPressRegisteredNote(note, HitResult.Perfect, new BmsPressContext(3000, new BmsFrameContext(3000, 1)));
        foreach (var note in notes.OfType<BmsLongNote>())
            context.CheckRegisteredNotePassive(note, new BmsFrameContext(5000, .75));
        var reverted = new List<BmsHitObject>();
        output.Reverting = note =>
        {
            reverted.Add(note);
            Assert.That(output.GetMainResult(note).HasResult, Is.True);
            Assert.That(output.GetRawTime(note), Is.EqualTo(5000));
        };
        output.AfterReset = note =>
        {
            Assert.That(output.GetMainResult(note).HasResult, Is.False);
            Assert.That(output.GetRawTime(note), Is.Null);
            Assert.That(output.GetMainResult(note).GameplayRate, Is.EqualTo(.75));
            Assert.That(output.GetEndpoints((BmsLongNote)note), Has.Count.EqualTo(2));
        };
        output.RevertAfter(5000);
        Assert.That(reverted, Is.Empty);
        output.RevertAfter(4999);
        Assert.That(reverted, Is.EqualTo(new[] { notes[1], notes[0] }));
        output.RevertAfter(4998);
        Assert.That(reverted, Has.Count.EqualTo(2));
    }

    [Test]
    public void RemovingLongNoteCannotReenterIndexesDuringLastPassiveResult()
    {
        var (context, output, notes) = create();
        var note = (BmsLongNote)notes[0];
        context.BeginNoteRemoval(note);
        output.StatePublished = (source, _, _) =>
        {
            Assert.That(context.IsNoteCandidate(source), Is.False);
            Assert.That(context.GetHoldingNotes(), Is.Empty);
        };
        context.CheckRegisteredNotePassive(note, new BmsFrameContext(5000, 1));
        context.CheckRegisteredNotePassive(note, new BmsFrameContext(5001, 1));
        Assert.That(output.MainResults, Is.EqualTo(1));
        Assert.That(context.GetLongNoteState(note).TailJudged, Is.True);
        Assert.That(context.IsNoteCandidate(note), Is.False);
    }

    [TestCase((BmsLongNoteMode)999)]
    public void UncoveredModesFailAtComposition(BmsLongNoteMode mode)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, LockedLongNoteMode = mode, Rank = 2 };
        var note = new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 3000, Duration = 1000 };
        Assert.Throws<InvalidOperationException>(() => new BmsJudgementEngine([note], beatmap.LayoutVariant, 1, true, new BmsRecordingJudgementOutput()));
    }

    private static (BmsColumnJudgementContext Context, BmsRecordingJudgementOutput Output, BmsHitObject[] Notes) create(int count = 1)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, LockedLongNoteMode = BmsLongNoteMode.LongNote, Rank = 2 };
        var notes = Enumerable.Range(0, count).Select(i => (BmsHitObject)new BmsLongNote
            { Beatmap = beatmap, Column = 1, StartTime = 3000 + i, Duration = 1000 }).Append(new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 6000 }).ToArray();
        var output = new BmsRecordingJudgementOutput();
        var context = new BmsJudgementEngine(notes, beatmap.LayoutVariant, 1, true, output).GetColumn(1);
        foreach (var note in notes) context.ActivateNote(note);
        return (context, output, notes);
    }
}
