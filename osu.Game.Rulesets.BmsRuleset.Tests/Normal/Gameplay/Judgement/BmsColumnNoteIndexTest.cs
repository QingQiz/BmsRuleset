using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsColumnNoteIndexTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void ReorderingUsesActiveRanksAndPreservesBodyPhaseOrder(bool batchUpdates)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, LockedLongNoteMode = BmsLongNoteMode.HellChargeNote, Rank = 2 };
        var notes = Enumerable.Range(0, 3).Select(_ => new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 1000 }).ToArray();
        var column = new BmsJudgementEngine(notes, beatmap.LayoutVariant, 1, batchUpdates, new BmsRecordingJudgementOutput()).GetColumn(1);
        foreach (var note in notes)
        {
            column.ActivateNote(note);
            column.TryPressRegisteredNote(note, HitResult.Perfect, new BmsPressContext(1000, new BmsFrameContext(1000, 1)));
        }

        BmsHitObject[] order = [notes[2], notes[0], notes[1]];
        column.SetNoteActivationOrder(order);
        Assert.That(column.GetPendingLongNoteTails(), Is.EqualTo(order));
        Assert.That(column.GetActiveBodies(), Is.EqualTo(batchUpdates ? notes : order));
        Assert.That(order.Select(n => column.Notes.RankOf(column.GetNoteSlot(n))), Is.EqualTo(new[] { 0, 1, 2 }));

        column.BeginNoteRemoval(notes[0]);
        Assert.That(column.Notes.RankOf(column.GetNoteSlot(notes[0])), Is.EqualTo(-1));
        Assert.That(column.GetPendingLongNoteTails(), Is.EqualTo(new[] { notes[2], notes[1] }));
        column.ActivateNote(notes[0]);
        column.SetNoteActivationOrder(notes);
        Assert.That(column.GetPendingLongNoteTails(), Is.EqualTo(notes));
        Assert.That(column.GetActiveBodies().Distinct().Count(), Is.EqualTo(3));
    }

    [Test]
    public void RejectedOrderLeavesTheLiveIndexUsable()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2 };
        var notes = Enumerable.Range(0, 3).Select(_ => new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 }).ToArray();
        var column = new BmsJudgementEngine(notes, beatmap.LayoutVariant, 1, true, new BmsRecordingJudgementOutput()).GetColumn(1);
        column.ActivateNote(notes[0]);
        column.ActivateNote(notes[1]);
        BmsHitObject[][] invalidOrders = [[notes[1], notes[1]], [notes[1]], [notes[1], notes[2]]];
        foreach (var order in invalidOrders)
        {
            Assert.Throws<InvalidOperationException>(() => column.SetNoteActivationOrder(order));
            Assert.That(column.GetNoteCandidates(), Is.EqualTo(new[] { notes[0], notes[1] }));
            Assert.That(column.FirstPendingNote, Is.SameAs(notes[0]));
            Assert.That(column.Notes.RankOf(column.GetNoteSlot(notes[0])), Is.Zero);
        }
        column.SetNoteActivationOrder([notes[1], notes[0]]);
        Assert.That(column.GetNoteCandidates(), Is.EqualTo(new[] { notes[1], notes[0] }));
        Assert.That(column.FirstPendingNote, Is.SameAs(notes[0]), "Equal-time head priority follows registration, independently of traversal order.");
    }

    [Test]
    public void MovingAJudgedNoteKeepsItInHistoricalEmptyPoorSelection()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2 };
        var notes = Enumerable.Range(1, 3).Select(i => new BmsNote { Beatmap = beatmap, Column = 1, StartTime = i * 1000 }).ToArray();
        var column = new BmsJudgementEngine(notes, beatmap.LayoutVariant, 1, true, new BmsRecordingJudgementOutput()).GetColumn(1);
        var moved = notes[2];
        column.ActivateNote(moved);
        column.TryPressRegisteredNote(moved, HitResult.Perfect, new BmsPressContext(3000, new BmsFrameContext(3000, 1)));
        column.BeginNoteRemoval(moved);
        moved.StartTime = 500;
        column.RefreshNoteTiming(moved);
        var outcome = column.HandleNotePress(new BmsPressContext(600, new BmsFrameContext(3001, 1)));
        Assert.That(outcome, Is.EqualTo(PressOutcome.ForEmptyPoor(500, 1)));
        Assert.That(column.GetNoteCandidates(), Is.Empty);
    }
}
