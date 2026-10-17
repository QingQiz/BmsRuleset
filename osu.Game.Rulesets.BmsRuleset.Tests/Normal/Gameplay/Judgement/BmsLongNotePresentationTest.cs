using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.Presentation;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsLongNotePresentationTest
{
    private static readonly BmsLongNoteState held = new(true, true, false, HitResult.Perfect, 1000, HitResult.None, null, null);

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void InputAndTimeRefreshPresentationWithoutStatePublication(BmsLongNoteMode mode)
    {
        var note = createNote(mode);
        var map = BmsLongNotePresentation.SelectMapping(mode);
        var context = new BmsLongNotePresentationContext(1200, true);
        var first = map(note, held, context);
        Assert.That(first.HoldingBody, Is.True);
        Assert.That(first.ReleasedStyle, Is.False);
        Assert.That(first.AnimateBody, Is.True);
        Assert.That(first.PrepareHeadPin && first.RestoreHeadPin && first.HeadReached, Is.True);

        var released = map(note, held, context with { Held = false });
        Assert.That(released.HoldingBody, Is.False);
        Assert.That(released.ReleasedStyle, Is.True);
        Assert.That(released.AnimateBody, Is.True);
        Assert.That(map(note, held, context), Is.EqualTo(first), "Repeated visual reads are pure, including at the same timestamp.");

        var afterEnd = map(note, held, context with { Time = 1600, Held = false });
        Assert.That(afterEnd.ReleasedStyle, Is.False);
        Assert.That(afterEnd.HoldLightActive, Is.False);
        Assert.That(afterEnd.KeepVisibleAfterHit, Is.EqualTo(mode != BmsLongNoteMode.LongNote));
    }

    [Test]
    public void LnHeadCanDrivePresentationBeforeTheMainResult()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote);
        run.Press(1000, HitResult.Perfect);
        Assert.That(run.Output.GetMainResult(run.Note).HasResult, Is.False);
        var current = BmsLongNotePresentation.SelectMapping(BmsLongNoteMode.LongNote)(run.Note, run.State,
            new BmsLongNotePresentationContext(1100, true));
        Assert.That(current.HoldingBody && current.AnimateBody && current.PrepareHeadPin, Is.True);
    }

    [TestCase(BmsLongNoteMode.LongNote, 1100, HitResult.Ok)]
    [TestCase(BmsLongNoteMode.LongNote, 1289.999, HitResult.Ok)]
    [TestCase(BmsLongNoteMode.ChargeNote, 1100, HitResult.Meh)]
    [TestCase(BmsLongNoteMode.ChargeNote, 1289.999, HitResult.Meh)]
    public void OutOfWindowReleaseKeepsBodyUntilEnd(BmsLongNoteMode mode, double releaseTime, HitResult expected)
    {
        var run = new BmsLongNoteTestContext(mode);
        run.Press(1000, HitResult.Perfect);
        run.Release(releaseTime);

        Assert.That(run.State.TailJudged, Is.True);
        Assert.That(run.State.TailResult, Is.EqualTo(expected));
        Assert.That(run.State.EarlyReleaseOutsideWindow, Is.True);
        double[] times = [releaseTime, 1499];
        bool[] heldInputs = [false, true];
        foreach (var time in times)
        {
            foreach (var heldInput in heldInputs)
            {
                var current = run.Presentation(heldInput, time);
                Assert.That(current.Retired || current.HoldingBody || current.HoldLightActive, Is.False);
                Assert.That(current.KeepAlive && current.KeepVisibleAfterHit && current.ReleasedStyle, Is.True);
                Assert.That(current.LifetimeEnd, Is.EqualTo(run.Note.EndTime));
            }
        }
    }

    [TestCase(BmsLongNoteMode.LongNote, 1335, HitResult.Ok)]
    [TestCase(BmsLongNoteMode.LongNote, 1340, HitResult.Ok)]
    [TestCase(BmsLongNoteMode.LongNote, 1390, HitResult.Great)]
    [TestCase(BmsLongNoteMode.LongNote, 1490, HitResult.Perfect)]
    [TestCase(BmsLongNoteMode.ChargeNote, 1335, HitResult.Ok)]
    [TestCase(BmsLongNoteMode.ChargeNote, 1340, HitResult.Ok)]
    [TestCase(BmsLongNoteMode.ChargeNote, 1390, HitResult.Great)]
    [TestCase(BmsLongNoteMode.ChargeNote, 1490, HitResult.Perfect)]
    public void InWindowTailHidesImmediately(BmsLongNoteMode mode, double releaseTime, HitResult expected)
    {
        var run = new BmsLongNoteTestContext(mode);
        run.Press(1000, HitResult.Perfect);
        run.Release(releaseTime);

        Assert.That(run.State.TailJudged, Is.True);
        Assert.That(run.State.TailResult, Is.EqualTo(expected));
        Assert.That(run.State.EarlyReleaseOutsideWindow, Is.False);
        var current = run.Presentation(false, releaseTime);
        Assert.That(current.Retired && current.ClearBodyOnRetire, Is.True);
        Assert.That(current.KeepAlive || current.KeepVisibleAfterHit || current.ReleasedStyle, Is.False);
    }

    [TestCase(BmsLongNoteMode.LongNote, 1289.999, true)]
    [TestCase(BmsLongNoteMode.LongNote, 1335, false)]
    [TestCase(BmsLongNoteMode.ChargeNote, 1289.999, true)]
    [TestCase(BmsLongNoteMode.ChargeNote, 1335, false)]
    public void DelayedFrameUsesReleaseWindowInsteadOfApplicationTime(BmsLongNoteMode mode, double releaseTime, bool keepBody)
    {
        var run = new BmsLongNoteTestContext(mode);
        run.Press(1000, HitResult.Perfect);
        run.Release(releaseTime, 1499);

        Assert.That(run.State.TailTime, Is.EqualTo(1499));
        Assert.That(run.State.EarlyReleaseOutsideWindow, Is.EqualTo(keepBody));
        var current = run.Presentation(false, 1499);
        Assert.That(current.Retired, Is.EqualTo(!keepBody));
        Assert.That(current.KeepAlive, Is.EqualTo(keepBody));
    }

    [TestCase(BmsLongNoteMode.LongNote, 1100, true)]
    [TestCase(BmsLongNoteMode.LongNote, 1250, false)]
    [TestCase(BmsLongNoteMode.ChargeNote, 1100, true)]
    [TestCase(BmsLongNoteMode.ChargeNote, 1250, false)]
    public void ReleaseMarginExpiryPreservesReleaseWindow(BmsLongNoteMode mode, double releaseTime, bool keepBody)
    {
        var run = new BmsLongNoteTestContext(mode, layout: BmsLayoutVariant.Pms9K);
        run.Press(1000, HitResult.Perfect);
        run.Release(releaseTime);
        run.Frame(releaseTime + 199, 199, false);
        Assert.That(run.State.TailJudged, Is.False);
        Assert.That(run.Presentation(false, releaseTime + 199).ReleasedStyle, Is.True);

        run.Frame(releaseTime + 200, 1, false);
        Assert.That(run.State.TailJudged, Is.True);
        Assert.That(run.State.EarlyReleaseOutsideWindow, Is.EqualTo(keepBody));
        var current = run.Presentation(false, releaseTime + 200);
        Assert.That(current.Retired, Is.EqualTo(!keepBody));
        Assert.That(current.KeepAlive && current.ReleasedStyle, Is.EqualTo(keepBody));
        Assert.That(current.LifetimeEnd, Is.EqualTo(run.Note.EndTime));
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    public void RewindClearsOutOfWindowBreakBeforeInWindowRelease(BmsLongNoteMode mode)
    {
        var run = new BmsLongNoteTestContext(mode);
        run.Press(1000, HitResult.Perfect);
        run.Release(1100);
        Assert.That(run.State.EarlyReleaseOutsideWindow, Is.True);

        run.Rewind(1050);
        Assert.That(run.State.TailJudged || run.State.EarlyReleaseOutsideWindow, Is.False);
        run.Release(1490);
        Assert.That(run.State.TailJudged, Is.True);
        Assert.That(run.State.EarlyReleaseOutsideWindow, Is.False);
        Assert.That(run.Presentation(false, 1490).Retired, Is.True);
    }

    [Test]
    public void HeadBadDoesNotKeepAnInWindowTailVisible()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote);
        run.Press(1150, HitResult.Ok);
        run.Release(1490);

        Assert.That(run.State.TailResult, Is.EqualTo(HitResult.Ok));
        Assert.That(run.State.EarlyReleaseOutsideWindow, Is.False);
        Assert.That(run.Presentation(false, 1490).Retired, Is.True);
    }

    [Test]
    public void SuccessfulHcnTailKeepsBodyUntilItsOwnExpiry()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.HellChargeNote);
        run.Press(1000, HitResult.Perfect);
        run.Release(1490);
        var before = run.Presentation(false, 1490);
        Assert.That(run.State.TailJudged, Is.True);
        Assert.That(before.HoldingBody && before.AnimateBody, Is.True);
        Assert.That(before.ReleasedStyle || before.PrepareHeadPin || before.HoldLightActive || before.Retired, Is.False);
        run.Frame(1600, 110, false);
        var after = run.Presentation(false, 1600);
        Assert.That(after.Retired, Is.True);
        Assert.That(after.HoldingBody || after.AnimateBody || after.ClearBodyOnRetire, Is.False);
    }

    private static BmsLongNote createNote(BmsLongNoteMode mode) => new()
    {
        Beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2, LockedLongNoteMode = mode },
        Column = 1, StartTime = 1000, Duration = 500,
    };
}
