using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using RhythmEngine.Profiles.Common;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Playback;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Audio;

[TestFixture]
public class BmsKeySoundCursorTest
{
    [TestCase(835, 2210, 3210)]
    public void SelectedProfileControlsSamplePreparationAndExpiry(double nextReady, double headExpiry, double tailExpiry)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2 };
        var tap = new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000, SampleKey = 1 };
        var hold = new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 2000, Duration = 1000, Kind = KeyNoteKind.ReleaseHold, SampleKey = 2, TailSampleKey = 3 };
        beatmap.HitObjects.AddRange([tap, hold]);
        using var score = new BmsScoreProcessor();
        score.ApplyBeatmap(beatmap);
        var compiled = score.EngineChart;
        var usages = BmsGameplayAudioController.GetSampleUsages(beatmap, compiled).ToArray();
        var cursor = new BmsKeySoundCursor(beatmap.HitObjects, note => note.StartTime + compiled.TimingFor(note).HeadDeadline / 1000.0);
        Assert.Multiple(() =>
        {
            Assert.That(usages.Single(usage => usage.SampleKey == 2).EarliestTriggerTime, Is.EqualTo(nextReady));
            Assert.That(usages.Single(usage => usage.SampleKey == 2).LatestTriggerTime, Is.EqualTo(headExpiry));
            Assert.That(usages.Single(usage => usage.SampleKey == 3).EarliestTriggerTime, Is.EqualTo(nextReady + 1000));
            Assert.That(usages.Single(usage => usage.SampleKey == 3).LatestTriggerTime, Is.EqualTo(tailExpiry));
            Assert.That(cursor.Next(headExpiry - 1000, _ => false), Is.SameAs(tap));
            Assert.That(cursor.Next(headExpiry - 1000 + 0.001, _ => false), Is.SameAs(hold));
            Assert.That(cursor.Next(headExpiry - 1000, _ => false), Is.SameAs(tap));
        });

        var session = new JudgementSession(compiled.Chart, compiled.Configuration);
        session.AdvanceTo(Time.FromMilliseconds(headExpiry + 0.001));
        Assert.That(session.GetNote(compiled.Ids[hold]).Parts.Single(part => part.Part == NoteParts.HEAD).Resolved, Is.True);
    }

    [TestCase(BmsJudgementModifiers.None, 835)]
    [TestCase(BmsJudgementModifiers.NoBad, 887.5)]
    [TestCase(BmsJudgementModifiers.NoBad | BmsJudgementModifiers.NoGood, 955)]
    [TestCase(BmsJudgementModifiers.NoBad | BmsJudgementModifiers.NoGreat, 985)]
    public void AudioReadinessUsesTheEarliestEnabledHitWindow(BmsJudgementModifiers modifiers, double earliest)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2 };
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000, SampleKey = 1 });
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 2000, SampleKey = 2 });
        var usage = BmsGameplayAudioController.GetSampleUsages(beatmap, new BmsEngineChart(beatmap, new BmsGameplayProfile(modifiers))).Single(item => item.SampleKey == 2);
        Assert.That(usage.EarliestTriggerTime, Is.EqualTo(earliest));
        Assert.That(usage.LatestTriggerTime, Is.EqualTo(2210));
    }

    [Test]
    public void ConfiguredModsShareAudioAndFrameworkOffsets()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 0, TotalColumns = 8 };
        var first = new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 };
        var second = new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 2000 };
        first.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        second.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        beatmap.HitObjects.AddRange([first, second]);
        using var score = new BmsScoreProcessor();
        score.Mods.Value = [new BmsModLr2(), new BmsModNoBad()];
        score.ApplyBeatmap(beatmap);
        var cursor = new BmsKeySoundCursor(beatmap.HitObjects, note => note.StartTime + score.EngineChart.TimingFor(note).HeadDeadline / 1000.0);
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

    private static BmsKeySoundCursor createCursor(IReadOnlyList<BmsHitObject> notes, IReadOnlyList<BmsInvisibleNote> invisibleNotes = null, GameplayProfile profile = null)
    {
        var compiled = new BmsEngineChart(notes.ToArray(), notes.FirstOrDefault()?.Beatmap.LayoutVariant ?? BmsLayoutVariant.Bme7K,
            notes.Select(note => note.Column).Distinct().DefaultIfEmpty(0).ToArray(), profile);
        return new BmsKeySoundCursor(notes, note => note.StartTime + compiled.TimingFor(note).HeadDeadline / 1000.0, invisibleNotes);
    }

    [Test]
    public void NextAdvancesCursorForwardAcrossNotes()
    {
        var cursor = createCursor([note(1000), note(2000)]);

        Assert.That(cursor.Next(500, _ => false)?.StartTime, Is.EqualTo(1000));
        // note@1000 finished at t=1100 (still within BAD window, so isFinished drives the skip)
        Assert.That(cursor.Next(1100, h => h.StartTime == 1000)?.StartTime, Is.EqualTo(2000));
    }

    [Test]
    public void NextAdvancesToLaterNoteAfterForwardJump()
    {
        var cursor = createCursor([note(1000), note(100000)]);

        Assert.That(cursor.Next(500, _ => false)?.StartTime, Is.EqualTo(1000));
        // Forward jump to t=6000: note@1000 is past its BAD window (6000 > 1280), so the linear
        // scan advances past it to note@100000 (not past: 6000 < 100280) — no reset needed.
        Assert.That(cursor.Next(6000, _ => false)?.StartTime, Is.EqualTo(100000));
    }

    [Test]
    public void NextResetsCursorOnBackwardSeek()
    {
        var cursor = createCursor([note(1000)]);

        // t=2000: past BAD window -> null, cursor advances past the note.
        Assert.That(cursor.Next(2000, _ => false), Is.Null);
        // Backward seek (500 < 2000) resets the cursor -> note@1000 returned again.
        Assert.That(cursor.Next(500, _ => false)?.StartTime, Is.EqualTo(1000));
    }

    [Test]
    public void BackwardSeekKeepsPendingNotesWithLargeExRank()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2, ExRank = 1000 };
        var pending = new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 };
        var cursor = createCursor([pending], profile: new BmsGameplayProfile(BmsJudgementModifiers.NoBad));
        Assert.That(cursor.Next(4000, _ => false), Is.Null);
        Assert.That(cursor.Next(2500, _ => false), Is.SameAs(pending));
    }

    [Test]
    public void InvisibleSampleSearchUpdatesTheBackwardSeekClock()
    {
        var pending = note(1000);
        var hidden = new BmsInvisibleNote { StartTime = 2000 };
        var cursor = createCursor([pending], [hidden]);
        Assert.That(cursor.Next(1100, _ => true), Is.Null);
        Assert.That(cursor.Next(3000, _ => true), Is.SameAs(hidden));
        Assert.That(cursor.Next(1200, _ => false), Is.SameAs(pending));
    }

    [Test]
    public void NextReturnsFirstPendingNoteBeforeItIsPastBadWindow()
    {
        var cursor = createCursor([note(1000)]);

        // t=500: note@1000 not past BAD window (1000+280=1280), not finished -> returned.
        Assert.That(cursor.Next(500, _ => false)?.StartTime, Is.EqualTo(1000));
    }

    [Test]
    public void NextReturnsNullForEmptySlice()
    {
        var cursor = createCursor(Array.Empty<BmsHitObject>());

        Assert.That(cursor.Next(500, _ => false), Is.Null);
    }

    [Test]
    public void NextReturnsNullWhenAllNotesFinished()
    {
        var cursor = createCursor([note(1000)]);

        Assert.That(cursor.Next(500, _ => true), Is.Null);
    }

    [Test]
    public void NextSkipsLandmines()
    {
        var cursor = createCursor([mine(1000), note(2000)]);

        Assert.That(cursor.Next(500, _ => false)?.StartTime, Is.EqualTo(2000));
    }

    [Test]
    public void NextSkipsNotesPastBadWindow()
    {
        var cursor = createCursor([note(1000)]);

        // t=2000: note@1000 past BAD window (2000 > 1280) -> skipped -> null.
        Assert.That(cursor.Next(2000, _ => false), Is.Null);
    }

    [Test]
    public void InvisibleSamplePersistsUntilLaterVisibleNoteAndSurvivesSeeking()
    {
        var hidden = new BmsInvisibleNote { StartTime = 1000 };
        var visible = note(5000);
        var cursor = createCursor([visible], [hidden]);
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
        var cursor = createCursor([], [hidden]);
        Assert.That(cursor.Next(1000, _ => false), Is.Null);
        Assert.That(cursor.Next(100000, _ => true), Is.SameAs(hidden));
        Assert.That(cursor.Next(0, _ => false), Is.Null);
    }

    [Test]
    public void SimultaneousVisibleNoteWinsAndJudgedLongNotesDoNotReplaceHiddenSample()
    {
        var hidden = new BmsInvisibleNote { StartTime = 1000 };
        var visible = note(1000);
        var cursor = createCursor([visible, new BmsLongNote { Beatmap = visible.Beatmap, Column = 1, StartTime = 2000, Duration = 500 }, mine(3000)], [hidden]);
        Assert.That(cursor.Next(4000, _ => true), Is.SameAs(visible));
    }
}
