using System.Text.Json;
using NUnit.Framework;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Etterna;
using RhythmEngine.Profiles.Mania;
using RhythmEngine.Profiles.O2Jam;

namespace RhythmEngine.Tests;

[TestFixture]
public class ProfileConformanceTest
{
    [Test]
    public void Lr2RejectsUnsupportedRankBeforeCompilingMineOnlyCharts()
        => Assert.Throws<NotSupportedException>(() => BmsProfiles.CreateLr2(
            new KeyChart(1, [new KeyNote(0, 0, 1000000, 1000000, KeyNoteKind.Mine)]), new BmsOptions(Rank: BmsRank.VeryEasy)));

    [TestCase(false)]
    [TestCase(true)]
    public void BeatorajaSearchExcludesTheExactEarly500MsBoundary(bool scratch)
    {
        var source = new KeyChart(1, [note(0, 1000) with { Directional = scratch }]);
        var session = BmsProfiles.CreateBeatoraja(source).CreateRecordingSession();
        session.AdvanceTo(ms(500), [press(500)]);
        Assert.That(session.EventCount, Is.Zero);
        session.AdvanceTo(ms(500) + 2, [new InputEvent(ms(500) + 1, 0, false), new InputEvent(ms(500) + 2, 0, true)]);
        Assert.That(session.GetEventsSince(0).Single().Result.Value, Is.EqualTo("empty-poor"));
    }

