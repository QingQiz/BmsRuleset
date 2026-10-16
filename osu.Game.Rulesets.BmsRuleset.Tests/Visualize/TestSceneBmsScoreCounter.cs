using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Screens.Play.HUD;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsScoreCounter : BmsPlayerTestScene
{
    private ArgonScoreCounter reference = null!;

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(_ => []);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap { TotalColumns = 8 };
        beatmap.HitObjects.Add(new BmsNote { StartTime = 60000, Column = 1 });
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BurstRollingMatchesArgonIncludingRetargetAndRewind(bool hidden)
    {
        loadCounters();
        AddStep("compare rolling frames", () =>
        {
            var counters = Player.HUDOverlay.ChildrenOfType<BmsScoreCounter>().ToArray();
            var source = new ManualClock();
            var clock = new FramedClock(source);
            reference.Clock = clock;
            reference.Alpha = hidden ? 0 : 1;
            foreach (var counter in counters)
            {
                counter.Clock = clock;
                counter.Alpha = hidden ? 0 : 1;
                counter.SetCountWithoutRolling(0);
            }
            reference.SetCountWithoutRolling(0);

            burst(1000);
            foreach (var counter in counters)
                Assert.That(counter.Transforms.Count(t => t.TargetMember == nameof(counter.DisplayedCount)), Is.Zero);
            frame(0);
            frame(50);
            frame(125);
            burst(2000);
            frame(125);
            frame(200);
            frame(375);
            Assert.That(counters.Select(c => c.DisplayedCount), Is.All.EqualTo(2000));

            source.CurrentTime = 0;
            clock.ProcessFrame();
            burst(500);
            frame(0);
            frame(100);
            frame(250);
            Assert.That(counters.Select(c => c.DisplayedCount), Is.All.EqualTo(500));

            void burst(int target)
            {
                for (var i = target - 99; i <= target; i++)
                {
                    reference.Current.Value = i;
                    foreach (var counter in counters)
                        counter.Current.Value = i;
                }
            }

            void frame(double time)
            {
                source.CurrentTime = time;
                clock.ProcessFrame();
                reference.UpdateSubTree();
                foreach (var counter in counters)
                {
                    counter.UpdateSubTree();
                    Assert.That(counter.DisplayedCount, Is.EqualTo(reference.DisplayedCount), $"rolling at {time} ms");
                }
            }
        });
    }

    [Test]
    public void ImmediateCountsCancelPendingRolling()
    {
        loadCounters();
        AddStep("stop and reset pending targets", () =>
        {
            foreach (var counter in Player.HUDOverlay.ChildrenOfType<BmsScoreCounter>())
                verify(counter);

            static void verify(BmsScoreCounter counter)
            {
                counter.Current.Value = 1000;
                counter.StopRolling();
                assertImmediate(1000);
                counter.Current.Value = 2000;
                counter.SetCountWithoutRolling(700);
                assertImmediate(700);
                counter.Current.Value = 3000;
                counter.ResetCount();
                assertImmediate(0);

                void assertImmediate(long expected)
                {
                    counter.UpdateSubTree();
                    Assert.That(counter.DisplayedCount, Is.EqualTo(expected));
                    Assert.That(counter.Transforms.Count(t => t.TargetMember == nameof(counter.DisplayedCount)), Is.Zero);
                }
            }
        });
    }

    [Test]
    public void DenseBurstAllocatesLessThanNativeAnimations()
    {
        loadCounters();
        AddStep("compare rolling allocations", () =>
        {
            var nativeBytes = measure(reference);
            foreach (var counter in Player.HUDOverlay.ChildrenOfType<BmsScoreCounter>())
            {
                var batchedBytes = measure(counter);
                Assert.That(batchedBytes, Is.LessThan(nativeBytes / 2), $"batched {batchedBytes}, native {nativeBytes}");
            }

            static long measure(ArgonScoreCounter counter)
            {
                counter.Current.Value = 1;
                counter.UpdateSubTree();
                counter.SetCountWithoutRolling(0);
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 1; i <= 1000; i++)
                    counter.Current.Value = i;
                counter.UpdateSubTree();
                return GC.GetAllocatedBytesForCurrentThread() - before;
            }
        });
    }

    private void loadCounters()
    {
        AddStep("load player", () => LoadPlayer());
        AddUntilStep("score counters loaded", () => Player.IsLoaded && Player.HUDOverlay.ChildrenOfType<BmsScoreCounter>().Any(c => c.IsLoaded));
        AddStep("pause chart and add native reference", () =>
        {
            Player.GameplayClockContainer.Stop();
            Assert.That(Player.HUDOverlay.ChildrenOfType<BmsScoreCounter>().Count(), Is.EqualTo(2));
            Player.HUDOverlay.Add(reference = new ArgonScoreCounter());
        });
        AddUntilStep("native reference loaded", () => reference.IsLoaded);
    }
}
