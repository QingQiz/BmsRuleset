using System.Linq;
using NUnit.Framework;
using RhythmEngine.Profiles.Common;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsLongNotePresentationTest
{
    [Test]
    public void ReleasedBodyDoesNotBlockAdjacentNote(
        [Values(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode mode,
        [Values(1200, 1834.999, 1835, 1880, 1910)] double releaseTime,
        [Values(1999.999, 2000, 2000.001)] double pressTime)
    {
        var (run, next) = createAdjacentNotes(mode);
        run.Press(1000);
        run.Release(releaseTime);
        var endpoints = run.Output.GetEndpoints(run.Note).ToArray();

        Assert.That(run.Repress(pressTime), Is.True);
        Assert.That(run.Output.GetMainResult(next).Type, Is.EqualTo(HitResult.Perfect));
        Assert.That(run.Output.GetEndpoints(run.Note), Is.EqualTo(endpoints));
    }

    [Test]
    public void Lr2ReleasedBodyDoesNotBlockAdjacentNote(
        [Values(1200, 1799.999, 1800, 1899.999, 1900, 1990)] double releaseTime,
        [Values(1999.999, 2000, 2000.001)] double pressTime)
    {
        var (run, next) = createAdjacentNotes(BmsLongNoteMode.LongNote, lr2: true);
        run.Press(1000);
        run.Release(releaseTime);
        Assert.That(run.Repress(pressTime), Is.True);
        Assert.That(run.Output.GetMainResult(next).Type, Is.EqualTo(HitResult.Perfect));
    }

    [Test]
    public void ReleasedLongNoteDoesNotTakeTheNextHead(
        [Values(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode mode,
        [Values(100, 1000)] double duration,
        [Values] bool nextIsLongNote)
    {
        var (run, next) = createAdjacentNotes(mode, duration: duration, nextIsLongNote: nextIsLongNote);
        run.Press(1000);
        run.Release(1050);
        run.Repress(next.StartTime);

        var part = run.Column.GetNote(next).Parts.Single(part => part.Part == (nextIsLongNote ? NoteParts.HEAD : NoteParts.TAP));
        Assert.That(part.Resolved, Is.True);
        var result = nextIsLongNote ? run.Column.Profile.HitResultFor(part.Result) : run.Output.GetMainResult(next).Type;
        Assert.That(result, Is.EqualTo(HitResult.Perfect));
    }

    [TestCase(BmsLongNoteMode.LongNote, 50, HitResult.Good)]
    [TestCase(BmsLongNoteMode.ChargeNote, 50, HitResult.Good)]
    [TestCase(BmsLongNoteMode.LongNote, 112.5, HitResult.Good)]
    [TestCase(BmsLongNoteMode.ChargeNote, 112.5, HitResult.Good)]
    [TestCase(BmsLongNoteMode.LongNote, 112.501, HitResult.Ok)]
    [TestCase(BmsLongNoteMode.ChargeNote, 112.501, HitResult.Ok)]
    [TestCase(BmsLongNoteMode.LongNote, 165, HitResult.Ok)]
    [TestCase(BmsLongNoteMode.ChargeNote, 165, HitResult.Ok)]
    [TestCase(BmsLongNoteMode.LongNote, 165.001, HitResult.None)]
    [TestCase(BmsLongNoteMode.ChargeNote, 165.001, HitResult.None)]
    public void TailTimePressUsesTheNextNotesHeadWindow(BmsLongNoteMode mode, double gap, HitResult expected)
    {
        var (run, next) = createAdjacentNotes(mode, nextOffset: gap);
        HitResult? nonConsuming = null;
        run.Output.NonConsumingJudgement += (_, _, result) => nonConsuming = result;
        run.Press(1000);
        run.Release(1200);
        run.Repress(2000);

        Assert.That(run.Output.GetMainResult(next).Type, Is.EqualTo(expected));
        Assert.That(nonConsuming, Is.EqualTo(expected == HitResult.None ? HitResult.Miss : (HitResult?)null));
    }

    [TestCase(BmsLongNoteMode.LongNote, 1200, 2000, true)]
    [TestCase(BmsLongNoteMode.ChargeNote, 1200, 2000, true)]
    [TestCase(BmsLongNoteMode.LongNote, 1835, 2000, false)]
    [TestCase(BmsLongNoteMode.ChargeNote, 1835, 2000, false)]
    [TestCase(BmsLongNoteMode.LongNote, 1835, 2000.001, false)]
    [TestCase(BmsLongNoteMode.ChargeNote, 1835, 2000.001, false)]
    [TestCase(BmsLongNoteMode.LongNote, 1835, 2000.002, true)]
    [TestCase(BmsLongNoteMode.ChargeNote, 1835, 2000.002, false)]
    [TestCase(BmsLongNoteMode.LongNote, 1835, 2035, true)]
    [TestCase(BmsLongNoteMode.ChargeNote, 1835, 2035, false)]
    [TestCase(BmsLongNoteMode.ChargeNote, 1835, 2035.001, true)]
    [TestCase(BmsLongNoteMode.LongNote, 1880, 2000, true)]
    [TestCase(BmsLongNoteMode.ChargeNote, 1880, 2000, true)]
    public void PmsPendingReleaseTakesInputUntilSettlement(BmsLongNoteMode mode, double releaseTime, double pressTime, bool hitsNext)
    {
        var (run, next) = createAdjacentNotes(mode, BmsLayoutVariant.Popn9K);
        run.Press(1000);
        run.Release(releaseTime);
        run.Repress(pressTime);

        Assert.That(run.Output.GetMainResult(next).HasResult, Is.EqualTo(hitsNext));
        Assert.That(run.Presentation(true, pressTime).HoldingBody, Is.False);
    }

    private static (BmsLongNoteTestContext Run, BmsHitObject Next) createAdjacentNotes(BmsLongNoteMode mode,
                                                                                    BmsLayoutVariant layout = BmsLayoutVariant.Bme7K, bool lr2 = false,
                                                                                    double duration = 1000, bool nextIsLongNote = false, double nextOffset = 0)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = layout,
            TotalColumns = layout == BmsLayoutVariant.Popn9K ? 9 : 8,
            Rank = 2,
            LockedLongNoteMode = mode,
        };
        var note = new BmsLongNote { Beatmap = beatmap, Kind = mode.ToNoteKind(), Column = 1, StartTime = 1000, Duration = duration };
        BmsHitObject next = nextIsLongNote ? new BmsLongNote { Kind = mode.ToNoteKind(), Duration = 500 } : new BmsNote();
        next.Beatmap = beatmap;
        next.Column = 1;
        next.StartTime = 1000 + duration + nextOffset;
        var run = new BmsLongNoteTestContext(note, notes: [note, next], profile: lr2 ? GameplayProfile.FromMods([new BmsModLr2()]) : null);
        return (run, next);
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void InputAndTimeRefreshPresentationWithoutStatePublication(BmsLongNoteMode mode)
    {
        var run = new BmsLongNoteTestContext(mode);
        run.Press(1000);
        var first = run.Presentation(true, 1200);
        Assert.That(first.HoldingBody, Is.True);
        Assert.That(first.ReleasedStyle, Is.False);
        Assert.That(first.AnimateBody, Is.True);
        Assert.That(first.PrepareHeadPin && first.HeadReached, Is.True);

        var released = run.Presentation(false, 1200);
        Assert.That(released.HoldingBody, Is.False);
        Assert.That(released.ReleasedStyle, Is.True);
        Assert.That(released.AnimateBody || released.HoldLightActive, Is.False);
        Assert.That(run.Presentation(true, 1200), Is.EqualTo(first), "Repeated visual reads are pure, including at the same timestamp.");

        var afterEnd = run.Presentation(false, 1600);
        Assert.That(afterEnd.ReleasedStyle, Is.False);
        Assert.That(afterEnd.HoldLightActive, Is.False);
        Assert.That(afterEnd.KeepVisibleAfterHit, Is.EqualTo(mode != BmsLongNoteMode.LongNote));
    }

    [Test]
    public void LnHeadCanDrivePresentationBeforeTheMainResult()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote);
        run.Press(1000);
        Assert.That(run.Output.GetMainResult(run.Note).HasResult, Is.False);
        var current = run.Presentation(true, 1100);
        Assert.That(current.HoldingBody && current.AnimateBody && current.PrepareHeadPin, Is.True);
    }

    [TestCase(BmsLongNoteMode.LongNote, 1100, HitResult.Ok)]
    [TestCase(BmsLongNoteMode.LongNote, 1289.999, HitResult.Ok)]
    [TestCase(BmsLongNoteMode.ChargeNote, 1100, HitResult.Meh)]
    [TestCase(BmsLongNoteMode.ChargeNote, 1289.999, HitResult.Meh)]
    public void OutOfWindowReleaseKeepsBodyUntilEnd(BmsLongNoteMode mode, double releaseTime, HitResult expected)
    {
        var run = new BmsLongNoteTestContext(mode);
        run.Press(1000);
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
        run.Press(1000);
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
        run.Press(1000);
        run.Release(releaseTime, 1499);

        Assert.That(run.Column.GetNote(run.Note).Parts[2].Time / 1000.0, Is.EqualTo(releaseTime));
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
        var run = new BmsLongNoteTestContext(mode, layout: BmsLayoutVariant.Popn9K);
        run.Press(1000);
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
        run.Press(1000);
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
        run.Press(1150);
        run.Release(1490);

        Assert.That(run.State.TailResult, Is.EqualTo(HitResult.Perfect));
        Assert.That(run.Results.Single(), Is.EqualTo(HitResult.Ok));
        Assert.That(run.State.EarlyReleaseOutsideWindow, Is.False);
        Assert.That(run.Presentation(false, 1490).Retired, Is.True);
    }

    [Test]
    public void SuccessfulHcnTailKeepsBodyUntilItsOwnExpiry()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.HellChargeNote);
        run.Press(1000);
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

}
