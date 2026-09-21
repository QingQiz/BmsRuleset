#nullable enable
using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics.Pooling;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
[NonParallelizable]
public partial class TestSceneBmsHitExplosionLimit : BmsPlayerTestScene
{
    private BmsHitExplosionLimitProbe? probe;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(60000)]);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var chart = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 3 };
        for (var i = 0; i < 256; i++)
            chart.HitObjects.Add(new BmsNote { StartTime = 5000, Column = 1 });
        chart.HitObjects.Add(new BmsNote { StartTime = 60000, Column = 2 });
        BmsTestBeatmaps.SetupBeatmapInfo(chart, ruleset);
        return chart;
    }

    private BmsColumn column => (BmsColumn)Playfield.Stage.Columns[1];

    private void load(int limit, BmsHitExplosionOverflowPolicy policy = BmsHitExplosionOverflowPolicy.KeepExisting)
    {
        AddStep("load experimental limit", () =>
        {
            probe?.Dispose();
            probe = new BmsHitExplosionLimitProbe(limit, policy);
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        seek(1000);
    }

    private void seek(double time)
    {
        AddStep($"seek {time}", () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(time); });
        AddUntilStep("simulation reached target", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < 0.000001);
    }

    [TestCase(16)]
    [TestCase(32)]
    [TestCase(64)]
    [TestCase(128)]
    public void LimitBoundsPreloadAndBurst(int limit)
    {
        load(limit);
        AddAssert("prewarm respects the limit", () => column.ChildrenOfType<DrawablePool<BmsHitExplosion>>().All(p => p.CurrentPoolSize <= limit));
        AddStep("trigger extreme burst", () => { for (var i = 0; i < 1000; i++) column.TriggerHitExplosion(false); });
        seek(1050);
        AddAssert("drawable count is bounded", () => column.HitExplosionArea.AliveChildren.Count() == limit);
        seek(1250);
        AddUntilStep("all pulses expire", () => !column.HitExplosionArea.AliveChildren.Any());
        AddStep("trigger another burst", () => { for (var i = 0; i < 1000; i++) column.TriggerHitExplosion(false); });
        seek(1300);
        AddAssert("expired objects are reused within the same budget", () => column.ChildrenOfType<DrawablePool<BmsHitExplosion>>().All(p => p.CurrentPoolSize <= limit));
    }

    [Test]
    public void UnderLimitPreservesEveryFadePhase()
    {
        load(16);
        for (var i = 0; i < 4; i++)
        {
            seek(1000 + i * 40);
            AddStep("trigger independent pulse", () => column.TriggerHitExplosion(false));
        }
        seek(1150);
        AddStep("no phase changes below the cap", () =>
        {
            var pulses = column.HitExplosionArea.AliveChildren.OrderBy(p => p.LifetimeStart).ToArray();
            Assert.That(pulses, Has.Length.EqualTo(4));
            double[] expected = [50d / 120, 90d / 120, 70d / 80, 30d / 80];
            for (var i = 0; i < pulses.Length; i++)
                Assert.That(pulses[i].Alpha, Is.EqualTo(expected[i]).Within(0.001));
        });
    }

    [Test]
    public void OverflowRetainsLatestHitAndSeparatesNormalFromLongNote()
    {
        load(2, BmsHitExplosionOverflowPolicy.ReplaceOldest);
        AddStep("first tap and hold pulse", () => { column.TriggerHitExplosion(false); column.TriggerHitExplosion(true); });
        seek(1040);
        AddStep("second tap", () => column.TriggerHitExplosion(false));
        seek(1080);
        AddStep("overflow tap", () => column.TriggerHitExplosion(false));
        seek(1090);
        AddStep("oldest tap replaced, hold pulse retained", () =>
        {
            var pulses = column.HitExplosionArea.AliveChildren.OrderBy(p => p.LifetimeStart).ToArray();
            Assert.That(pulses.Select(p => p.LifetimeStart), Is.EqualTo(new double[] { 1000, 1040, 1080 }));
            Assert.That(pulses[0].Alpha, Is.EqualTo(110d / 120).Within(0.001));
            Assert.That(pulses[2].Alpha, Is.EqualTo(10d / 80).Within(0.001));
        });
    }

    [Test]
    public void KeepExistingLetsPulsesReachPeakBrightnessDuringOverflow()
    {
        load(2);
        AddStep("fill both slots", () => { column.TriggerHitExplosion(false); column.TriggerHitExplosion(false); });
        seek(1040);
        AddStep("overflow with a dense burst", () => { for (var i = 0; i < 1000; i++) column.TriggerHitExplosion(false); });
        seek(1080);
        AddAssert("both original pulses reach their peak", () => column.HitExplosionArea.AliveChildren.Count() == 2
            && column.HitExplosionArea.AliveChildren.All(p => Math.Abs(p.Alpha - 1) < 0.001 && p.LifetimeStart == 1000));
    }

    [Test]
    public void ZeroKeepsOriginalUnlimitedBehaviour()
    {
        load(0);
        AddStep("trigger 256 pulses", () => { for (var i = 0; i < 256; i++) column.TriggerHitExplosion(false); });
        seek(1050);
        AddAssert("all 256 pulses are present", () => column.HitExplosionArea.AliveChildren.Count() == 256);
    }

    [TearDown]
    public void RemoveExperiment()
    {
        probe?.Dispose();
        probe = null;
    }

    protected override void Dispose(bool isDisposing)
    {
        probe?.Dispose();
        base.Dispose(isDisposing);
    }
}
