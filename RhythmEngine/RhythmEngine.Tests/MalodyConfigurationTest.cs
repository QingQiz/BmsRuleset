using System.Collections.Immutable;
using NUnit.Framework;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Malody;
using RhythmEngine.Profiles.Patterns;

namespace RhythmEngine.Tests;

[TestFixture]
public class MalodyConfigurationTest
{
    [Test]
    public void EveryHoldRequiresPositiveDuration([Values] MalodyClient client, [Values(0, -1)] int duration)
    {
        var source = new KeyChart(1,
        [
            new KeyNote(0, 0, ms(1000), ms(2000), KeyNoteKind.Hold),
            new KeyNote(1, 0, ms(3000), ms(3000 + duration), KeyNoteKind.Hold),
        ]);
        Assert.Throws<ArgumentException>(() => MalodyProfile.Create(source, new MalodyOptions(Client: client)));
    }

    [TestCase(MalodyJudge.Amateur, 56, 96, 130, 1560)]
    [TestCase(MalodyJudge.Beginner, 46, 86, 120, 1700)]
    [TestCase(MalodyJudge.Climber, 36, 76, 110, 2000)]
    [TestCase(MalodyJudge.Dancer, 28, 68, 102, 2200)]
    [TestCase(MalodyJudge.Expert, 20, 60, 94, 2400)]
    public void AllPublishedPcJudgeTablesAndMultipliers(MalodyJudge judge, double best, double cool, double good, double perfectScore)
    {
        var windows = MalodyProfile.PcWindows(judge);
        Assert.That(windows.Entries.Take(3).Select(w => Time.ToMilliseconds(w.Late)), Is.EqualTo([best, cool, good]));
        var profile = MalodyProfile.Create(tapChart(), new MalodyCalibration(testHealth()), new MalodyOptions(Judge: judge));
        var session = profile.CreateRecordingSession();
        session.AdvanceTo(ms(2000), [new InputEvent(ms(1000), 0, true)]);
        Assert.That(session.Score.Score, Is.EqualTo(perfectScore).Within(1e-7));
        Assert.That(session.MaximumScore, Is.EqualTo(perfectScore).Within(1e-7));
    }

    [TestCase(MalodyJudge.Amateur, 56, 96, 130)]
    [TestCase(MalodyJudge.Beginner, 46, 86, 120)]
    [TestCase(MalodyJudge.Climber, 36, 76, 110)]
    [TestCase(MalodyJudge.Dancer, 28, 68, 102)]
    [TestCase(MalodyJudge.Expert, 20, 60, 94)]
    public void PcRankWindowsMatchOriginalBinaryAtGradeBoundaries(MalodyJudge judge, int best, int cool, int good)
    {
        foreach (var (boundary, expected, after) in new[]
                 {
                     (best, "best", "cool"),
                     (cool, "cool", "good"),
                     (good, "good", "miss"),
                 })
        {
            Assert.That(judgeResult(judge, boundary), Is.EqualTo(expected), $"+/-{boundary}ms boundary");
            Assert.That(judgeResult(judge, -boundary), Is.EqualTo(expected), $"-/+{boundary}ms boundary");
            Assert.That(judgeResult(judge, boundary + 1), Is.EqualTo(after), $"+{boundary + 1}ms");
            Assert.That(judgeResult(judge, -(boundary + 1)), Is.EqualTo(after), $"-{boundary + 1}ms");
        }
    }

    [TestCase(MalodyClient.ClassicPc)]
    [TestCase(MalodyClient.VPro)]
    public void ProAndLegacyPcUseThePublishedPcTable(MalodyClient client)
    {
        var session = MalodyProfile.Create(tapChart(), new MalodyCalibration(testHealth()), new MalodyOptions(Client: client)).CreateRecordingSession();
        session.AdvanceTo(ms(2000), [new InputEvent(ms(1040), 0, true)]);
        Assert.That(session.GetEventsSince(0).Single().Result.Value, Is.EqualTo("cool"));
        Assert.That(session.Score.Score, Is.EqualTo(1485));
        Assert.That(session.Score.Accuracy, Is.EqualTo(0.75));
    }

