using NUnit.Framework;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Etterna;
using RhythmEngine.Profiles.Malody;
using RhythmEngine.Profiles.Mania;
using RhythmEngine.Profiles.O2Jam;
using RhythmEngine.Profiles.Patterns;

namespace RhythmEngine.Tests;

[TestFixture]
public class ProfileBehaviourTest
{
    [Test]
    public void ManiaCompletionHonoursExplicitEndAcrossCloneAndReset([Values] bool stable, [Values] bool empty)
    {
        var source = new KeyChart(1, empty ? [] : [tap(0, 1000)], endTime: ms(3000));
        var profile = stable ? ManiaProfiles.CreateStable(source) : ManiaProfiles.CreateLazer(source);
        var session = new JudgementSession(profile.Chart, profile.Configuration);
        session.AdvanceTo(ms(1000), empty ? [] : [new InputEvent(ms(1000), 0, true)]);
        var clone = session.Clone();
        foreach (var current in new[] { session, clone })
        {
            current.AdvanceTo(ms(2999.999));
            Assert.That(current.Completed, Is.False);
            current.AdvanceTo(ms(3000));
            Assert.That(current.Completed, Is.True);
            current.Reset();
            Assert.That(current.Completed, Is.False);
        }
    }

    [TestCase("malody-pc")]
    [TestCase("malody-mobile")]
    [TestCase("malody-v")]
    [TestCase("malody-rc")]
    [TestCase("etterna")]
    public void ConcurrentHoldsRemainIndependentAcrossCloneAndReset(string profile)
    {
        var compiled = compile(profile, new KeyChart(2, [hold(0, 1000, 2000), hold(1, 1100, 2100) with { Lane = 1 }]));
        var session = compiled.CreateRecordingSession();
        InputEvent[] heads = [press(1000), new(ms(1100), 1, true)];
        session.AdvanceTo(ms(1200), heads);
        var clone = session.Clone();
        session.AdvanceTo(ms(1700), [release(1250)]);
        clone.AdvanceTo(ms(1700), [new(ms(1250), 1, false)]);

        Assert.That(session.GetNote(0).Parts.Single(p => p.Part == NoteParts.BODY).Active, Is.False);
        Assert.That(session.GetNote(1).Parts.Single(p => p.Part == NoteParts.BODY).Active, Is.True);
        Assert.That(clone.GetNote(0).Parts.Single(p => p.Part == NoteParts.BODY).Active, Is.True);
        Assert.That(clone.GetNote(1).Parts.Single(p => p.Part == NoteParts.BODY).Active, Is.False);
        session.AdvanceTo(ms(2500), [new(ms(2100), 1, false)]);
        clone.AdvanceTo(ms(2500), [release(2000)]);
        var expected = clone.GetEventsSince(0);
        Assert.That(session.Completed && clone.Completed, Is.True);
        Assert.That(session.GetEventsSince(0), Is.Not.EqualTo(expected));

        session.Reset();
        Assert.That(session.GetNote(0).Parts.All(p => !p.Resolved && !p.Active), Is.True);
        Assert.That(clone.GetEventsSince(0), Is.EqualTo(expected));
        session.AdvanceTo(ms(2500), [.. heads, new(ms(1250), 1, false), release(2000)]);
        Assert.That(session.GetEventsSince(0), Is.EqualTo(expected));
        Assert.That(session.Health, Is.EqualTo(clone.Health));
    }

    [TestCase("beatoraja", 4)]
    [TestCase("lr2", 4)]
    [TestCase("lazer", 1000000)]
    [TestCase("stable", 1000000)]
    [TestCase("o2jam", 600)]
    [TestCase("etterna", 100)]
    public void PerfectTapAndHoldReachAdvertisedPotential(string profile, double expectedScore)
    {
        var compiled = compile(profile, new KeyChart(1, [tap(0, 1000), hold(1, 2000, 3000)]));
        var session = compiled.CreateRecordingSession();
        session.AdvanceTo(ms(5000), [press(1000), release(1001), press(2000), release(3000)]);
        Assert.Multiple(() =>
        {
            Assert.That(session.Score.Score, Is.EqualTo(expectedScore).Within(1e-7));
            Assert.That(session.MaximumScore, Is.EqualTo(expectedScore).Within(1e-7));
            Assert.That(session.Score.Accuracy, Is.EqualTo(1).Within(1e-7));
            Assert.That(session.Completed, Is.True);
        });
    }

