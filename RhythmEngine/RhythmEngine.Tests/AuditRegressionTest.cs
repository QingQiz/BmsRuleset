using NUnit.Framework;
using RhythmEngine.Profiles;

namespace RhythmEngine.Tests;

[TestFixture]
public class AuditRegressionTest
{
    [TestCase(BmsRank.VeryHard, "great")]
    [TestCase(BmsRank.Hard, "great")]
    [TestCase(BmsRank.Normal, "great")]
    [TestCase(BmsRank.Easy, "pgreat")]
    public void Lr2LateShortHoldRetainsHeadUntilTheNextClockTick(BmsRank rank, string grade)
    {
        var session = Lr2Beta3Profile.Create(chart(hold(0, 2000, 2010)), new Lr2Beta3Options(Rank: rank)).CreateSession();
        session.AdvanceTo(ms(2020), [input(2020, true)]);
        Assert.That(session.EventCount, Is.Zero);
        session.AdvanceTo(ms(2021));
        Assert.That(session.GetEventsSince(0).Select(e => (e.Result, e.Time)), Is.EqualTo(new[] { (grade, ms(2021)) }));
        Assert.That(session.Completed, Is.True);
    }

    [TestCase(900, 1200, "held")]
    [TestCase(900, 1250, "let-go")]
    [TestCase(1050, 1299, "held")]
    [TestCase(1050, 1300, "let-go")]
    public void EtternaHoldGraceStartsAfterItsHeadTime(int release, int regrab, string tail)
    {
        var session = EtternaProfile.Create(chart(hold(0, 1000, 2000))).CreateSession();
        session.AdvanceTo(ms(2200), [input(850, true), input(release, false), input(regrab, true)]);
        Assert.That(session.GetEventsSince(0).Last().Result, Is.EqualTo(tail));
    }

    [TestCase(KeyNoteKind.Hold)]
    [TestCase(KeyNoteKind.Roll)]
    public void EtternaMissedHoldStillConstrainsInternalTap(KeyNoteKind kind)
    {
        var session = EtternaProfile.Create(chart(hold(0, 1000, 2000) with { Kind = kind }, tap(1, 1500))).CreateSession();
        session.AdvanceTo(ms(1350), [input(1350, true)]);
        Assert.That(session.GetEventsSince(0).Any(e => e.NoteId == 1), Is.False);
        session.AdvanceTo(ms(1500), [input(1400, false), input(1500, true)]);
        Assert.That(session.GetEventsSince(0).Single(e => e.NoteId == 1).Result, Is.EqualTo("w1"));
    }

    [Test]
    public void LazerUsesLogicalLaneEdgesWhileRetainingPhysicalControls()
    {
        var session = ManiaProfiles.CreateLazer(chart(tap(0, 1000), tap(1, 1100), tap(2, 1300))).CreateSession();
        session.AdvanceTo(ms(1100), [input(1000, true, 1), input(1100, true, 2)]);
        Assert.That(session.GetEventsSince(0).Select(e => e.NoteId), Is.EqualTo(new[] { 0 }));
        session.AdvanceTo(ms(1350), [input(1150, false, 1), input(1200, false, 2), input(1300, true, 1)]);
        Assert.That(session.GetEventsSince(0).Single(e => e.NoteId == 2).Result, Is.EqualTo("perfect"));
    }

    [TestCase(KeyNoteKind.Hold)]
    [TestCase(KeyNoteKind.ReleaseHold)]
    [TestCase(KeyNoteKind.SustainHold)]
    public void BeatorajaRetainsResolvedHoldHeadForEmptyPoor(KeyNoteKind kind)
    {
        var session = BmsProfiles.CreateBeatoraja(chart(hold(0, 1000, 1010) with { Kind = kind })).CreateSession();
        session.AdvanceTo(ms(1050), [input(1000, true), input(1010, false), input(1050, true)]);
        Assert.That(session.GetEventsSince(0).Last().Result, Is.EqualTo("empty-poor"));
        Assert.That(session.Health.Value, Is.EqualTo(98));
        Assert.That(session.Score.Score, Is.EqualTo(kind == KeyNoteKind.Hold ? 2 : 4));
        session.AdvanceTo(ms(1200));
        Assert.That(session.Completed, Is.True);
    }

    [Test]
    public void StableEarlyJudgedTapBlocksItsLaneUntilStart()
    {
        var session = ManiaProfiles.CreateStable(chart(tap(0, 1000), tap(1, 1050))).CreateSession();
        session.AdvanceTo(ms(950), [input(900, true), input(901, false), input(950, true)]);
        Assert.That(session.GetEventsSince(0).Select(e => (e.NoteId, e.Result)), Is.EqualTo(new[] { (0, "ok") }));
        session.AdvanceTo(ms(1050), [input(951, false), input(1050, true)]);
        Assert.That(session.GetEventsSince(0).Last().NoteId, Is.EqualTo(1));
        Assert.That(session.GetEventsSince(0).Last().Result, Is.EqualTo("perfect"));
    }

    private static KeyChart chart(params KeyNote[] notes) => new(1, notes);
    private static KeyNote tap(int id, int time) => new(id, 0, ms(time), ms(time));
    private static KeyNote hold(int id, int start, int end) => new(id, 0, ms(start), ms(end), KeyNoteKind.Hold);
    private static InputEvent input(int time, bool pressed, int control = 0) => new(ms(time), 0, pressed, control);
    private static long ms(int value) => value * 1000L;
}
