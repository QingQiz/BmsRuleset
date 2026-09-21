#nullable enable
using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Framework.Graphics.Pooling;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsVisualCulling : BmsPlayerTestScene
{
    private DrawableBmsHitObject drawable = null!;
    private BmsHitObject hitObject = null!;
    private bool longNote;
    private BmsLongNoteMode mode;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(60000)]);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Rank = 2,
            LockedLongNoteMode = mode,
            HitObjects =
            [
                longNote ? new BmsLongNote { StartTime = 5000, Duration = 2000, Column = 1 }
                         : new BmsNote { StartTime = 5000, Column = 1 },
                new BmsNote { StartTime = 30000, Column = 2 },
            ],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    private void load(bool useLongNote = false, BmsLongNoteMode longNoteMode = BmsLongNoteMode.LongNote)
    {
        AddStep("load player", () =>
        {
            longNote = useLongNote;
            mode = longNoteMode;
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Playfield.Stage.IsLoaded);
        seek(4800);
        AddUntilStep("object alive", () =>
        {
            drawable = Playfield.AllColumnAliveObjects().OfType<DrawableBmsHitObject>().SingleOrDefault(d => d.HitObject.StartTime == 5000)!;
            if (drawable != null)
                hitObject = drawable.HitObject;
            return drawable != null;
        });
    }

    private void seek(double time)
    {
        AddStep($"seek {time}", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(time);
        });
        AddUntilStep("simulation caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < 0.001);
    }

    private void hide()
    {
        AddStep("move full object outside viewport", () =>
        {
            drawable.HitObject.ScrollPositionAtStartTime = -1_000_000;
            if (drawable.HitObject is BmsLongNote ln)
            {
                ln.ScrollPositionAtEndTime = -1_000_000;
                ln.VisualScrollPositionAtEndTime = -1_000_000;
            }
        });
        AddUntilStep("visuals culled", () => !drawable.IsPresent);
    }

    [Test]
    public void TestHitFeedbackReusesExpiredDrawables()
    {
        var poolSize = 0;
        load();
        AddStep("warm overlapping feedback", () =>
        {
            for (var i = 0; i < 32; i++)
                ((BmsColumn)Playfield.Stage.Columns[1]).TriggerHitExplosion(false);
        });
        AddStep("record warmed pool", () => poolSize = ((BmsColumn)Playfield.Stage.Columns[1]).ChildrenOfType<DrawablePool<BmsHitExplosion>>().Sum(p => p.CurrentPoolSize));
        seek(4850);
        AddAssert("every pulse remains visible", () => ((BmsColumn)Playfield.Stage.Columns[1]).HitExplosionArea.AliveChildren.Count(d => d.Alpha > 0) == 32);
        seek(5100);
        AddUntilStep("pulses expire", () => !((BmsColumn)Playfield.Stage.Columns[1]).HitExplosionArea.AliveChildren.Any());
        AddStep("trigger another burst", () =>
        {
            var column = (BmsColumn)Playfield.Stage.Columns[1];
            for (var i = 0; i < 32; i++)
                column.TriggerHitExplosion(false);
        });
        AddAssert("expired drawables are reused", () => ((BmsColumn)Playfield.Stage.Columns[1]).ChildrenOfType<DrawablePool<BmsHitExplosion>>().Sum(p => p.CurrentPoolSize) == poolSize);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TestHitFeedbackPreservesOverlappingPulseBrightness(bool isLongNote)
    {
        load();
        for (var pulse = 0; pulse < 4; pulse++)
        {
            seek(4800 + pulse * 50);
            AddStep("trigger independent pulse", () => ((BmsColumn)Playfield.Stage.Columns[1]).TriggerHitExplosion(isLongNote));
        }

        seek(4990);
        AddStep("all four pulses retain their original fade phase", () =>
        {
            var pulses = ((BmsColumn)Playfield.Stage.Columns[1]).HitExplosionArea.AliveChildren.OrderBy(d => d.LifetimeStart).ToArray();
            Assert.That(pulses, Has.Length.EqualTo(4));
            double[] expected = [10d / 120, 60d / 120, 110d / 120, 40d / 80];
            for (var i = 0; i < pulses.Length; i++)
                Assert.That(pulses[i].Alpha, Is.EqualTo(expected[i]).Within(0.001), $"pulse {i}");
        });
        seek(5200);
        AddUntilStep("all pulses expire", () => !((BmsColumn)Playfield.Stage.Columns[1]).HitExplosionArea.AliveChildren.Any());
    }

    [Test]
    public void TestOffscreenTapReceivesPassivePoorBeforeRemoval()
    {
        load();
        hide();
        seek(5281);
        AddAssert("passive poor reached score processor", () => Player.Results.Any(r => r.HitObject == hitObject && r.Type == HitResult.Meh));
        AddAssert("judged at checkpoint without waiting for removal", () =>
            Player.Results.Single(r => r.HitObject == hitObject).TimeAbsolute <= 5281);
    }

    [Test]
    public void TestOffscreenTapStillAcceptsInputAndReenters()
    {
        load();
        hide();
        AddStep("restore position", () => drawable.HitObject.ScrollPositionAtStartTime = Playfield.ScrollController.CurrentScrollPosition + 100);
        AddUntilStep("visible with fresh position", () => drawable.IsPresent && drawable.Y < 0 && drawable.Y > -drawable.Parent!.DrawHeight);
        hide();
        seek(5000);
        AddStep("press while culled", () => Playfield.Stage.Columns[1].HandlePress(5000));
        AddAssert("input judged perfectly", () => Player.Results.Any(r => r.HitObject == hitObject && r.Type == HitResult.Perfect));
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void TestLongNoteCrossingViewportIsNotCulled(BmsLongNoteMode longNoteMode)
    {
        load(true, longNoteMode);
        AddStep("place endpoints on opposite sides", () =>
        {
            var scroll = Playfield.ScrollController;
            var distance = scroll.ScrollRange / scroll.ScrollSpeedMultiplier * 3;
            drawable.HitObject.ScrollPositionAtStartTime = scroll.CurrentScrollPosition - distance;
            var ln = (BmsLongNote)drawable.HitObject;
            ln.ScrollPositionAtEndTime = ln.VisualScrollPositionAtEndTime = scroll.CurrentScrollPosition + distance;
        });
        AddUntilStep("head below viewport but body present", () => drawable.Y > drawable.Parent!.DrawHeight && drawable.IsPresent);
        hide();
        AddStep("bring body back", () => ((BmsLongNote)drawable.HitObject).VisualScrollPositionAtEndTime = Playfield.ScrollController.CurrentScrollPosition + 100);
        AddUntilStep("body restores visual presence", () => drawable.IsPresent);
    }

    [Test]
    public void TestCulledHellChargeStillDrainsAndRecovers()
    {
        load(true, BmsLongNoteMode.HellChargeNote);
        hide();
        seek(5400);
        var healthAfterHead = 0d;
        AddStep("capture head poor health", () => healthAfterHead = Player.HealthProcessor.Health.Value);
        seek(5700);
        AddAssert("culled body drains health", () => Player.HealthProcessor.Health.Value < healthAfterHead);
        AddStep("hold culled HCN", () => Playfield.Stage.Columns[1].HandlePress(5700));
        var releasedHealth = 0d;
        AddStep("capture released health", () => releasedHealth = Player.HealthProcessor.Health.Value);
        seek(6300);
        AddAssert("culled body recovers health", () => Player.HealthProcessor.Health.Value > releasedHealth);
        AddAssert("body remains culled", () => !drawable.IsPresent);
    }
}