    [TestCase("beatoraja")]
    [TestCase("lr2")]
    [TestCase("lazer")]
    [TestCase("stable")]
    [TestCase("o2jam")]
    [TestCase("etterna")]
    public void AllMissesFinishEveryObject(string profile)
    {
        var session = compile(profile, new KeyChart(1, [tap(0, 1000), hold(1, 2000, 3000)])).CreateRecordingSession();
        session.AdvanceTo(ms(5000));
        Assert.That(session.Completed, Is.True);
        Assert.That(session.Score.Accuracy, Is.LessThanOrEqualTo(0));
    }

    [TestCase(-15000, "pgreat")]
    [TestCase(15000, "pgreat")]
    [TestCase(15001, "great")]
    [TestCase(-165000, "bad")]
    [TestCase(210000, "bad")]
    [TestCase(-165001, "empty-poor")]
    public void BeatorajaNormalMicrosecondReference(long offset, string expected)
    {
        var session = BmsProfiles.CreateBeatoraja(new KeyChart(1, [tap(0, 1000)])).CreateRecordingSession();
        session.AdvanceTo(ms(1000) + offset, [new InputEvent(ms(1000) + offset, 0, true)]);
        Assert.That(session.GetEventsSince(0).Single().Result.Value, Is.EqualTo(expected));
    }

    [TestCase(BmsRank.VeryHard, 27000)]
    [TestCase(BmsRank.Hard, 32000)]
    [TestCase(BmsRank.Normal, 40000)]
    [TestCase(BmsRank.Easy, 60000)]
    public void Lr2GreatWindowFromLunaticVibes(BmsRank rank, long boundary)
    {
        var profile = BmsProfiles.CreateLr2(new KeyChart(1, [tap(0, 1000)]), new BmsOptions(Rank: rank));
        var rule = (TapRule)profile.Chart.Notes[0].Rule;
        Assert.That(rule.Configuration.Windows.ResultFor(boundary - 1)?.Value, Is.EqualTo("great"));
        Assert.That(rule.Configuration.Windows.ResultFor(boundary)?.Value, Is.EqualTo("good"));
    }

    [Test]
    public void EmptyPoorDoesNotConsumeNoteOrAccuracyPotential()
    {
        var session = BmsProfiles.CreateBeatoraja(new KeyChart(1, [tap(0, 1000)])).CreateRecordingSession();
        session.AdvanceTo(ms(2000), [press(600), release(601), press(1000), release(1010), press(1100)]);
        Assert.That(session.GetEventsSince(0).Select(e => e.Result.Value), Is.EqualTo(["empty-poor", "pgreat", "empty-poor"]));
        Assert.That(session.Score.Score, Is.EqualTo(2));
        Assert.That(session.Score.AccuracyMaximum, Is.EqualTo(2));
        Assert.That(session.Score.Combo, Is.EqualTo(1));
    }

    [Test]
    public void BeatorajaComboSelectionSkipsLateBadForGoodCandidate()
    {
        var chart = new KeyChart(1, [tap(0, 1000), tap(1, 1200)]);
        var session = BmsProfiles.CreateBeatoraja(chart).CreateRecordingSession();
        session.AdvanceTo(ms(1200), [press(1200)]);
        Assert.That(session.GetEventsSince(0).Single().NoteId, Is.EqualTo(1));
    }

    [TestCase(BmsSelection.Combo)]
    [TestCase(BmsSelection.Score)]
    public void BeatorajaSelectionReplacesAnEarlierLateBad(BmsSelection selection)
    {
        var chart = new KeyChart(1, [tap(0, 1000), tap(1, 1050)]);
        var session = BmsProfiles.CreateBeatoraja(chart, new BmsOptions(Selection: selection)).CreateRecordingSession();
        session.AdvanceTo(ms(1200), [press(1200)]);
        var judgement = session.GetEventsSince(0).Single();
        Assert.That((judgement.NoteId, judgement.Result.Value), Is.EqualTo((1, "bad")));
    }

    [TestCase(BmsSelection.Nearest)]
    [TestCase(BmsSelection.Oldest)]
    public void BeatorajaSelectionPrefersAnUnjudgedNoteToAnEmptyPoorTarget(BmsSelection selection)
    {
        var chart = new KeyChart(1, [tap(0, 1000), tap(1, 1100)]);
        var session = BmsProfiles.CreateBeatoraja(chart, new BmsOptions(Selection: selection)).CreateRecordingSession();
        session.AdvanceTo(ms(1030), [press(1000), release(1001), press(1030)]);
        var judgement = session.GetEventsSince(1).Single();
        Assert.That((judgement.NoteId, judgement.Result.Value), Is.EqualTo((1, "good")));
    }