    [TestCase(MalodyClient.VStandard)]
    public void VStandardProvidesDefaultWindowsAndAcceptsExplicitCalibration(MalodyClient client)
    {
        Assert.That(MalodyProfile.VWindows(MalodyJudge.Climber, false).Entries[0].Late, Is.EqualTo(ms(45)));
        var measuredWindows = new TimingWindows([
            new TimingWindow(new ResultId("best"), -50000, 50000), new TimingWindow(new ResultId("cool"), -80000, 80000),
            new TimingWindow(new ResultId("good"), -120000, 120000), new TimingWindow(new ResultId("miss"), -150000, 150000)
        ]);
        var calibration = new MalodyCalibration(testHealth(), ImmutableDictionary<MalodyJudge, TimingWindows>.Empty.Add(MalodyJudge.Climber, measuredWindows));
        var profile = MalodyProfile.Create(tapChart(), calibration, new MalodyOptions(Client: client));
        var calibrated = profile.CreateRecordingSession();
        calibrated.AdvanceTo(ms(1049), [new InputEvent(ms(1049), 0, true)]);
        Assert.That(calibrated.GetEventsSince(0).First().Result.Value, Is.EqualTo("best"));
    }

    [Test]
    public void ScoreUsesUpdatedBoundedBonusAndHoldDropKeepsAccuracyDenominator()
    {
        var notes = new KeyChart(1, [new KeyNote(0, 0, ms(1000), ms(1000)), new KeyNote(1, 0, ms(2000), ms(3000), KeyNoteKind.Hold)]);
        var session = MalodyProfile.Create(notes, new MalodyCalibration(testHealth())).CreateRecordingSession();
        session.AdvanceTo(ms(4000), [new InputEvent(ms(2000), 0, true), new InputEvent(ms(2500), 0, false)]);
        Assert.That(session.GetEventsSince(0).Where(e => e.Result.Value != "hold-tick").Select(e => e.Result.Value), Is.EqualTo(["miss", "best", "hold-drop"]));
        Assert.That(session.Score.Score, Is.EqualTo(1690).Within(1e-7));
        Assert.That(session.Score.AccuracyMaximum, Is.EqualTo(2000));
        Assert.That(session.Score.Bonus, Is.EqualTo(0.37).Within(1e-7));
        Assert.That(session.Score.Combo, Is.Zero);
    }

    [TestCase(MalodyJudge.Amateur)]
    [TestCase(MalodyJudge.Beginner)]
    [TestCase(MalodyJudge.Climber)]
    [TestCase(MalodyJudge.Dancer)]
    [TestCase(MalodyJudge.Expert)]
    public void PcBonusUsesTheSameIntegerChangesAtEveryRank(MalodyJudge judge)
    {
        var singleTap = tapChart();
        var windows = MalodyProfile.PcWindows(judge);
        var goodOffset = (Time.ToMilliseconds(windows.Entries[1].Late) + Time.ToMilliseconds(windows.Entries[2].Late)) / 2;
        var goodSession = MalodyProfile.Create(singleTap, new MalodyCalibration(testHealth()), new MalodyOptions(Judge: judge)).CreateRecordingSession();
        goodSession.AdvanceTo(ms(1500), [new InputEvent(ms(1000 + goodOffset), 0, true)]);
        Assert.That(goodSession.GetEventsSince(0).Single().Result.Value, Is.EqualTo("good"));
        Assert.That(goodSession.Score.Bonus, Is.EqualTo(0.92).Within(1e-12));

        var missSession = MalodyProfile.Create(singleTap, new MalodyCalibration(testHealth()), new MalodyOptions(Judge: judge)).CreateRecordingSession();
        missSession.AdvanceTo(ms(1500));
        Assert.That(missSession.GetEventsSince(0).Single().Result.Value, Is.EqualTo("miss"));
        Assert.That(missSession.Score.Bonus, Is.EqualTo(0.68).Within(1e-12));
    }

