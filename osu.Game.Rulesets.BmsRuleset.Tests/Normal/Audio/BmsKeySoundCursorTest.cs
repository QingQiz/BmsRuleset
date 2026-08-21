using System;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Playback;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Audio;

[TestFixture]
public class BmsKeySoundCursorTest
{
    // HitWindows is only populated by ApplyDefaults at load time, so it stays null in these tests
    // and isPastBadWindow falls back to BmsHitWindows.FALLBACK_BAD_WINDOW (280 ms) — no beatmap needed.
    private static BmsNote note(double startTime) => new()
    {
        StartTime = startTime,
        Column = 0,
    };

    private static BmsLandmine mine(double startTime) => new()
    {
        StartTime = startTime,
        Column = 0,
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
}
