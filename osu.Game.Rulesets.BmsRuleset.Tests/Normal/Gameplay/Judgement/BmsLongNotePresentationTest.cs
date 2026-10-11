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
            new BmsLongNotePresentationContext(1100, true, HasMainResult: false, ResumeTime: 1200));
        Assert.That(current.HoldingBody && current.AnimateBody && current.PrepareHeadPin, Is.True);
        Assert.That(current.HiddenDuringResume, Is.False);
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

    [TestCase(false, false)]
    [TestCase(true, true)]
    public void ResumeVisibilityRespectsSyntheticCompletionWithoutMainResult(bool mainResult, bool hidden)
    {
        var note = createNote(BmsLongNoteMode.HellChargeNote);
        var state = held with { HeadResult = HitResult.Meh, TailJudged = true, TailResult = HitResult.Meh, HasActiveBody = true };
        var map = BmsLongNotePresentation.SelectMapping(BmsLongNoteMode.HellChargeNote);
        var context = new BmsLongNotePresentationContext(1100, false, mainResult, 1200);
        Assert.That(map(note, state, context).HiddenDuringResume, Is.EqualTo(hidden));
        Assert.That(map(note, state, context with { ResumeTime = 1500 }).HiddenDuringResume, Is.True);
        Assert.That(map(note, state, context with { ResumeTime = null }).HiddenDuringResume, Is.False);
    }

    private static BmsLongNote createNote(BmsLongNoteMode mode) => new()
    {
        Beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2, LockedLongNoteMode = mode },
        Column = 1, StartTime = 1000, Duration = 500,
    };
}