    [Test]
    public void Lr2LateBadChainSharesThePressWithTheFollowingNote()
    {
        var session = BmsProfiles.CreateLr2(new KeyChart(1, [tap(0, 1000), tap(1, 1200)])).CreateRecordingSession();
        session.AdvanceTo(ms(1190), [press(1190)]);
        Assert.That(session.GetEventsSince(0).Select(e => (e.NoteId, e.Result.Value)), Is.EqualTo([(0, "bad"), (1, "pgreat")]));
        Assert.That(session.Score.Score, Is.EqualTo(2));
    }

    [Test]
    public void OrdinaryLongNoteScoresOnceAndChargeScoresTwice()
    {
        var source = new KeyChart(2, [hold(0, 1000, 2000), hold(1, 1000, 2000) with { Lane = 1, Kind = KeyNoteKind.ReleaseHold }]);
        var profile = BmsProfiles.CreateBeatoraja(source);
        var session = profile.CreateRecordingSession();
        session.AdvanceTo(ms(3000), [press(1000), new InputEvent(ms(1000), 1, true), release(2000), new InputEvent(ms(2000), 1, false)]);
        Assert.That(profile.Chart.ScorePlan.Opportunities.Length, Is.EqualTo(3));
        Assert.That(session.Score.Score, Is.EqualTo(6));
    }

    [Test]
    public void ChargeRequiresReleaseButOrdinaryLongNoteCompletesWhileHeld()
    {
        var ordinary = BmsProfiles.CreateBeatoraja(new KeyChart(1, [hold(0, 1000, 2000)])).CreateRecordingSession();
        var charge = BmsProfiles.CreateBeatoraja(new KeyChart(1, [hold(0, 1000, 2000) with { Kind = KeyNoteKind.ReleaseHold }])).CreateRecordingSession();
        ordinary.AdvanceTo(ms(3000), [press(1000)]);
        charge.AdvanceTo(ms(3000), [press(1000)]);
        Assert.That(ordinary.GetEventsSince(0).Last().Result.Value, Is.EqualTo("pgreat"));
        Assert.That(charge.GetEventsSince(0).Last().Result.Value, Is.EqualTo("poor"));
    }

    [Test]
    public void ScratchChargeUsesOppositeControl()
    {
        var source = new KeyChart(1, [hold(0, 1000, 2000) with { Kind = KeyNoteKind.ReleaseHold, Directional = true }]);
        var session = BmsProfiles.CreateBeatoraja(source).CreateRecordingSession();
        session.AdvanceTo(ms(3000), [press(1000), new InputEvent(ms(2000), 0, true, 1)]);
        Assert.That(session.Score.Score, Is.EqualTo(4));
        Assert.That(session.GetEventsSince(0).Last().Offset, Is.Zero);
    }

    [Test]
    public void SustainIntegratesDirectionChangesAndDoesNotDependOnFrameSize()
    {
        var profile = BmsProfiles.CreateBeatoraja(new KeyChart(1, [hold(0, 1000, 3000) with { Kind = KeyNoteKind.SustainHold }]));
        var whole = profile.CreateRecordingSession();
        var partitioned = profile.CreateRecordingSession();
        InputEvent[] input = [press(1000), release(1100), press(1200), release(3000)];
        whole.AdvanceTo(ms(4000), input);
        partitioned.AdvanceTo(ms(1100), input[..2]);
        partitioned.AdvanceTo(ms(1200), input[2..3]);
        for (var time = 1201; time < 3000; time++)
            partitioned.AdvanceTo(ms(time));
        partitioned.AdvanceTo(ms(4000), input[3..]);
        Assert.That(partitioned.GetEventsSince(0), Is.EqualTo(whole.GetEventsSince(0)));
        Assert.That(partitioned.Health, Is.EqualTo(whole.Health));
        Assert.That(whole.GetEventsSince(0).Count(e => e.Part.Value == "body"), Is.GreaterThan(0));
    }