    [Test]
    public void BonusChangesBeforeMixedJudgementScoring()
    {
        var chart = new KeyChart(1, Enumerable.Range(0, 4).Select(i => new KeyNote(i, 0, ms((i + 1) * 1000), ms((i + 1) * 1000))));
        var session = MalodyProfile.Create(chart, new MalodyCalibration(testHealth()), new MalodyOptions(Judge: MalodyJudge.Climber)).CreateRecordingSession();
        session.AdvanceTo(ms(1500), [new InputEvent(ms(1100), 0, true), new InputEvent(ms(1101), 0, false)]);
        Assert.That(session.GetEventsSince(0).Last().Result.Value, Is.EqualTo("good"));
        Assert.That(session.Score.Bonus, Is.EqualTo(0.92).Within(1e-12));
        Assert.That(session.Score.Score, Is.EqualTo(768).Within(1e-9));

        session.AdvanceTo(ms(2500), [new InputEvent(ms(2050), 0, true), new InputEvent(ms(2051), 0, false)]);
        Assert.That(session.GetEventsSince(1).Last().Result.Value, Is.EqualTo("cool"));
        Assert.That(session.Score.Bonus, Is.EqualTo(0.90).Within(1e-12));
        Assert.That(session.Score.Score, Is.EqualTo(2193).Within(1e-9));

        session.AdvanceTo(ms(3500), [new InputEvent(ms(3100), 0, true), new InputEvent(ms(3101), 0, false)]);
        Assert.That(session.GetEventsSince(2).Last().Result.Value, Is.EqualTo("good"));
        Assert.That(session.Score.Bonus, Is.EqualTo(0.82).Within(1e-12));
        Assert.That(session.Score.Score, Is.EqualTo(2921).Within(1e-9));

        session.AdvanceTo(ms(4500));
        Assert.That(session.GetEventsSince(3).Last().Result.Value, Is.EqualTo("miss"));
        Assert.That(session.Score.Bonus, Is.EqualTo(0.50).Within(1e-12));
        Assert.That(session.Score.Score, Is.EqualTo(2921).Within(1e-9));
    }

    [TestCase(MalodyJudge.Amateur, 65, 105, 150)]
    [TestCase(MalodyJudge.Beginner, 55, 95, 140)]
    [TestCase(MalodyJudge.Climber, 45, 85, 130)]
    [TestCase(MalodyJudge.Dancer, 37, 77, 122)]
    [TestCase(MalodyJudge.Expert, 29, 69, 114)]
    public void Android437TablesMatchArm64ConstructorAndRankSetter(MalodyJudge judge, int best, int cool, int good)
    {
        var profile = MalodyProfile.Create(tapChart(), new MalodyOptions(Client: MalodyClient.ClassicMobile, Judge: judge));
        var windows = ((TapRule)profile.Chart.Notes[0].Rule).Configuration.Windows;
        Assert.That(windows.Entries.Take(3).Select(w => Time.ToMilliseconds(w.Late)), Is.EqualTo([best, cool, good]));

        foreach (var sign in new[] { -1, 1 })
        {
            Assert.That(windows.ResultFor(ms(sign * best))?.Value, Is.EqualTo("best"));
            Assert.That(windows.ResultFor(ms(sign * (best + 1)))?.Value, Is.EqualTo("cool"));
            Assert.That(windows.ResultFor(ms(sign * cool))?.Value, Is.EqualTo("cool"));
            Assert.That(windows.ResultFor(ms(sign * (cool + 1)))?.Value, Is.EqualTo("good"));
            Assert.That(windows.ResultFor(ms(sign * good))?.Value, Is.EqualTo("good"));
            Assert.That(windows.ResultFor(ms(sign * (good + 1)))?.Value, Is.EqualTo("miss"));
        }
    }

    [TestCase(MalodyJudge.Amateur)]
    [TestCase(MalodyJudge.Beginner)]
    [TestCase(MalodyJudge.Climber)]
    [TestCase(MalodyJudge.Dancer)]
    [TestCase(MalodyJudge.Expert)]
    public void HealthAndBonusHaveSeparateRankScaling(MalodyJudge judge)
    {
        var source = new KeyChart(1, Enumerable.Range(0, 4).Select(i => new KeyNote(i, 0, ms((i + 1) * 1000), ms((i + 1) * 1000))));
        var session = MalodyProfile.Create(source, new MalodyOptions(Judge: judge)).CreateRecordingSession();
        var scale = (int)judge + 1;
        var windows = MalodyProfile.PcWindows(judge);
        session.AdvanceTo(ms(1500));
        Assert.That(session.Health.Value, Is.EqualTo(1 - 0.05 * scale).Within(1e-7));
        Assert.That(session.Score.Bonus, Is.EqualTo(0.68));
        session.AdvanceTo(ms(2500), [new InputEvent(ms(2000) + windows.Entries[1].Late + ms(1), 0, true), new InputEvent(ms(2400), 0, false)]);
        Assert.That(session.Health.Value, Is.EqualTo(1 - 0.07 * scale).Within(1e-7));
        Assert.That(session.Score.Bonus, Is.EqualTo(0.60));
        session.AdvanceTo(ms(3500), [new InputEvent(ms(3000) + windows.Entries[0].Late + ms(1), 0, true), new InputEvent(ms(3400), 0, false)]);
        Assert.That(session.Health.Value, Is.EqualTo(1 - 0.07 * scale + 0.004).Within(1e-7));
        Assert.That(session.Score.Bonus, Is.EqualTo(0.58));
        session.AdvanceTo(ms(4500), [new InputEvent(ms(4000), 0, true)]);
        Assert.That(session.Health.Value, Is.EqualTo(1 - 0.07 * scale + 0.016).Within(1e-7));
        Assert.That(session.Score.Bonus, Is.EqualTo(0.59));
    }

