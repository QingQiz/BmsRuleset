#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Course;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Course;

[TestFixture]
public class BmsCourseHealthInheritanceTest
{
    private static readonly BmsLayoutVariant[] layouts = [BmsLayoutVariant.Bme7K, BmsLayoutVariant.Bms5K, BmsLayoutVariant.Pms9K];

    [Test]
    public void SingleGaugeCarriesHealthAcrossThreeStages(
        [Values(BmsGaugeType.Class, BmsGaugeType.ExClass, BmsGaugeType.ExHardClass)] BmsGaugeType type,
        [Values(null, BmsGaugeProfileFamily.FiveKeys, BmsGaugeProfileFamily.SevenKeys, BmsGaugeProfileFamily.Pms,
            BmsGaugeProfileFamily.Keyboard, BmsGaugeProfileFamily.Lr2)] BmsGaugeProfileFamily? family,
        [Values] bool modsFirst)
    {
        var session = createSession(type, family, false);
        BmsGaugeStateSnapshot[] expected = [new(type, 1, false)];
        var saved = new List<(Score Score, BmsGaugeStateSnapshot[] Initial)>();

        for (var stage = 0; stage < layouts.Length; stage++)
        {
            session.BeginCurrentStage();
            using var health = createHealth(stage);
            var mods = persistMods(session.Mods);
            var observed = new List<double>();
            health.Health.ValueChanged += change => observed.Add(change.NewValue);
            if (modsFirst) applyMods(mods, health);
            session.ConfigureHealthProcessor(health);
            if (!modsFirst) applyMods(mods, health);

            assertStartingState(health, expected, family, stage);
            Assert.That(observed, Has.All.EqualTo(expected[0].Health), "Loading must not temporarily refill a carried gauge.");
            var score = archive(mods);
            saved.Add((score, expected));

            health.RegisterNonConsumingJudgement(HitResult.Ok, 1000);
            health.ApplyHellChargeTick(true, eventTime: 1200);
            Assert.That(health.HasPassedAtEnd(), Is.True);
            Assert.That(health.Health.Value, Is.LessThan(expected[0].Health));
            expected = health.CurrentGaugeStates.ToArray();
            session.CompleteCurrentStage(score.ScoreInfo, expected);
            if (stage + 1 < layouts.Length)
            {
                session.RequestAdvance();
                session.Advance();
            }
        }

        Assert.That(session.Status, Is.EqualTo(BmsCourseStatus.Passed));
        for (var stage = 0; stage < saved.Count; stage++)
        {
            using var replayHealth = createHealth(stage);
            applyMods(persistMods(saved[stage].Score.DeepClone().ScoreInfo.Mods), replayHealth);
            assertStartingState(replayHealth, saved[stage].Initial, family, stage);
        }
    }

    [Test]
    public void AutoGaugeCarriesEachSurvivingTierAcrossThreeStages(
        [Values(null, BmsGaugeProfileFamily.FiveKeys, BmsGaugeProfileFamily.SevenKeys, BmsGaugeProfileFamily.Pms,
            BmsGaugeProfileFamily.Keyboard, BmsGaugeProfileFamily.Lr2)] BmsGaugeProfileFamily? family,
        [Values] bool modsFirst)
    {
        var session = createSession(BmsGaugeType.ExHardClass, family, true);
        var expected = BmsModAutoGauge.COURSE_AUTO_GAUGE_CHAIN.Select(type => new BmsGaugeStateSnapshot(type, 1, false)).ToArray();
        var saved = new List<(Score Score, BmsGaugeStateSnapshot[] Initial)>();

        for (var stage = 0; stage < layouts.Length; stage++)
        {
            session.BeginCurrentStage();
            using var health = createHealth(stage);
            var mods = persistMods(session.Mods);
            if (modsFirst) applyMods(mods, health);
            session.ConfigureHealthProcessor(health);
            if (!modsFirst) applyMods(mods, health);
            assertStartingState(health, expected, family, stage);

            var score = archive(mods);
            saved.Add((score, expected));
            var tierAtStart = health.GaugeType;
            if (stage < 2)
            {
                for (var miss = 0; health.GaugeType == tierAtStart && miss < 200; miss++)
                    health.RegisterNonConsumingJudgement(HitResult.Ok, 1000 + miss);
                Assert.That(health.GaugeType, Is.Not.EqualTo(tierAtStart), "The stage must exercise a real AG downgrade.");
            }
            else
                health.RegisterNonConsumingJudgement(HitResult.Ok, 1000);

            Assert.That(health.HasPassedAtEnd(), Is.True);
            mods.OfType<BmsModAutoGauge>().Single().ApplyToScore(score.ScoreInfo);
            expected = health.CurrentGaugeStates.Where(state => !state.Failed).ToArray();
            Assert.That(expected, Has.Length.EqualTo(2 - System.Math.Min(stage, 1)));
            session.CompleteCurrentStage(score.ScoreInfo, health.CurrentGaugeStates);
            if (stage + 1 < layouts.Length)
            {
                session.RequestAdvance();
                session.Advance();
            }
        }

        Assert.That(session.Status, Is.EqualTo(BmsCourseStatus.Passed));
        for (var stage = 0; stage < saved.Count; stage++)
        {
            using var replayHealth = createHealth(stage);
            applyMods(persistMods(saved[stage].Score.DeepClone().ScoreInfo.Mods), replayHealth);
            assertStartingState(replayHealth, saved[stage].Initial, family, stage);
        }
    }