    [Test]
    public void StableAndLazerHaveDistinctPerfectWindowsAndHoldPotential()
    {
        var chart = new KeyChart(1, [tap(0, 1000), hold(1, 2000, 3000)]);
        var stable = ManiaProfiles.CreateStable(chart, new ManiaOptions(OverallDifficulty: 5));
        var lazer = ManiaProfiles.CreateLazer(chart, new ManiaOptions(OverallDifficulty: 5));
        Assert.That(((TapRule)stable.Chart.Notes[0].Rule).Configuration.Windows.ResultFor(ms(18))?.Value, Is.EqualTo("great"));
        Assert.That(((TapRule)((OrderedNoteRule)lazer.Chart.Notes[0].Rule).Inner).Configuration.Windows.ResultFor(ms(18))?.Value, Is.EqualTo("perfect"));
        Assert.That(stable.Chart.ScorePlan.Opportunities.Length, Is.EqualTo(2));
        Assert.That(lazer.Chart.ScorePlan.Opportunities.Length, Is.EqualTo(3));
    }

    [Test]
    public void StableGreatScoreAndAccuracyUseDifferentWeights()
    {
        var session = ManiaProfiles.CreateStable(new KeyChart(1, [tap(0, 1000)])).CreateRecordingSession();
        session.AdvanceTo(ms(2000), [press(1030)]);
        Assert.That(session.Score.Score, Is.EqualTo(968750));
        Assert.That(session.Score.Accuracy, Is.EqualTo(1));
    }

    [Test]
    public void StableCombinesEndpointErrorsAndLazerJudgesThemSeparately()
    {
        var source = new KeyChart(1, [hold(0, 1000, 2000)]);
        var stable = ManiaProfiles.CreateStable(source).CreateRecordingSession();
        var lazer = ManiaProfiles.CreateLazer(source).CreateRecordingSession();
        InputEvent[] input = [press(1010), release(2030)];
        stable.AdvanceTo(ms(3000), input);
        lazer.AdvanceTo(ms(3000), input);
        Assert.That(stable.GetEventsSince(0).Last().Result.Value, Is.EqualTo("great"));
        Assert.That(lazer.GetEventsSince(0).Select(e => e.Result.Value), Is.EqualTo(["perfect", "great"]));
    }

    [TestCase("lazer")]
    [TestCase("stable")]
    public void BrokenManiaHoldCanRecoverButTailIsCapped(string profile)
    {
        var session = compile(profile, new KeyChart(1, [hold(0, 1000, 2000)])).CreateRecordingSession();
        session.AdvanceTo(ms(3000), [press(1000), release(1400), press(1600), release(2000)]);
        Assert.That(session.GetEventsSince(0).Last().Result.Value, Is.EqualTo("meh"));
    }

    [Test]
    public void LazerReleaseLenienceDoesNotApplyToHead()
    {
        var profile = ManiaProfiles.CreateLazer(new KeyChart(1, [hold(0, 1000, 2000)]));
        var rule = (HoldRule)((OrderedNoteRule)profile.Chart.Notes[0].Rule).Inner;
        Assert.That(rule.Configuration.HeadWindows.ResultFor(ms(20))?.Value, Is.EqualTo("great"));
        Assert.That(rule.Configuration.TailWindows.ResultFor(ms(20))?.Value, Is.EqualTo("perfect"));
    }

    [Test]
    public void O2JamBeatWindowsCrossTempoBoundaries()
    {
        var source = new KeyChart(1, [tap(0, 1000)], [new TempoPoint(0, 120), new TempoPoint(ms(1020), 240)]);
        var profile = O2JamProfile.Create(source);
        var window = ((TapRule)profile.Chart.Notes[0].Rule).Configuration.Windows.Entries[0];
        Assert.That(window.Early, Is.EqualTo(-66400));
        Assert.That(window.Late, Is.EqualTo(43200));
    }

    [Test]
    public void O2JamPillRescuesResultBeforeScoreHealthAndDisplay()
    {
        var notes = Enumerable.Range(0, 16).Select(i => tap(i, 1000 + i * 1000)).ToArray();
        var profile = O2JamProfile.Create(new KeyChart(1, notes), new O2JamOptions(BeatBasedWindows: false));
        var session = profile.CreateRecordingSession();
        var input = Enumerable.Range(0, 15).SelectMany(i => new[] { press(1000 + i * 1000), release(1001 + i * 1000) })
                              .Append(press(16150)).ToArray();
        session.AdvanceTo(ms(18000), input);
        var rescued = session.GetEventsSince(0).Last();
        Assert.That(rescued.OriginalResult.Value, Is.EqualTo("bad"));
        Assert.That(rescued.Result.Value, Is.EqualTo("good"));
        Assert.That(session.Score.Counters["pills"], Is.Zero);
        Assert.That(session.Health.Value, Is.EqualTo(1));
        Assert.That(session.Score.Combo, Is.EqualTo(16));
    }