    [TestCase(MalodyClient.ClassicPc, 110)]
    [TestCase(MalodyClient.ClassicMobile, 130)]
    public void ClassicReleaseLenienceAndBodyTicksUseDifferentClocks(MalodyClient client, int lenience)
    {
        var source = new KeyChart(1, [new KeyNote(0, 0, ms(1000), ms(2000), KeyNoteKind.Hold)]);
        var profile = MalodyProfile.Create(source, new MalodyOptions(Client: client));
        var early = profile.CreateRecordingSession();
        early.AdvanceTo(ms(2200), [new InputEvent(ms(1000), 0, true), new InputEvent(ms(2000 - lenience) - 1, 0, false)]);
        Assert.That(early.GetEventsSince(0).Last().Result.Value, Is.EqualTo("hold-drop"));
        Assert.That(early.Score.Bonus, Is.EqualTo(0.68));

        var atBoundary = profile.CreateRecordingSession();
        atBoundary.AdvanceTo(ms(2200), [new InputEvent(ms(1000), 0, true), new InputEvent(ms(2000 - lenience), 0, false)]);
        Assert.That(atBoundary.GetEventsSince(0).Any(e => e.Result.Value == "hold-drop"), Is.False);
        Assert.That(atBoundary.Score.Score, Is.EqualTo(2000));
        Assert.That(atBoundary.Score.AccuracyMaximum, Is.EqualTo(1000));
        Assert.That(atBoundary.Score.Combo, Is.EqualTo(1 + (1000 - lenience) / 100));
    }

    [Test]
    public void ClassicHoldCanRegrabAfterEachDropWithoutScoringItsHeadAgain()
    {
        var source = new KeyChart(1, [new KeyNote(0, 0, ms(1000), ms(2000), KeyNoteKind.Hold)]);
        var session = MalodyProfile.Create(source).CreateRecordingSession();
        session.AdvanceTo(ms(1300), [new InputEvent(ms(1000), 0, true), new InputEvent(ms(1250), 0, false)]);
        Assert.That(session.Score.Combo, Is.Zero);
        Assert.That(session.Completed, Is.False);
        var clone = session.Clone();
        InputEvent[] regrab = [new(ms(1400), 0, true)];
        session.AdvanceTo(ms(2200), regrab);
        clone.AdvanceTo(ms(1550), regrab);
        clone.AdvanceTo(ms(2200));
        Assert.That(session.GetEventsSince(0).Count(e => e.Part.Value == "head"), Is.EqualTo(1));
        Assert.That(session.GetEventsSince(0).Where(e => e.Result.Value == "hold-tick").Select(e => Time.ToMilliseconds(e.Time)),
            Is.EqualTo(new double[] { 1100, 1200, 1500, 1600, 1700, 1800, 1900, 2000 }));
        Assert.That(session.Score.Combo, Is.EqualTo(6));
        Assert.That(session.Score.MaximumCombo, Is.EqualTo(6));
        Assert.That(session.Score.Score, Is.EqualTo(2000));
        Assert.That(session.Score.Bonus, Is.EqualTo(0.68));
        Assert.That(session.Score.AccuracyMaximum, Is.EqualTo(1000));
        Assert.That(clone.GetEventsSince(0), Is.EqualTo(session.GetEventsSince(0)));
        Assert.That(session.Completed, Is.True);
    }

