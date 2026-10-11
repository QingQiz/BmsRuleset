using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

public partial class BmsCoreJudgementScheduleTest
{
    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void TimingReorderAndRebindingKeepEachNotesRegisteredState(BmsLongNoteMode mode)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2, LockedLongNoteMode = mode };
        var first = new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 500 };
        var second = new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 2000, Duration = 500 };
        var output = new BmsRecordingJudgementOutput();
        var column = new BmsJudgementEngine([first, second], beatmap.LayoutVariant, 1, true, output).GetColumn(1);
        column.ActivateNote(first);
        column.ActivateNote(second);
        var schedule = column.Schedule;
        var firstBinding = schedule.Bind(first);
        var secondBinding = schedule.Bind(second);
        var firstState = BmsJudgementStateInspection.GetRuntime(column, first);
        var secondState = BmsJudgementStateInspection.GetRuntime(column, second);

        second.StartTime = 500;
        column.RefreshNoteConfiguration(second);
        var rebound = schedule.Bind(second);
        schedule.Unbind(secondBinding);
        schedule.BeginBatch(0, 1, 1);
        schedule.RunSubtree([firstBinding, secondBinding, rebound], new BmsFrameContext(800, 1));

        Assert.That(column.GetLongNoteState(first).HeadJudged, Is.False);
        Assert.That(column.GetLongNoteState(second).HeadJudged, Is.True);
        Assert.That(column.GetLongNoteState(second).HeadResult, Is.EqualTo(HitResult.Meh));
        Assert.That(BmsJudgementStateInspection.GetRuntime(column, first), Is.SameAs(firstState));
        Assert.That(BmsJudgementStateInspection.GetRuntime(column, second), Is.SameAs(secondState));
        Assert.That(schedule.TryGetCurrent(second, out var current), Is.True);
        Assert.That(current, Is.EqualTo(rebound));

        column.RewindLongNotes(0);
        output.RevertAfter(0);
        column.RestoreExecutionState(new BmsFrameContext(0, 1, true));
        schedule.BeginBatch(1, 2, 2);
        schedule.RunSubtree([firstBinding, rebound], new BmsFrameContext(1300, 1));
        Assert.That(column.GetLongNoteState(first).HeadJudged, Is.True);
        Assert.That(column.GetLongNoteState(second).HeadJudged, Is.True);
        Assert.That(BmsJudgementStateInspection.GetRuntime(column, first), Is.SameAs(firstState));
        Assert.That(BmsJudgementStateInspection.GetRuntime(column, second), Is.SameAs(secondState));
    }
}
