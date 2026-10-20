using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsColumnNoteIndexTest
{
    [Test]
    public void MovingAJudgedNoteKeepsItInHistoricalEmptyPoorSelection()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2 };
        var notes = Enumerable.Range(1, 3).Select(i => new BmsNote { Beatmap = beatmap, Column = 1, StartTime = i * 1000 }).ToArray();
        var column = new BmsJudgementEngine(notes, beatmap.LayoutVariant, 1, new BmsRecordingJudgementOutput()).GetColumn(1);
        var moved = notes[2];

        column.TryPressRegisteredNote(moved, HitResult.Perfect, new BmsPressContext(3000, new BmsFrameContext(3000, 1)));

        moved.StartTime = 500;
        column.RefreshNoteTiming(moved);
        var outcome = column.HandleNotePress(new BmsPressContext(600, new BmsFrameContext(3001, 1)));
        Assert.That(outcome, Is.EqualTo(PressOutcome.ForEmptyPoor(500, 1)));
        Assert.That(column.GetNoteCandidates(), Is.EqualTo(notes.Take(2)));
    }

    [Test]
    public void DurationSearchSkipsCloserHeadsWithNoHitWindow()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 3 };
        var notes = new[] { 900d, 1000, 1060, 1150 }.Select(time => new BmsNote
        {
            Beatmap = beatmap, Column = 1, StartTime = time, JudgementRate = time is 1000 or 1060 ? .05 : 1,
        }).ToArray();
        var output = new BmsRecordingJudgementOutput();
        var column = new BmsJudgementEngine(notes, beatmap.LayoutVariant, 1, output).GetColumn(1);
        column.HandleNotePress(new BmsPressContext(1030, new BmsFrameContext(1030, 1)), BmsJudgementAlgorithm.Duration);
        Assert.That(output.GetMainResult(notes[3]).Type, Is.EqualTo(HitResult.Good));
        Assert.That(output.MainResults, Is.EqualTo(1));
    }

    [Test]
    public void DurationSearchKeepsChartOrderForStackedHeadsAndPrefersEarlierDistanceTies()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 3 };
        var notes = new[] { 1000d, 1000, 1060 }.Select(time => new BmsNote { Beatmap = beatmap, Column = 1, StartTime = time }).ToArray();
        var output = new BmsRecordingJudgementOutput();
        var column = new BmsJudgementEngine(notes, beatmap.LayoutVariant, 1, output).GetColumn(1);
        column.HandleNotePress(new BmsPressContext(1030, new BmsFrameContext(1030, 1)), BmsJudgementAlgorithm.Duration);
        Assert.That(output.GetMainResult(notes[0]).HasResult, Is.True);
        Assert.That(output.MainResults, Is.EqualTo(1));
    }

    [Test]
    public void StackedPmsHeadsShareTheEmptyPoorMistakeMarker()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Pms9K, Rank = 3 };
        var notes = Enumerable.Range(0, 2).Select(_ => new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 }).ToArray();
        var column = new BmsJudgementEngine(notes, beatmap.LayoutVariant, 1, new BmsRecordingJudgementOutput()).GetColumn(1);
        Assert.That(column.HandleNotePress(new BmsPressContext(600, new BmsFrameContext(600, 1))).Kind, Is.EqualTo(PressOutcomeKind.EmptyPoor));
        Assert.That(notes.All(column.HasPmsMistake), Is.True);
    }

    [TestCase(63)]
    [TestCase(64)]
    [TestCase(65)]
    [TestCase(129)]
    public void TimingChangesAndRebuildsRestorePendingHeadsAcrossIndexWords(int count)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 3 };
        var notes = Enumerable.Range(0, count).Select(i => new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 + i * 10 }).ToArray();
        var output = new BmsRecordingJudgementOutput();
        var column = new BmsJudgementEngine(notes, beatmap.LayoutVariant, 1, output).GetColumn(1);
        foreach (var note in notes)
            column.TryPressRegisteredNote(note, HitResult.Perfect, new BmsPressContext(note.StartTime, new BmsFrameContext(note.StartTime, 1)));
        Assert.That(column.FirstPendingNote, Is.Null);

        output = new BmsRecordingJudgementOutput();
        column = new BmsJudgementEngine(notes, beatmap.LayoutVariant, 1, output).GetColumn(1);
        foreach (var note in notes.Take(count / 2 + 1))
            column.TryPressRegisteredNote(note, HitResult.Perfect, new BmsPressContext(note.StartTime, new BmsFrameContext(note.StartTime, 1)));
        Assert.That(column.FirstPendingNote, Is.SameAs(notes[count / 2 + 1]));

        notes[^1].StartTime = 900;
        column.RefreshNoteTiming(notes[^1]);
        column.HandleNotePress(new BmsPressContext(900, new BmsFrameContext(900, 1)), BmsJudgementAlgorithm.Duration);
        Assert.That(output.GetMainResult(notes[^1]).Type, Is.EqualTo(HitResult.Perfect));
        Assert.That(column.FirstPendingNote, Is.SameAs(notes[count / 2 + 1]));
    }
}