    [Test]
    public void NearlyEmptyGaugeIsNotRoundedOrRefilledAtTheNextStage(
        [Values(BmsGaugeType.Class, BmsGaugeType.ExClass, BmsGaugeType.ExHardClass)] BmsGaugeType type,
        [Values(0.000000001, 0.005, 0.999999999, 1)] double remaining)
    {
        var session = createSession(type, BmsGaugeProfileFamily.Lr2, false);
        session.BeginCurrentStage();
        session.CompleteCurrentStage(new ScoreInfo { Passed = true }, [new(type, remaining, false)]);
        session.RequestAdvance();
        session.Advance();
        session.BeginCurrentStage();

        using var health = createHealth(1);
        applyMods(persistMods(session.Mods), health);
        session.ConfigureHealthProcessor(health);
        Assert.That(health.Health.Value, Is.EqualTo(remaining));
        Assert.That(health.HasPassedAtEnd(), Is.True);
        health.RegisterNonConsumingJudgement(HitResult.Ok, 1000);
        if (remaining <= 0.005)
        {
            Assert.That(health.Health.Value, Is.Zero);
            Assert.That(health.HasEverFailed, Is.True);
            Assert.That(health.HasPassedAtEnd(), Is.False);
            session.FailCurrentStage(new ScoreInfo { Passed = false }, health.CurrentGaugeStates);
            Assert.That(session.CanContinue, Is.False);
            Assert.That(session.Stages[2].Status, Is.EqualTo(BmsCourseStageStatus.NotPlayed));
        }
    }

    [TestCase(BmsGaugeType.Class)]
    [TestCase(BmsGaugeType.ExClass)]
    [TestCase(BmsGaugeType.ExHardClass)]
    public void RebuildingAFailedInheritedGaugeRestoresItsCarriedHealth(BmsGaugeType type)
    {
        var session = createSession(type, BmsGaugeProfileFamily.Lr2, false);
        session.BeginCurrentStage();
        session.CompleteCurrentStage(new ScoreInfo { Passed = true }, [new(type, 0.001, false)]);
        session.RequestAdvance();
        session.Advance();
        using var health = createHealth(1);
        applyMods(persistMods(session.Mods), health);
        var initial = health.CurrentGaugeStates.ToArray();

        health.RegisterNonConsumingJudgement(HitResult.Ok, 1000);
        Assert.That(health.HasFailed, Is.True);
        health.ResetReplayState();
        Assert.Multiple(() =>
        {
            Assert.That(health.CurrentGaugeStates, Is.EqualTo(initial));
            Assert.That(health.Health.Value, Is.EqualTo(0.001));
            Assert.That(health.HasFailed, Is.False);
            Assert.That(health.HasEverFailed, Is.False);
            Assert.That(health.HasPassedAtEnd(), Is.True);
        });
        health.RegisterNonConsumingJudgement(HitResult.Ok, 1000);
        Assert.That(health.HasPassedAtEnd(), Is.False);
    }

