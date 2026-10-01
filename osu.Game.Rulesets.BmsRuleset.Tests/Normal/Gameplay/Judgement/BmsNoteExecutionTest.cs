using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Rules;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsNoteExecutionTest
{
    [Test]
    public void RegistrationsShareOnlySupportedOperationsAndValueWindows()
    {
        var notes = makeNotes(1024);
        var first = new BmsJudgementEngine(notes, BmsLayoutVariant.Bme7K, 1, true, new BmsRecordingJudgementOutput()).GetColumn(1);
        var second = new BmsJudgementEngine(notes, BmsLayoutVariant.Bme7K, 1, true, new BmsRecordingJudgementOutput()).GetColumn(1);
        Assert.That(typeof(BmsNoteRegistration).IsValueType, Is.True);
        Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<BmsNoteJudgementWindows>(), Is.False);
        Assert.That(first.NoteRegistrations, Has.Length.EqualTo(notes.Length));
        for (var i = 0; i < notes.Length; i++)
        {
            var registration = first.NoteRegistrations[i];
            Assert.That(registration.Object, Is.SameAs(notes[i]));
            Assert.That(registration.Slot, Is.EqualTo(i));
            Assert.That(registration.Press, Is.SameAs(first.NoteRegistrations[0].Press));
            Assert.That(registration.Passive, Is.SameAs(first.NoteRegistrations[0].Passive));
            Assert.That(registration.Press.Target, Is.SameAs(registration.Passive.Target));
        }
        Assert.That(first.NoteRegistrations[0].Press, Is.Not.SameAs(second.NoteRegistrations[0].Press), "each column borrows its own typed storage");
    }

    [Test]
    public void PassiveDeadlineAdvancesAfterConsumptionAndRemoval()
    {
        var notes = makeNotes(3);
        var output = new BmsRecordingJudgementOutput();
        var context = new BmsJudgementEngine(notes, BmsLayoutVariant.Bme7K, 1, true, output).GetColumn(1);
        foreach (var note in notes)
            context.ActivateNote(note);
        var firstDeadline = context.NoteWindows[0].NextPassiveJudgementTime(notes[0]);
        var secondDeadline = context.NoteWindows[1].NextPassiveJudgementTime(notes[1]);
        context.RefreshPassiveDeadline(0);
        Assert.That(context.NextPassiveUpdateTime, Is.EqualTo(firstDeadline));
        context.TryPressRegisteredNote(notes[0], HitResult.Perfect, new BmsPressContext(1000, new BmsFrameContext(1000, 1)));
        context.RefreshPassiveDeadline(firstDeadline);
        Assert.That(context.NextPassiveUpdateTime, Is.EqualTo(firstDeadline), "retain the old conservative deadline until it is crossed");
        context.RefreshPassiveDeadline(firstDeadline + 1);
        Assert.That(context.NextPassiveUpdateTime, Is.EqualTo(secondDeadline));
        context.BeginNoteRemoval(notes[1]);
        context.RefreshPassiveDeadline(secondDeadline + 1);
        Assert.That(context.NextPassiveUpdateTime, Is.EqualTo(context.NoteWindows[2].NextPassiveJudgementTime(notes[2])));
        context.BeginNoteRemoval(notes[2]);
        context.RefreshPassiveDeadline(10000);
        Assert.That(context.NextPassiveUpdateTime, Is.EqualTo(double.PositiveInfinity));
    }

    [Test]
    public void RemovingNoteCanCommitOnceWithoutReenteringAnyCandidateQuery()
    {
        var notes = makeNotes(2);
        var output = new BmsRecordingJudgementOutput();
        var context = new BmsJudgementEngine(notes, BmsLayoutVariant.Bme7K, 1, true, output).GetColumn(1);
        foreach (var note in notes)
            context.ActivateNote(note);
        context.BeginNoteRemoval(notes[0]);
        output.BeforeResultCheckpoint = (note, _) =>
        {
            Assert.That(output.GetMainResult(note).Type, Is.EqualTo(HitResult.Meh));
            Assert.That(context.IsNoteCandidate(note), Is.False);
            Assert.That(context.FirstPendingNote, Is.SameAs(notes[1]));
        };
        output.AfterResultCheckpoint = output.BeforeResultCheckpoint;
        context.CheckRegisteredNotePassive(notes[0], new BmsFrameContext(1800, 1));
        context.CheckRegisteredNotePassive(notes[0], new BmsFrameContext(1801, 1));
        Assert.That(output.MainResults, Is.EqualTo(1));
        Assert.That(context.TryPressRegisteredNote(notes[0], HitResult.Perfect, new BmsPressContext(1000, new BmsFrameContext(1801, 1))), Is.False);
    }

    [Test]
    public void UnsupportedScoringTypesCannotEnterTheBoundedPath()
    {
        var note = makeNotes(1)[0];
        note.Beatmap.LockedLongNoteMode = (BmsLongNoteMode)999;
        var longNote = new BmsLongNote { Beatmap = note.Beatmap, Column = 1, StartTime = 1000, Duration = 1000 };
        Assert.Throws<InvalidOperationException>(() => new BmsJudgementEngine([longNote], BmsLayoutVariant.Bme7K, 1, true, new BmsRecordingJudgementOutput()));
    }

    private static BmsHitObject[] makeNotes(int count)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2 };
        return Enumerable.Range(0, count).Select(i => (BmsHitObject)new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 + i * 1000 }).ToArray();
    }
}
