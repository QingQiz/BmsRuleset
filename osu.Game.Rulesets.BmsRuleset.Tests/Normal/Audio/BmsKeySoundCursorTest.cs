using System;
using System.Linq;
using NUnit.Framework;
using RhythmEngine.Profiles.Bms;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Playback;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Audio;

[TestFixture]
public class BmsKeySoundCursorTest
{
    [TestCase(BmsJudgementModifiers.None, 835)]
    [TestCase(BmsJudgementModifiers.NoBad, 887.5)]
    [TestCase(BmsJudgementModifiers.NoBad | BmsJudgementModifiers.NoGood, 955)]
    [TestCase(BmsJudgementModifiers.NoBad | BmsJudgementModifiers.NoGreat, 985)]
    public void AudioReadinessUsesTheEarliestEnabledHitWindow(BmsJudgementModifiers modifiers, double earliest)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2, JudgementModifiers = modifiers };
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000, SampleKey = 1 });
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 2000, SampleKey = 2 });
        var usage = BmsGameplayAudioController.GetSampleUsages(beatmap).Single(item => item.SampleKey == 2);
        Assert.That(usage.EarliestTriggerTime, Is.EqualTo(earliest));
        Assert.That(usage.LatestTriggerTime, Is.EqualTo(2210));
    }

    [Test]
    public void ConfiguredModsShareAudioAndFrameworkOffsets()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 0, TotalColumns = 8, JudgementModifiers = BmsJudgementModifiers.Lr2oraja | BmsJudgementModifiers.NoBad };
        var first = new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 };
        var second = new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 2000 };
        first.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        second.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        var cursor = new BmsKeySoundCursor([first, second]);
        Assert.That(first.HitWindows.WindowFor(HitResult.Ok), Is.EqualTo(200));
        Assert.That(cursor.Next(1100, _ => false), Is.SameAs(first));
        Assert.That(cursor.Next(1200, _ => false), Is.SameAs(first));
        Assert.That(cursor.Next(1200.001, _ => false), Is.SameAs(second));
        var result = new BmsJudgementResult(first, first.CreateJudgement()) { Type = HitResult.Ok };
        result.SetTiming(1100, 1);
        Assert.That(result.TimeOffset, Is.EqualTo(100));
    }

    // Seven-key EASY has a 280 ms passive deadline.
    private static BmsNote note(double startTime) => new()
    {
        Beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 3 },
        StartTime = startTime,
        Column = 1,
    };

    private static BmsLandmine mine(double startTime) => new()
    {
        Beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 3 },
        StartTime = startTime,
        Column = 1,
    };

    [Test]
    public void NextAdvancesCursorForwardAcrossNotes()
    {
        var cursor = new BmsKeySoundCursor([note(1000), note(2000)]);

        Assert.That(cursor.Next(500, _ => false)?.StartTime, Is.EqualTo(1000));
        // note@1000 finished at t=1100 (still within BAD window, so isFinished drives the skip)
        Assert.That(cursor.Next(1100, h => h.StartTime == 1000)?.StartTime, Is.EqualTo(2000));
    }

    [Test]
    public void NextAdvancesToLaterNoteAfterForwardJump()
    {
        var cursor = new BmsKeySoundCursor([note(1000), note(100000)]);

        Assert.That(cursor.Next(500, _ => false)?.StartTime, Is.EqualTo(1000));
        // Forward jump to t=6000: note@1000 is past its BAD window (6000 > 1280), so the linear
        // scan advances past it to note@100000 (not past: 6000 < 100280) — no reset needed.
        Assert.That(cursor.Next(6000, _ => false)?.StartTime, Is.EqualTo(100000));
    }

    [Test]
    public void NextResetsCursorOnBackwardSeek()
    {
        var cursor = new BmsKeySoundCursor([note(1000)]);

        // t=2000: past BAD window -> null, cursor advances past the note.
        Assert.That(cursor.Next(2000, _ => false), Is.Null);
        // Backward seek (500 < 2000) resets the cursor via binary search -> note@1000 returned again.
        Assert.That(cursor.Next(500, _ => false)?.StartTime, Is.EqualTo(1000));
    }

    [Test]
    public void NextReturnsFirstPendingNoteBeforeItIsPastBadWindow()
    {
        var cursor = new BmsKeySoundCursor([note(1000)]);

        // t=500: note@1000 not past BAD window (1000+280=1280), not finished -> returned.
        Assert.That(cursor.Next(500, _ => false)?.StartTime, Is.EqualTo(1000));
    }

    [Test]
    public void NextReturnsNullForEmptySlice()
    {
        var cursor = new BmsKeySoundCursor(Array.Empty<BmsHitObject>());

        Assert.That(cursor.Next(500, _ => false), Is.Null);
    }

    [Test]
    public void NextReturnsNullWhenAllNotesFinished()
    {
        var cursor = new BmsKeySoundCursor([note(1000)]);

        Assert.That(cursor.Next(500, _ => true), Is.Null);
    }

    [Test]
    public void NextSkipsLandmines()
    {
        var cursor = new BmsKeySoundCursor([mine(1000), note(2000)]);

        Assert.That(cursor.Next(500, _ => false)?.StartTime, Is.EqualTo(2000));
    }

    [Test]
    public void NextSkipsNotesPastBadWindow()
    {
        var cursor = new BmsKeySoundCursor([note(1000)]);

        // t=2000: note@1000 past BAD window (2000 > 1280) -> skipped -> null.
        Assert.That(cursor.Next(2000, _ => false), Is.Null);
    }

    [Test]
    public void InvisibleSamplePersistsUntilLaterVisibleNoteAndSurvivesSeeking()
    {
        var hidden = new BmsInvisibleNote { StartTime = 1000 };
        var visible = note(5000);
        var cursor = new BmsKeySoundCursor([visible], [hidden]);
        Assert.That(cursor.Next(1000, _ => false), Is.SameAs(visible));
        Assert.That(cursor.Next(1001, _ => true), Is.SameAs(hidden));
        Assert.That(cursor.Next(4000, _ => true), Is.SameAs(hidden));
        Assert.That(cursor.Next(6000, _ => true), Is.SameAs(visible));
        Assert.That(cursor.Next(2000, _ => true), Is.SameAs(hidden));
        Assert.That(cursor.Next(500, _ => false), Is.SameAs(visible));
    }

    [Test]
    public void InvisibleOnlyLaneIsSilentUntilPassed()
    {
        var hidden = new BmsInvisibleNote { StartTime = 1000 };
        var cursor = new BmsKeySoundCursor([], [hidden]);
        Assert.That(cursor.Next(1000, _ => false), Is.Null);
        Assert.That(cursor.Next(100000, _ => true), Is.SameAs(hidden));
        Assert.That(cursor.Next(0, _ => false), Is.Null);
    }

    [Test]
    public void SimultaneousVisibleNoteWinsAndJudgedLongNotesDoNotReplaceHiddenSample()
    {
        var hidden = new BmsInvisibleNote { StartTime = 1000 };
        var visible = note(1000);
        var cursor = new BmsKeySoundCursor([visible, new BmsLongNote { StartTime = 2000 }, mine(3000)], [hidden]);
        Assert.That(cursor.Next(4000, _ => true), Is.SameAs(visible));
    }
}