    [TestCase(MalodyClient.VStandard, 45, 85, 130)]
    [TestCase(MalodyClient.VPro, 36, 76, 110)]
    public void VUsesStrictGradeComparisons(MalodyClient client, int best, int cool, int good)
    {
        var windows = MalodyProfile.VWindows(MalodyJudge.Climber, client == MalodyClient.VPro);

        foreach (var sign in new[] { -1, 1 })
        {
            Assert.That(windows.ResultFor(ms(sign * (best - 1)))?.Value, Is.EqualTo("best"));
            Assert.That(windows.ResultFor(ms(sign * best))?.Value, Is.EqualTo("cool"));
            Assert.That(windows.ResultFor(ms(sign * cool))?.Value, Is.EqualTo("good"));
            Assert.That(windows.ResultFor(ms(sign * good))?.Value, Is.EqualTo("miss"));
        }
    }

    [TestCase(MalodyClient.VStandard)]
    [TestCase(MalodyClient.VPro)]
    public void VEarlyMissCanBeDisabledWithoutLosingLaterPerfectInput(MalodyClient client)
    {
        var session = MalodyProfile.Create(tapChart(), new MalodyOptions(Client: client, AllowEarlyMiss: false, VVersion: MalodyVVersion.Rc20)).CreateRecordingSession();
        session.AdvanceTo(ms(850), [new InputEvent(ms(850), 0, true)]);
        Assert.That(session.EventCount, Is.Zero);
        session.AdvanceTo(ms(1000), [new InputEvent(ms(900), 0, false), new InputEvent(ms(1000), 0, true)]);
        Assert.That(session.GetEventsSince(0).Single().Result.Value, Is.EqualTo("best"));
    }

    [TestCase(MalodyClient.VStandard, 130)]
    [TestCase(MalodyClient.VPro, 110)]
    public void Rc20FlushesAllBodyTicksBeforeAReleaseJudgement(MalodyClient client, int lenience)
    {
        var source = new KeyChart(1, [new KeyNote(0, 0, ms(1000), ms(2000), KeyNoteKind.Hold)]);
        var session = MalodyProfile.Create(source, new MalodyOptions(Client: client, VVersion: MalodyVVersion.Rc20)).CreateRecordingSession();
        session.AdvanceTo(ms(2000 - lenience), [new InputEvent(ms(1000), 0, true), new InputEvent(ms(2000 - lenience), 0, false)]);
        Assert.That(session.Completed, Is.True);
        Assert.That(session.Score.Combo, Is.EqualTo(11));
        Assert.That(session.Score.Score, Is.EqualTo(2000));
        Assert.That(session.GetEventsSince(0).Last().ExpectedTime, Is.EqualTo(ms(2000)));
        Assert.That(session.GetEventsSince(0).Last().Time, Is.EqualTo(ms(2000 - lenience)));
    }

    [Test]
    public void ClassicHeadMissCanRegrabForBodyComboWithoutChangingAccuracy()
    {
        var source = new KeyChart(1, [new KeyNote(0, 0, ms(1000), ms(2000), KeyNoteKind.Hold)]);
        var session = MalodyProfile.Create(source).CreateRecordingSession();
        session.AdvanceTo(ms(2100), [new InputEvent(ms(1400), 0, true)]);
        Assert.That(session.GetEventsSince(0).First().Result.Value, Is.EqualTo("miss"));
        Assert.That(session.Score.Combo, Is.EqualTo(6));
        Assert.That(session.Score.Score, Is.Zero);
        Assert.That(session.Score.AccuracyMaximum, Is.EqualTo(1000));
    }

