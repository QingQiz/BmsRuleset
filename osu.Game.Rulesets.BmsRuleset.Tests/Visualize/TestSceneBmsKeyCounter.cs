using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.HUD;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsKeyCounter : BmsPlayerTestScene
{
    private BmsKeyCounter counter = null!;
    private ArgonKeyCounter reference = null!;
    private TestTrigger trigger = null!;
    private TestTrigger referenceTrigger = null!;
    private ManualClock source = null!;
    private FramedClock clock = null!;
    private readonly List<bool> states = [];
    private readonly List<bool> referenceStates = [];

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(_ => []);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap { TotalColumns = 8 };
        beatmap.HitObjects.Add(new BmsNote { StartTime = 60000, Column = 1 });
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void BurstsAndRetargetsMatchArgon(bool hidden, bool reverse)
    {
        loadCounters(hidden);
        AddStep("compare input bursts and animations", () =>
        {
            var forward = !reverse;
            frame(0);
            burst(false);
            frames(0, 5, 10, 30, 80, 260);

            setTime(300);
            press(forward);
            frames(300, 305, 320, 350);
            burst(false);
            frames(350, 355, 380, 450, 650);

            setTime(700);
            burst(true);
            frames(700, 705, 710, 730, 780);
            release(forward);
            frames(780, 800, 900, 1100);

            void burst(bool endPressed)
            {
                for (var i = 0; i < 100; i++)
                {
                    press(forward);
                    release(forward);
                }
                if (endPressed)
                    press(forward);
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MultipleEventTimesAndDelayedUpdatesMatchArgon(bool delayed)
    {
        loadCounters();
        AddStep("compare events across clock changes", () =>
        {
            frame(0);
            press();
            if (!delayed) frame(0);
            setTime(4);
            release();
            if (!delayed) frame(4);
            setTime(8);
            press();
            if (!delayed) frame(8);
            setTime(14);
            release();
            frames(50, 100, 300);

            setTime(20);
            press(false);
            release(false);
            frames(20, 25, 60, 300);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InitiallyHeldTriggerAndDefaultPlayerLayout(bool held)
    {
        loadCounters(initiallyHeld: held);
        AddStep("compare initial state and native layout", () =>
        {
            frames(0, 5, 30, 80);
            release();
            frames(80, 100, 200, 400);
            var display = Player.HUDOverlay.ChildrenOfType<BmsKeyCounterDisplay>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(display.ChildrenOfType<KeyCounter>(), Is.All.InstanceOf<BmsKeyCounter>());
                Assert.That(display.Anchor, Is.EqualTo(Anchor.BottomRight));
                Assert.That(display.Origin, Is.EqualTo(Anchor.BottomRight));
                Assert.That(display.Position, Is.EqualTo(new Vector2(-36, -66)));
                Assert.That(display.UsesFixedAnchor, Is.True);
            });
        });
    }

    [Test]
    public void DenseBurstAllocatesLessThanNativeAnimations()
    {
        loadCounters();
        AddStep("compare burst allocations", () =>
        {
            press();
            release();
            frame(0);
            var batchedBytes = measure(trigger);
            counter.UpdateSubTree();
            var nativeBytes = measure(referenceTrigger);
            reference.UpdateSubTree();
            assertState();
            Assert.That(batchedBytes, Is.LessThan(nativeBytes / 2), $"batched {batchedBytes}, native {nativeBytes}");

            static long measure(TestTrigger input)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 1000; i++)
                {
                    input.Press();
                    input.Release();
                }
                return GC.GetAllocatedBytesForCurrentThread() - before;
            }
        });
    }

    private void loadCounters(bool hidden = false, bool initiallyHeld = false)
    {
        AddStep("load player", () => LoadPlayer());
        AddUntilStep("key display loaded", () => Player.IsLoaded && Player.HUDOverlay.ChildrenOfType<BmsKeyCounterDisplay>().Any(c => c.IsLoaded));
        AddStep("create reference counters", () =>
        {
            Player.GameplayClockContainer.Stop();
            source = new ManualClock();
            clock = new FramedClock(source);
            trigger = new TestTrigger();
            referenceTrigger = new TestTrigger();
            if (initiallyHeld)
            {
                trigger.Press();
                referenceTrigger.Press();
            }

            counter = new BmsKeyCounter(trigger) { Clock = clock, Alpha = hidden ? 0 : 1 };
            reference = new ArgonKeyCounter(referenceTrigger) { Clock = clock, Alpha = hidden ? 0 : 1 };
            states.Clear();
            referenceStates.Clear();
            counter.IsActive.BindValueChanged(e => states.Add(e.NewValue));
            reference.IsActive.BindValueChanged(e => referenceStates.Add(e.NewValue));
            AddRange([counter, reference]);
        });
        AddUntilStep("reference counters loaded", () => counter.IsLoaded && reference.IsLoaded);
    }

    private void setTime(double time)
    {
        source.CurrentTime = time;
        clock.ProcessFrame();
    }

    private void press(bool forward = true)
    {
        trigger.Press(forward);
        referenceTrigger.Press(forward);
        assertState();
    }

    private void release(bool forward = true)
    {
        trigger.Release(forward);
        referenceTrigger.Release(forward);
        assertState();
    }

    private void assertState()
    {
        Assert.That(counter.IsActive.Value, Is.EqualTo(reference.IsActive.Value));
        Assert.That(counter.CountPresses.Value, Is.EqualTo(reference.CountPresses.Value));
        Assert.That(states, Is.EqualTo(referenceStates));
    }

    private void frames(params double[] times)
    {
        foreach (var time in times)
            frame(time);
    }

    private void frame(double time)
    {
        setTime(time);
        counter.UpdateSubTree();
        reference.UpdateSubTree();
        assertState();
        var indicator = counter.ChildrenOfType<Circle>().Single();
        var expectedIndicator = reference.ChildrenOfType<Circle>().Single();
        Assert.That(indicator.Y, Is.EqualTo(expectedIndicator.Y).Within(0.00001), $"indicator Y at {time}");
        Assert.That(indicator.Alpha, Is.EqualTo(expectedIndicator.Alpha).Within(0.00001), $"indicator alpha at {time}");
        var texts = counter.ChildrenOfType<OsuSpriteText>().ToArray();
        var expectedTexts = reference.ChildrenOfType<OsuSpriteText>().ToArray();
        for (var i = 0; i < texts.Length; i++)
        {
            Assert.That(texts[i].Colour, Is.EqualTo(expectedTexts[i].Colour), $"text {i} colour at {time}");
            Assert.That(texts[i].Text, Is.EqualTo(expectedTexts[i].Text), $"text {i} at {time}");
        }
    }

    private partial class TestTrigger() : InputTrigger("K")
    {
        public void Press(bool forward = true) => Activate(forward);

        public void Release(bool forward = true) => Deactivate(forward);
    }
}