    [TestCase(0, 2)]
    [TestCase(5, 2)]
    [TestCase(65, 0)]
    [TestCase(180, -5.5)]
    [TestCase(200, -5.5)]
    public void Wife3IndependentCurveAnchors(double offset, double expected)
        => Assert.That(EtternaProfile.Wife3(offset), Is.EqualTo(expected).Within(1e-7));

    [Test]
    public void EtternaDisplayWindowDoesNotQuantizeWifeCredit()
    {
        var source = new KeyChart(1, [tap(0, 1000)]);
        var a = EtternaProfile.Create(source).CreateRecordingSession();
        var b = EtternaProfile.Create(source).CreateRecordingSession();
        a.AdvanceTo(ms(2000), [press(1010)]);
        b.AdvanceTo(ms(2000), [press(1020)]);
        Assert.That(a.GetEventsSince(0).Single().Result.Value, Is.EqualTo(b.GetEventsSince(0).Single().Result.Value));
        Assert.That(a.Score.Score, Is.GreaterThan(b.Score.Score));
    }

    [Test]
    public void EtternaHoldGraceAndRollRetriggerAreDistinct()
    {
        var holdSession = EtternaProfile.Create(new KeyChart(1, [hold(0, 1000, 2000)])).CreateRecordingSession();
        holdSession.AdvanceTo(ms(3000), [press(1000), release(1400), press(1649)]);
        Assert.That(holdSession.GetEventsSince(0).Last().Result.Value, Is.EqualTo("held"));

        var rollSession = EtternaProfile.Create(new KeyChart(1, [hold(0, 1000, 2000) with { Kind = KeyNoteKind.Roll }])).CreateRecordingSession();
        rollSession.AdvanceTo(ms(3000), [press(1000)]);
        Assert.That(rollSession.GetEventsSince(0).Last().Result.Value, Is.EqualTo("let-go"));
        Assert.That(rollSession.Score.AccuracyPoints, Is.EqualTo(-2.5));
    }

    [Test]
    public void EtternaMineHasIndependentPenaltyAndNoAccuracyPotential()
    {
        var source = new KeyChart(2, [tap(0, 1000), new KeyNote(1, 1, ms(1000), ms(1000), KeyNoteKind.Mine)]);
        var session = EtternaProfile.Create(source).CreateRecordingSession();
        session.AdvanceTo(ms(2000), [new InputEvent(ms(1000), 1, true), press(1000)]);
        Assert.That(session.GetEventsSince(0).Select(e => e.Result.Value), Is.EqualTo(["mine-hit", "w1"]));
        Assert.That(session.Score.AccuracyMaximum, Is.EqualTo(2));
        Assert.That(session.Score.AccuracyPoints, Is.EqualTo(-5));
    }

    [Test]
    public void ShortAutomaticHoldCanStartLateWithinItsHeadWindow()
    {
        var session = BmsProfiles.CreateBeatoraja(new KeyChart(1, [hold(0, 1000, 1010)])).CreateRecordingSession();
        session.AdvanceTo(ms(2000), [press(1020)]);
        Assert.That(session.GetEventsSince(0).Select(e => e.Result.Value), Is.EqualTo(["great", "great"]));
        Assert.That(session.Score.Score, Is.EqualTo(1));
    }

    [Test]
    public void SustainKeepsObservingInputAfterAnEarlyTailResolution()
    {
        var profile = BmsProfiles.CreateBeatoraja(new KeyChart(1, [hold(0, 1000, 3000) with { Kind = KeyNoteKind.SustainHold }]));
        var session = profile.CreateRecordingSession();
        session.AdvanceTo(ms(4000), [press(1000), release(1100), press(1200)]);
        Assert.That(session.GetEventsSince(0).Count(e => e.Part.Value == "tail"), Is.EqualTo(1));
        Assert.That(session.GetEventsSince(0).Where(e => e.Part.Value == "body").Select(e => e.Result.Value), Is.All.EqualTo("sustain-gain"));
        Assert.That(session.GetEventsSince(0).First(e => e.Part.Value == "body").Time, Is.EqualTo(ms(1400) + 1));
    }