    [Test]
    public void FinalAutoGaugeTierFailsTheCourseAndDoesNotReviveRemovedTiers()
    {
        var session = createSession(BmsGaugeType.ExHardClass, BmsGaugeProfileFamily.Lr2, true);
        session.BeginCurrentStage();
        session.CompleteCurrentStage(new ScoreInfo { Passed = true },
        [
            new(BmsGaugeType.ExHardClass, 0, true),
            new(BmsGaugeType.ExClass, 0, true),
            new(BmsGaugeType.Class, 0.001, false),
        ]);
        session.RequestAdvance();
        session.Advance();
        session.BeginCurrentStage();
        using var health = createHealth(1);
        applyMods(persistMods(session.Mods), health);
        session.ConfigureHealthProcessor(health);
        BmsGaugeStateSnapshot[] expected = [new(BmsGaugeType.Class, 0.001, false)];
        Assert.That(health.CurrentGaugeStates, Is.EqualTo(expected));
        health.RegisterNonConsumingJudgement(HitResult.Ok, 1000);
        Assert.That(health.HasFailed, Is.True);
        Assert.That(health.HasPassedAtEnd(), Is.False);
        session.FailCurrentStage(new ScoreInfo { Passed = false }, health.CurrentGaugeStates);
        Assert.That(session.Status, Is.EqualTo(BmsCourseStatus.Failed));
        Assert.Throws<System.InvalidOperationException>(session.RequestAdvance);
        Assert.That(session.Stages[2].Status, Is.EqualTo(BmsCourseStageStatus.NotPlayed));
    }

    private static BmsCourseSession createSession(BmsGaugeType type, BmsGaugeProfileFamily? family, bool autoGauge)
    {
        string[] constraints = family switch
        {
            BmsGaugeProfileFamily.FiveKeys => ["gauge_5k"],
            BmsGaugeProfileFamily.SevenKeys => ["gauge_7k"],
            BmsGaugeProfileFamily.Pms => ["gauge_9k"],
            BmsGaugeProfileFamily.Keyboard => ["gauge_24k"],
            BmsGaugeProfileFamily.Lr2 => ["gauge_lr2"],
            _ => [],
        };
        var stages = layouts.Select((_, index) => new BmsResolvedCourseStage(
            new BmsCourseStage($"Stage {index}", "1", BeatmapHash: $"stage-{index}"), new BeatmapInfo())).ToArray();
        var course = new BmsCourseDefinition("inheritance", "Table", "Dan", stages.Select(stage => stage.Definition).ToArray(), constraints);
        var mods = BmsCourseSession.CreateCourseMods(autoGauge ? [new BmsModAutoGauge()] : [], type, constraints);
        return new BmsCourseSession(course, stages, mods, type);
    }

    private static BmsHealthProcessor createHealth(int stage)
    {
        var health = new BmsHealthProcessor();
        health.ApplyBeatmap(new BmsBeatmap
        {
            LayoutVariant = layouts[stage],
            HitObjects = [new BmsNote { Column = 1, StartTime = 1000 }],
        });
        return health;
    }

    private static Mod[] persistMods(IReadOnlyList<Mod> mods)
    {
        var score = new ScoreInfo { Ruleset = new BmsRuleset().RulesetInfo, Mods = mods.Select(mod => mod.DeepClone()).ToArray() };
        return new ScoreInfo { Ruleset = score.Ruleset, ModsJson = score.ModsJson }.Mods;
    }

    private static void applyMods(IReadOnlyList<Mod> mods, BmsHealthProcessor health)
    {
        foreach (var mod in mods.OfType<IApplicableToHealthProcessor>())
            mod.ApplyToHealthProcessor(health);
    }

    private static Score archive(Mod[] mods)
        => BmsTestReplayArchive.RoundTrip(new Score
        {
            ScoreInfo = new ScoreInfo { Ruleset = new BmsRuleset().RulesetInfo, Passed = true, Mods = mods },
            Replay = { Frames = [new BmsReplayFrame(0), new BmsReplayFrame(2000)] },
        }.DeepClone());

    private static void assertStartingState(BmsHealthProcessor health, BmsGaugeStateSnapshot[] expected, BmsGaugeProfileFamily? family, int stage)
    {
        var effectiveFamily = family ?? BmsGaugeProfileFamilyProvider.FromLayout(layouts[stage]);
        Assert.Multiple(() =>
        {
            Assert.That(health.IsCourseGaugeMode, Is.True);
            Assert.That(health.ConfiguredProfileFamily, Is.EqualTo(family));
            Assert.That(health.CurrentGaugeStates, Is.EqualTo(expected));
            Assert.That(health.Health.Value, Is.EqualTo(expected[0].Health));
            Assert.That(health.GaugeType, Is.EqualTo(expected[0].GaugeType));
            Assert.That(health.GaugeProfile, Is.EqualTo(BmsGaugeProfileFactory.Create(expected[0].GaugeType, effectiveFamily)));
            Assert.That(health.HasFailed, Is.False);
            Assert.That(health.HasEverFailed, Is.False);
        });
    }
}
