using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsRegistryRecoveryTest
{
    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void MixedColumnRewindsActiveAndPooledHolds(BmsLongNoteMode mode)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2, LockedLongNoteMode = mode };
        var first = new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 2000, Duration = 400 };
        var second = new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 4000, Duration = 400 };
        BmsHitObject[] notes =
        [
            new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 }, first,
            new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 3000 }, second,
            new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 5000 },
        ];
        var output = new BmsRecordingJudgementOutput();
        var column = new BmsJudgementEngine(notes, beatmap.LayoutVariant, 1, true, output).GetColumn(1);
        foreach (var note in notes) column.ActivateNote(note);
        foreach (var hold in new[] { first, second })
        {
            column.PressedDirections = 1;
            Assert.That(column.TryPressRegisteredNote(hold, HitResult.Perfect,
                new BmsPressContext(hold.StartTime, new BmsFrameContext(hold.StartTime, 1))), Is.True);
            column.HandleExecutionRelease(hold.EndTime, new BmsFrameContext(hold.EndTime, 1));
            Assert.That(column.GetLongNoteState(hold).TailJudged, Is.True);
        }

        column.BeginNoteRemoval(second);
        rewind(2500, 1);
        Assert.That(column.GetLongNoteState(first).TailJudged, Is.True, "Earlier history survives the seek.");
        Assert.That(column.GetLongNoteState(second).HeadJudged, Is.False, "Pooled holds rewind before reactivation.");
        column.ActivateNote(second);
        Assert.That(column.GetPendingLongNoteTails(), Is.Empty);
        Assert.That(column.GetActiveBodies(), Is.Empty);

        rewind(1900, 2);
        Assert.That(column.GetLongNoteState(first).HeadJudged, Is.False);
        Assert.That(column.GetLongNoteState(second).HeadJudged, Is.False);
        Assert.That(column.IsNoteCandidate(first), Is.True);
        Assert.That(column.IsNoteCandidate(second), Is.True);
        Assert.That(column.TryPressRegisteredNote(second, HitResult.Perfect,
            new BmsPressContext(4000, new BmsFrameContext(4000, 1))), Is.True);
        Assert.That(column.GetPendingLongNoteTails(), Is.EqualTo(new[] { second }));
        Assert.That(column.GetLongNoteState(first).HeadJudged, Is.False);

        void rewind(double time, long step)
        {
            output.RevertAfter(time);
            output.RevertSyntheticAfter(time);
            column.RewindPressHistory(time);
            column.Schedule.BeginBatch(1, step, step);
            var frame = new BmsFrameContext(time, 1, IsRewinding: true);
            Assert.That(column.Schedule.BeginRewindStep(frame), Is.True);
            column.RestoreExecutionState(frame);
            Assert.That(column.Schedule.BeginRewindStep(frame), Is.False);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RejectedMineOrderPreservesPublishedOrder(bool foreign)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2 };
        var mines = Enumerable.Range(0, 4).Select(_ => new BmsLandmine { Beatmap = beatmap, Column = 1, StartTime = 1000 }).ToArray();
        var observed = new List<BmsHitObject>();
        var output = new BmsRecordingJudgementOutput { BeforeResultCheckpoint = (note, _) => observed.Add(note) };
        var column = new BmsJudgementEngine(mines, beatmap.LayoutVariant, 1, false, output).GetColumn(1);
        foreach (var mine in mines.Take(3)) column.ActivateLandmine(mine);
        column.SetLandmineActivationOrder([mines[1], mines[0], mines[2]]);
        Assert.Throws<InvalidOperationException>(() => column.SetLandmineActivationOrder([mines[2], foreign ? mines[3] : mines[2], mines[0]]));
        Assert.Throws<InvalidOperationException>(() => column.SetLandmineActivationOrder([mines[2], mines[0]]));
        column.UpdateLandmines(new BmsFrameContext(1000, 1), true);
        Assert.That(observed, Is.EqualTo(new[] { mines[1], mines[0], mines[2] }));

        output.RevertAfter(999);
        column.RewindLandmines(999);
        observed.Clear();
        column.SetLandmineActivationOrder([mines[2], mines[1], mines[0]]);
        column.UpdateLandmines(new BmsFrameContext(1000, 1), true);
        Assert.That(observed, Is.EqualTo(new[] { mines[2], mines[1], mines[0] }), "A rejected proposal must not poison the next valid order.");
    }
}