    [Test]
    public void SustainCrossingAtAnInputTimestampIsNotLost()
    {
        var profile = BmsProfiles.CreateBeatoraja(new KeyChart(1, [hold(0, 1000, 3000) with { Kind = KeyNoteKind.SustainHold }]));
        var session = profile.CreateRecordingSession();
        session.AdvanceTo(ms(4000), [press(1000), new InputEvent(ms(1200) + 1, 0, false)]);
        Assert.That(session.GetEventsSince(0).Single(e => e.Result.Value == "sustain-gain").Time, Is.EqualTo(ms(1200) + 1));
    }

    [TestCase(BmsGauge.Hard, 0.06)]
    [TestCase(BmsGauge.ExHard, 0.03)]
    public void SustainRecoveryUsesHalfTheGreatGaugeIncrease(BmsGauge gauge, double expectedRecovery)
    {
        var chart = new KeyChart(1,
        [
            new KeyNote(0, 0, ms(500), ms(500), KeyNoteKind.Mine, MineDamage: 50),
            hold(1, 1000, 3000) with { Kind = KeyNoteKind.SustainHold },
        ]);
        var session = BmsProfiles.CreateBeatoraja(chart, new BmsOptions(Gauge: gauge)).CreateRecordingSession();
        session.AdvanceTo(ms(1000), [press(499), release(600), press(1000)]);
        var before = session.Health.Value;
        session.AdvanceTo(ms(1200) + 1);
        Assert.That(session.GetEventsSince(0).Last().Result.Value, Is.EqualTo("sustain-gain"));
        Assert.That(session.Health.Value - before, Is.EqualTo(expectedRecovery).Within(0.000005));
    }

    [Test]
    public void HardGaugeRemainsDeadAfterFailure()
    {
        var source = new KeyChart(1, [new KeyNote(0, 0, ms(1000), ms(1000), KeyNoteKind.Mine, MineDamage: 100), tap(1, 2000)]);
        var session = BmsProfiles.CreateBeatoraja(source, new BmsOptions(Gauge: BmsGauge.Hard)).CreateRecordingSession();
        session.AdvanceTo(ms(3000), [press(999), release(1500), press(2000)]);
        Assert.That(session.Health, Is.EqualTo(new HealthState(0, true)));
    }

    [Test]
    public void ManiaHpRecoveryCalibratesBothHoldEndpointsPerTopLevelObject()
    {
        var sparse = new KeyChart(1, [tap(0, 1000), tap(1, 100000)]);
        var withBreak = new KeyChart(1, sparse.Notes, breaks: [new ChartBreak(ms(2000), ms(99000))]);
        var sparseRecovery = ManiaHealthCalibration.ComputeRecoveryMultiplier(sparse, 5);
        var breakRecovery = ManiaHealthCalibration.ComputeRecoveryMultiplier(withBreak, 5);
        var holds = new KeyChart(1, [hold(0, 1000, 2000), hold(1, 100000, 101000)]);
        var holdRecovery = ManiaHealthCalibration.ComputeRecoveryMultiplier(holds, 5);
        Assert.That(sparseRecovery, Is.GreaterThan(1));
        Assert.That(breakRecovery, Is.GreaterThan(1));
        Assert.That(holdRecovery, Is.LessThan(sparseRecovery));
    }

    private static CompiledProfile compile(string profile, KeyChart chart) => profile switch
    {
        "beatoraja" => BmsProfiles.CreateBeatoraja(chart), "lr2" => BmsProfiles.CreateLr2(chart),
        "lazer" => ManiaProfiles.CreateLazer(chart), "stable" => ManiaProfiles.CreateStable(chart),
        "o2jam" => O2JamProfile.Create(chart), "etterna" => EtternaProfile.Create(chart),
        "malody-pc" => MalodyProfile.Create(chart),
        "malody-mobile" => MalodyProfile.Create(chart, new MalodyOptions(Client: MalodyClient.ClassicMobile)),
        "malody-v" => MalodyProfile.Create(chart, new MalodyOptions(Client: MalodyClient.VPro)),
        "malody-rc" => MalodyProfile.Create(chart, new MalodyOptions(Client: MalodyClient.VPro, VVersion: MalodyVVersion.Rc20)),
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };

    private static long ms(double milliseconds) => Time.FromMilliseconds(milliseconds);

    private static KeyNote tap(int id, double time) => new(id, 0, ms(time), ms(time));

    private static KeyNote hold(int id, double start, double end) => new(id, 0, ms(start), ms(end), KeyNoteKind.Hold);

    private static InputEvent press(double time) => new(ms(time), 0, true);

    private static InputEvent release(double time) => new(ms(time), 0, false);
}