    [Test]
    public void Open2JamScoringMatchesExecutedOriginalMethod()
    {
        using var stream = typeof(ProfileConformanceTest).Assembly.GetManifestResourceStream("RhythmEngine.Tests.Reference.open2jam-scoring.json")!;
        using var data = JsonDocument.Parse(stream);
        var rows = data.RootElement.EnumerateArray().ToArray();
        var chart = new KeyChart(1, rows.Select((_, i) => note(i, (i + 1) * 1000)));
        var session = O2JamProfile.Create(chart, new O2JamOptions(BeatBasedWindows: false)).CreateRecordingSession();

        for (var i = 0; i < rows.Length; i++)
        {
            var expected = rows[i];
            var time = (i + 1) * 1000;
            var input = expected.GetProperty("input").GetString();
            var offset = input switch { "COOL" => 0, "GOOD" => 100, "BAD" => 150, _ => (int?)null };
            session.AdvanceTo(ms(time + 500), offset is { } hit
                ? [press(time + hit), release(time + hit + 1)]
                : []);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(session.GetEventsSince(0).Last().Result.Value, Is.EqualTo(expected.GetProperty("result").GetString()!.ToLowerInvariant()), $"result {i}");
                Assert.That(session.Score.Score, Is.EqualTo(expected.GetProperty("score").GetInt32()), $"score {i}");
                Assert.That(session.Score.Counters["jam"], Is.EqualTo(expected.GetProperty("jam").GetInt32()), $"JAM {i}");
                Assert.That(session.Score.Counters["jam-progress"], Is.EqualTo(expected.GetProperty("progress").GetInt32()), $"progress {i}");
                Assert.That(session.Score.Counters["pills"], Is.EqualTo(expected.GetProperty("pills").GetInt32()), $"pills {i}");
                Assert.That(session.Score.Counters["cool-streak"], Is.EqualTo(expected.GetProperty("streak").GetInt32()), $"streak {i}");
                Assert.That(session.Health.Value, Is.EqualTo(expected.GetProperty("life").GetInt32() / 36000.0).Within(1e-10), $"life {i}");
                Assert.That(session.Score.Combo, Is.EqualTo(expected.GetProperty("combo").GetInt32()), $"combo {i}");
            }
        }
    }

    [Test]
    public void LazerScoreMatchesExecutedUpstreamSequence()
    {
        using var stream = typeof(ProfileConformanceTest).Assembly.GetManifestResourceStream("RhythmEngine.Tests.Reference.lazer-score.json")!;
        using var data = JsonDocument.Parse(stream);
        var rows = data.RootElement.EnumerateArray().ToArray();
        var chart = new KeyChart(1, rows.Select((_, i) => note(i, (i + 1) * 1000)));
        var session = ManiaProfiles.CreateLazer(chart).CreateRecordingSession();
        var offsets = new long?[] { 0, 20, 60, 90, 110, null, 0, 0 };

        for (var i = 0; i < rows.Length; i++)
        {
            var time = (i + 1) * 1000;
            var input = offsets[i] is { } offset
                ? new[] { press(time + offset), release(time + offset + 1) }
                : [];
            session.AdvanceTo(ms(time + 500), input);

            var expected = rows[i];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(session.GetEventsSince(i).Single().Result.Value, Is.EqualTo(expected.GetProperty("result").GetString()), $"result {i}");
                Assert.That(session.Score.Score, Is.EqualTo(expected.GetProperty("score").GetDouble()).Within(1), $"score {i}");
                Assert.That(session.Score.Accuracy, Is.EqualTo(expected.GetProperty("accuracy").GetDouble()).Within(1e-10), $"accuracy {i}");
                Assert.That(session.Score.Combo, Is.EqualTo(expected.GetProperty("combo").GetInt32()), $"combo {i}");
            }
        }
    }

    [TestCase(KeyNoteKind.ReleaseHold)]
    [TestCase(KeyNoteKind.SustainHold)]
    public void BeatorajaMissedHeadResolvesBothChargeEndpointsAtHeadTimeout(KeyNoteKind kind)
    {
        var session = BmsProfiles.CreateBeatoraja(new KeyChart(1, [hold(kind)])).CreateRecordingSession();
        session.AdvanceTo(ms(1210) + 1);
        Assert.That(session.GetEventsSince(0).Select(e => (e.Part.Value, e.Result.Value, e.Time)),
            Is.EqualTo([("head", "poor", ms(1210) + 1), ("tail", "poor", ms(1210) + 1)]));
        Assert.That(session.Score.AccuracyMaximum, Is.EqualTo(4));
    }

    [TestCase(KeyNoteKind.Hold, "bad")]
    [TestCase(KeyNoteKind.ReleaseHold, "poor")]
    [TestCase(KeyNoteKind.SustainHold, "poor")]
    public void BeatorajaVeryEarlyReleaseHasModeSpecificResult(KeyNoteKind kind, string result)
    {
        var session = BmsProfiles.CreateBeatoraja(new KeyChart(1, [hold(kind)])).CreateRecordingSession();
        session.AdvanceTo(ms(1500), [press(1000), release(1500)]);
        Assert.That(session.GetEventsSince(0).Last().Result.Value, Is.EqualTo(result));
    }

    [Test]
    public void BeatorajaLnCarriesTheWorseEndpointErrorAgainstItsHead()
    {
        var session = BmsProfiles.CreateBeatoraja(new KeyChart(1, [hold(KeyNoteKind.Hold)])).CreateRecordingSession();
        session.AdvanceTo(ms(2000), [press(1040), release(1990)]);
        var result = session.GetEventsSince(0).Last();
        Assert.That((result.Result.Value, result.ExpectedTime, result.Offset), Is.EqualTo(("great", ms(1000), (long?)ms(40))));
    }

    [TestCase(true, "mine-hit")]
    [TestCase(false, "mine-avoided")]
    public void BeatorajaMineSamplesStateAfterEqualTimeEdges(bool pressed, string expected)
    {
        var session = BmsProfiles.CreateBeatoraja(new KeyChart(1, [note(0, 1000) with { Kind = KeyNoteKind.Mine }])).CreateRecordingSession();
        session.AdvanceTo(ms(1000), pressed ? [press(1000)] : [press(999), release(1000)]);
        Assert.That(session.GetEventsSince(0).Single().Result.Value, Is.EqualTo(expected));
    }

    [Test]
    public void Lr2BadLongNoteHeadResolvesImmediately()
    {
        var session = BmsProfiles.CreateLr2(new KeyChart(1, [hold(KeyNoteKind.Hold)])).CreateRecordingSession();
        session.AdvanceTo(ms(1150), [press(1150)]);
        Assert.That(session.Score.AccuracyMaximum, Is.EqualTo(2));
        Assert.That(session.Completed, Is.True);
        Assert.That(session.GetEventsSince(0).Last().Result.Value, Is.EqualTo("bad"));
    }

    [Test]
    public void LazerBodyBreakResetsComboImmediatelyWithoutAccuracyPenalty()
    {
        var session = ManiaProfiles.CreateLazer(new KeyChart(1, [hold(KeyNoteKind.Hold)])).CreateRecordingSession();
        session.AdvanceTo(ms(1500), [press(1000), release(1500)]);
        Assert.That(session.GetEventsSince(0).Select(e => e.Result.Value), Is.EqualTo(["perfect", "combo-break"]));
        Assert.That(session.Score.Combo, Is.Zero);
        Assert.That(session.Score.AccuracyMaximum, Is.EqualTo(305));
        session.AdvanceTo(ms(2000), [press(1700), release(2000)]);
        Assert.That(session.GetEventsSince(0).Last().Result.Value, Is.EqualTo("meh"));
        Assert.That(session.Score.Combo, Is.EqualTo(1));
    }

    [Test]
    public void LazerMissedHeadCanBeRecoveredWithoutSpuriousBodyBreak()
    {
        var session = ManiaProfiles.CreateLazer(new KeyChart(1, [hold(KeyNoteKind.Hold)])).CreateRecordingSession();
        session.AdvanceTo(ms(2000), [press(1500), release(2000)]);
        Assert.That(session.GetEventsSince(0).Select(e => e.Result.Value), Is.EqualTo(["miss", "meh"]));
    }

    [Test]
    public void LazerForbidsRecoveryWithinTailOnlyLenience()
    {
        var session = ManiaProfiles.CreateLazer(new KeyChart(1, [hold(KeyNoteKind.Hold)])).CreateRecordingSession();
        session.AdvanceTo(ms(2500), [press(1000), release(1500), press(2140), release(2150)]);
        Assert.That(session.GetEventsSince(0).Last().Result.Value, Is.EqualTo("miss"));
    }

    [Test]
    public void LazerNoteLockRetiresPreviousNoteWhenNextNoteIsHit()
    {
        var source = new KeyChart(1, [note(0, 1000), note(1, 1050)]);
        var session = ManiaProfiles.CreateLazer(source).CreateRecordingSession();
        session.AdvanceTo(ms(1050), [press(1050)]);
        Assert.That(session.GetEventsSince(0).Select(e => (e.NoteId, e.Result.Value)), Is.EqualTo([(0, "miss"), (1, "perfect")]));
    }

    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(8)]
    [TestCase(9)]
    public void EtternaKeepsTheFull180MsW5Window(int judge)
    {
        var session = EtternaProfile.Create(new KeyChart(1, [note(0, 1000)]), new EtternaOptions(judge)).CreateRecordingSession();
        session.AdvanceTo(ms(1180), [press(1180)]);
        Assert.That(session.GetEventsSince(0).Single().Result.Value, Is.EqualTo("w5"));
        Assert.That(session.Score.AccuracyPoints, Is.EqualTo(-5.5));
    }

    [Test]
    public void EtternaMissedHoldCarriesWifePenaltyAtTheTail()
    {
        var session = EtternaProfile.Create(new KeyChart(1, [hold(KeyNoteKind.Hold)])).CreateRecordingSession();
        session.AdvanceTo(ms(2500));
        Assert.That(session.GetEventsSince(0).Select(e => (e.Result.Value, e.Time)), Is.EqualTo([("miss", ms(1180) + 1), ("missed-hold", ms(2000))]));
        Assert.That(session.Score.AccuracyPoints, Is.EqualTo(-10));
    }

    [Test]
    public void EtternaRollReleaseCannotExtendRetriggerDeadline()
    {
        var session = EtternaProfile.Create(new KeyChart(1, [hold(KeyNoteKind.Roll)])).CreateRecordingSession();
        session.AdvanceTo(ms(1800), [press(1000), release(1499)]);
        Assert.That(session.GetEventsSince(0).Last().Time, Is.EqualTo(ms(1500)));
        Assert.That(session.GetEventsSince(0).Last().Result.Value, Is.EqualTo("let-go"));
    }

    [Test]
    public void EtternaRollRetriggersAlsoJudgeNearbyTaps()
    {
        var session = EtternaProfile.Create(new KeyChart(1, [hold(KeyNoteKind.Roll), note(1, 1400)])).CreateRecordingSession();
        session.AdvanceTo(ms(2100), [press(1000), release(1300), press(1400), release(1700), press(1800)]);
        Assert.That(session.GetEventsSince(0).Select(e => (e.NoteId, e.Result.Value)), Is.EqualTo([(0, "w1"), (1, "w1"), (0, "held")]));
    }

    [Test]
    public void EtternaActiveHoldBlocksTapBeyond135MsWithoutConsumingIt()
    {
        var session = EtternaProfile.Create(new KeyChart(1, [hold(KeyNoteKind.Hold), note(1, 1500)])).CreateRecordingSession();
        session.AdvanceTo(ms(1350), [press(1000), release(1300), press(1350)]);
        Assert.That(session.GetEventsSince(0).Count(e => e.NoteId == 1), Is.Zero);
    }

    [Test]
    public void LazerTimeoutFinalizesTheUnbrokenBodyAsComboBreak()
    {
        var session = ManiaProfiles.CreateLazer(new KeyChart(1, [hold(KeyNoteKind.Hold)])).CreateRecordingSession();
        session.AdvanceTo(ms(2500), [press(1000)]);
        Assert.That(session.GetEventsSince(0).Select(e => e.Result.Value), Is.EqualTo(["perfect", "miss", "combo-break"]));
    }

    [TestCase(0.5, 0.004)]
    [TestCase(1, 0.008)]
    [TestCase(2, 0.016)]
    public void EtternaLifeDifficultyScalesRecovery(double difficulty, double increase)
    {
        var session = EtternaProfile.Create(new KeyChart(1, [note(0, 1000)]), new EtternaOptions(LifeDifficulty: difficulty)).CreateRecordingSession();
        session.AdvanceTo(ms(1100), [press(1000)]);
        Assert.That(session.Health.Value, Is.EqualTo(0.5 + increase).Within(1e-7));
    }

    [Test]
    public void EtternaHeldHoldUsesGameplayLifeChangeAtTail()
    {
        var session = EtternaProfile.Create(new KeyChart(1, [hold(KeyNoteKind.Hold)])).CreateRecordingSession();
        session.AdvanceTo(ms(3000), [press(1000), release(1400), press(1649)]);

        Assert.That(session.GetEventsSince(0).Last().Result.Value, Is.EqualTo("held"));
        Assert.That(session.Health.Value, Is.EqualTo(0.516).Within(1e-7));
    }

    [Test]
    public void EtternaMineSamplesAnExistingHoldAtItsChartTime()
    {
        var source = new KeyChart(1, [note(0, 1000) with { Kind = KeyNoteKind.Mine }]);
        var session = EtternaProfile.Create(source).CreateRecordingSession();
        session.AdvanceTo(ms(999), [press(900)]);
        Assert.That(session.EventCount, Is.Zero);
        session.AdvanceTo(ms(1000));
        Assert.That(session.GetEventsSince(0).Single().Time, Is.EqualTo(ms(1000)));
        Assert.That(session.GetEventsSince(0).Single().Result.Value, Is.EqualTo("mine-hit"));
    }

    [Test]
    public void EtternaReleaseBeforeMineAndPressAfterItsWindowAvoidIt()
    {
        var source = new KeyChart(1, [note(0, 1000) with { Kind = KeyNoteKind.Mine }]);
        var session = EtternaProfile.Create(source).CreateRecordingSession();
        session.AdvanceTo(ms(1200), [press(900), release(950), new InputEvent(ms(1075) + 1, 0, true)]);
        Assert.That(session.GetEventsSince(0).Single().Result.Value, Is.EqualTo("mine-avoided"));
        Assert.That(session.Score.AccuracyPoints, Is.Zero);
    }

    [TestCase(-75, "mine-hit")]
    [TestCase(75, "mine-hit")]
    [TestCase(75.001, "mine-avoided")]
    public void EtternaMinePressUsesInclusive75MsEdges(double offset, string result)
    {
        var source = new KeyChart(1, [note(0, 1000) with { Kind = KeyNoteKind.Mine }]);
        var session = EtternaProfile.Create(source).CreateRecordingSession();
        session.AdvanceTo(ms(1200), [press(1000 + offset)]);
        Assert.That(session.GetEventsSince(0).Single().Result.Value, Is.EqualTo(result));
    }

    [Test]
    public void EtternaNearestTapPreventsAnEarlyMineHitFromTheSamePress()
    {
        var source = new KeyChart(1, [note(0, 1000), note(1, 1060) with { Kind = KeyNoteKind.Mine }]);
        var session = EtternaProfile.Create(source).CreateRecordingSession();
        session.AdvanceTo(ms(1300), [press(1000), release(1001)]);
        Assert.That(session.GetEventsSince(0).Select(e => (e.NoteId, e.Result.Value)), Is.EqualTo([(0, "w1"), (1, "mine-avoided")]));
    }

    [Test]
    public void EtternaFutureMinePrecedesAPassedMineWhenBothAreInSearchRange()
    {
        var source = new KeyChart(1, [note(0, 1000) with { Kind = KeyNoteKind.Mine }, note(1, 1050) with { Kind = KeyNoteKind.Mine }]);
        var session = EtternaProfile.Create(source).CreateRecordingSession();
        session.AdvanceTo(ms(1200), [press(1020), release(1030)]);
        Assert.That(session.GetEventsSince(0).Select(e => (e.NoteId, e.Result.Value)), Is.EqualTo([(1, "mine-hit"), (0, "mine-avoided")]));
    }

    [Test]
    public void EtternaFutureMineOutsideItsHitWindowStillBlocksAPassedMine()
    {
        var source = new KeyChart(1, [note(0, 1000) with { Kind = KeyNoteKind.Mine }, note(1, 1150) with { Kind = KeyNoteKind.Mine }]);
        var session = EtternaProfile.Create(source).CreateRecordingSession();
        session.AdvanceTo(ms(1400), [press(1020), release(1030)]);
        Assert.That(session.GetEventsSince(0).Select(e => e.Result.Value), Is.All.EqualTo("mine-avoided"));
        Assert.That(session.EventCount, Is.EqualTo(2));
    }

    [Test]
    public void EtternaEquidistantTapsChooseTheFutureRow()
    {
        var session = EtternaProfile.Create(new KeyChart(1, [note(0, 1000), note(1, 1100)])).CreateRecordingSession();
        session.AdvanceTo(ms(1050), [press(1050)]);
        Assert.That(session.GetEventsSince(0).Single().NoteId, Is.EqualTo(1));
    }

    [Test]
    public void StableHoldCanRecoverAReleaseBeforeTheEarlyMehWindow()
    {
        var session = ManiaProfiles.CreateStable(new KeyChart(1, [hold(KeyNoteKind.Hold)])).CreateRecordingSession();
        session.AdvanceTo(ms(1850), [press(1000), release(1850)]);
        Assert.That(session.Completed, Is.False);
        session.AdvanceTo(ms(2500), [press(1900), release(2000)]);
        Assert.That(session.GetEventsSince(0).Last().Result.Value, Is.EqualTo("meh"));
    }

    [Test]
    public void Open2JamContinuesAfterTheLifeBarEmpties()
    {
        var session = O2JamProfile.Create(new KeyChart(1, Enumerable.Range(0, 30).Select(i => note(i, 1000 + i * 1000)))).CreateRecordingSession();
        session.AdvanceTo(ms(40000));
        Assert.That(session.Health, Is.EqualTo(new HealthState(0, false)));
        Assert.That(session.Cleared, Is.True);
    }

    private static long ms(double value) => Time.FromMilliseconds(value);

    private static KeyNote note(int id, double time) => new(id, 0, ms(time), ms(time));

    private static KeyNote hold(KeyNoteKind kind) => new(0, 0, ms(1000), ms(2000), kind);

    private static InputEvent press(double time) => new(ms(time), 0, true);

    private static InputEvent release(double time) => new(ms(time), 0, false);
}
