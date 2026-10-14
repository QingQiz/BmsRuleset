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
}