    [Test]
    public void VDisplaysTheFloorOfAccumulatedFractionalScore()
    {
        var source = new KeyChart(1, Enumerable.Range(0, 4).Select(i => new KeyNote(i, 0, ms((i + 1) * 1000), ms((i + 1) * 1000))));
        var session = MalodyProfile.Create(source, new MalodyOptions(Client: MalodyClient.VPro)).CreateRecordingSession();
        session.AdvanceTo(ms(3500),
        [
            new InputEvent(ms(1050), 0, true), new InputEvent(ms(1100), 0, false),
            new InputEvent(ms(2000), 0, true), new InputEvent(ms(2100), 0, false),
            new InputEvent(ms(3050), 0, true), new InputEvent(ms(3100), 0, false),
        ]);
        Assert.That(session.Score.Score, Is.EqualTo(4952));
        session.AdvanceTo(ms(4500), [new InputEvent(ms(4000), 0, true)]);
        Assert.That(session.Score.Score, Is.EqualTo(6932));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void EmptyHealthUsesTheConfiguredFailurePolicy(bool failOnEmpty)
    {
        var source = new KeyChart(1, Enumerable.Range(0, 30).Select(i => new KeyNote(i, 0, ms((i + 1) * 1000), ms((i + 1) * 1000))));
        var session = MalodyProfile.Create(source, new MalodyOptions(FailOnEmpty: failOnEmpty)).CreateRecordingSession();
        session.AdvanceTo(ms(31000));
        Assert.That(session.Health.Value, Is.Zero);
        Assert.That(session.Health.HasFailed, Is.EqualTo(failOnEmpty));
        Assert.That(session.Cleared, Is.EqualTo(!failOnEmpty));
    }

    [Test]
    public void CurrentVEndJudgementScoresBothHeadAndPromotedTail()
    {
        var source = new KeyChart(1, [new KeyNote(0, 0, ms(1000), ms(2000), KeyNoteKind.Hold)]);
        var session = MalodyProfile.Create(source, new MalodyOptions(Client: MalodyClient.VPro, JudgeAtEnd: true)).CreateRecordingSession();
        session.AdvanceTo(ms(1500), [new InputEvent(ms(1050), 0, true)]);
        Assert.That(session.Score.Score, Is.EqualTo(1485));
        Assert.That(session.Score.AccuracyMaximum, Is.EqualTo(1000));
        session.AdvanceTo(ms(2050), [new InputEvent(ms(2050), 0, false)]);
        Assert.That(session.GetEventsSince(0).Where(e => e.Part.Value is "head" or "tail").Select(e => e.Result.Value), Is.EqualTo(["cool", "best"]));
        Assert.That(session.Score.AccuracyMaximum, Is.EqualTo(2000));
        Assert.That(session.Completed, Is.True);
    }

    [Test]
    public void CurrentVMissingHeadAlsoMissesTheGradedTailAtTheGoodHorizon()
    {
        var source = new KeyChart(1, [new KeyNote(0, 0, ms(1000), ms(2000), KeyNoteKind.Hold)]);
        var session = MalodyProfile.Create(source, new MalodyOptions(Client: MalodyClient.VPro, JudgeAtEnd: true)).CreateRecordingSession();
        session.AdvanceTo(ms(1110));
        Assert.That(session.EventCount, Is.Zero);
        session.AdvanceTo(ms(1110) + 1);
        Assert.That(session.GetEventsSince(0).Select(e => (e.Part.Value, e.Result.Value)), Is.EqualTo([("head", "miss"), ("tail", "miss")]));
        Assert.That(session.Score.AccuracyMaximum, Is.EqualTo(2000));
        Assert.That(session.Health.Value, Is.EqualTo(0.7).Within(1e-7));
        Assert.That(session.Completed, Is.True);
    }

    [Test]
    public void CurrentVRequiresReleaseForGradedTailAndUsesUnscaledTimeout()
    {
        var source = new KeyChart(1, [new KeyNote(0, 0, ms(1000), ms(2000), KeyNoteKind.Hold)]);
        var session = MalodyProfile.Create(source, new MalodyOptions(Client: MalodyClient.VPro, JudgeAtEnd: true, WindowScale: 2)).CreateRecordingSession();
        session.AdvanceTo(ms(2160), [new InputEvent(ms(1000), 0, true)]);
        Assert.That(session.Completed, Is.False);
        Assert.That(session.GetEventsSince(0).Count(e => e.Part.Value == "tail"), Is.Zero);
        session.AdvanceTo(ms(2160) + 1);
        Assert.That(session.GetEventsSince(0).Last().Result.Value, Is.EqualTo("miss"));
        Assert.That(session.Completed, Is.True);
    }

    private static KeyChart tapChart() => new(1, [new KeyNote(0, 0, ms(1000), ms(1000))]);

    private static string judgeResult(MalodyJudge judge, double offset)
    {
        var session = MalodyProfile.Create(tapChart(), new MalodyCalibration(testHealth()), new MalodyOptions(Judge: judge)).CreateRecordingSession();
        session.AdvanceTo(ms(2000), [new InputEvent(ms(1000 + offset), 0, true)]);
        return session.GetEventsSince(0).Single().Result.Value;
    }

    private static long ms(double time) => Time.FromMilliseconds(time);

    // A deliberately supplied host gauge, not an assertion about Malody's unpublished HP table.
    private static HealthConfiguration testHealth() => HealthRules.LatchedGauge(0, 1, 1, _ => 0, _ => false, _ => true);
}
