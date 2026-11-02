#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.UI.Practice;
using osu.Game.Tests.Beatmaps;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsPracticeSetup
{
    [TestCase(1280, 720, false, false)]
    [TestCase(1280, 720, true, false)]
    [TestCase(1024, 600, false, true)]
    [TestCase(1024, 600, true, true)]
    [TestCase(760, 900, false, false)]
    [TestCase(760, 900, true, false)]
    [TestCase(760, 600, false, true)]
    [TestCase(760, 600, true, true)]
    public void HidingRepeatSettingsKeepsOtherControlsMovingSmoothly(int width, int height, bool zeroCountIn, bool scrollToEnd)
    {
        BmsPracticeSetupOverlay setup = null!;
        FoldMotionProbe probe = null!;
        OsuScrollContainer scroll = null!;
        Drawable summary = null!;
        Container settingsPanel = null!;
        var clock = new ManualClock { CurrentTime = 10000 };
        var samples = new List<FoldMotionSample>();
        var finished = false;

        FoldMotionSample sample(double elapsed) => new(elapsed,
            setup.CountInGroup.DrawHeight,
            setup.StartTimeInput.ToSpaceOfOtherDrawable(Vector2.Zero, setup).Y,
            setup.SkipResultsInput.ToSpaceOfOtherDrawable(Vector2.Zero, setup).Y,
            summary.ToSpaceOfOtherDrawable(Vector2.Zero, setup).Y,
            settingsPanel.Y, settingsPanel.Height,
            setup.StartButton.ToSpaceOfOtherDrawable(Vector2.Zero, setup).Y,
            setup.StartTimeInput.DrawWidth,
            setup.StartTimeInput.ToSpaceOfOtherDrawable(Vector2.Zero, setup).X,
            scroll.Current, scroll.ScrollableExtent, scroll.AvailableContent,
            setup.SelectionChart.DrawHeight);

        AddStep("open settings on a controlled frame clock", () =>
        {
            var beatmap = BmsTestBeatmaps.CreateBeatmap();
            beatmap.HitObjects =
            [
                new BmsNote { StartTime = 1000, Column = 1, Beatmap = beatmap },
                new BmsNote { StartTime = 3900, Column = 1, Beatmap = beatmap },
            ];
            Ruleset.Value = new BmsRuleset().RulesetInfo;
            Child = probe = new FoldMotionProbe
            {
                Size = new Vector2(width, height),
                Scale = new Vector2(0.5f),
                Clock = new FramedClock(clock),
                Child = setup = new BmsPracticeSetupOverlay(new TestWorkingBeatmap(beatmap, audioManager: Audio), [],
                    new BmsPracticeSettings { StartTime = 1000, EndTime = 4000, SkipResults = true, CountIn = zeroCountIn ? 0 : 3 }),
            };
            probe.AfterLayout = () => clock.CurrentTime += 1000d / 60;
            setup.Show();
        });
        AddUntilStep("initial settings are ready", () => setup.IsReady && setup.CountInGroup.DrawHeight > 60);
        AddStep("settle initial transforms and find neighbouring controls", () =>
        {
            clock.CurrentTime += 1000;
            scroll = setup.ChildrenOfType<OsuScrollContainer>().Single(container => container.ChildrenOfType<OsuTextBox>().Contains(setup.StartTimeInput));
            settingsPanel = setup.ChildrenOfType<Container>().Single(container => container.Name == "Practice settings panel");
            var fields = (FillFlowContainer)setup.CountInGroup.Parent!.Parent!;
            summary = fields.Children.SkipWhile(child => child != setup.CountInGroup.Parent).Skip(1).First();
        });
        AddWaitStep("layout has settled", 4);
        AddStep("choose initial scroll position", () =>
        {
            if (scrollToEnd)
                scroll.ScrollToEnd(false);
        });
        AddWaitStep("scroll has settled", 3);
        AddStep("record every rendered layout frame while hiding", () =>
        {
            samples.Add(sample(0));
            var start = clock.CurrentTime;
            setup.SkipResultsInput.Current.Value = false;
            probe.AfterLayout = () =>
            {
                samples.Add(sample(clock.CurrentTime - start));
                if (clock.CurrentTime - start >= 1000)
                {
                    probe.AfterLayout = null;
                    finished = true;
                }
                else
                    clock.CurrentTime += 1000d / 60;
            };
        });
        AddUntilStep("one second of frame evidence collected", () => finished);
        AddStep("save and inspect the motion trace", () =>
        {
            var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"practice-fold-{width}-{height}-{zeroCountIn}-{scrollToEnd}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = true }));
            TestContext.AddTestAttachment(path, "Practice settings collapse sampled at 60 frames per second");
            Assert.That(samples.Count, Is.GreaterThan(50));
            Assert.That(samples[^1].CountInHeight, Is.Zero);
            assertNoReverseMotion(samples.Select(frame => frame.CountInHeight), "count-in height");
            assertNoReverseMotion(samples.Select(frame => frame.PanelTop), "settings panel");
            assertNoReverseMotion(samples.Select(frame => frame.SummaryTop), "range summary");
            assertNoReverseMotion(samples.Select(frame => frame.ChartHeight), "chart height");
            Assert.That(samples.Max(frame => frame.InputWidth) - samples.Min(frame => frame.InputWidth), Is.LessThan(0.1), "input width must stay fixed");
            Assert.That(samples.Max(frame => frame.InputLeft) - samples.Min(frame => frame.InputLeft), Is.LessThan(0.1), "input must not shift horizontally");
            Assert.That(samples.Max(frame => frame.StartButtonTop) - samples.Min(frame => frame.StartButtonTop), Is.LessThan(0.1), "fixed actions must not move");
            var panelTravel = Math.Abs(samples[^1].PanelTop - samples[0].PanelTop);
            Assert.That(largestFrameMovement(samples.Select(frame => frame.PanelTop)), Is.LessThan(Math.Max(1, panelTravel * 0.35)), "panel must not jump in a single frame");
            Assert.That(largestFrameMovement(samples.Where(frame => frame.Time >= 200).Select(frame => frame.PanelTop)), Is.LessThan(1), "panel must not snap at the end of the fold");
            Assert.That(largestFrameMovement(samples.Where(frame => frame.Time >= 200).Select(frame => frame.SummaryTop)), Is.LessThan(1), "summary must not snap at the end of the fold");
            if (width >= 900 && !scrollToEnd)
                Assert.That(samples.Max(frame => frame.InputTop) - samples.Min(frame => frame.InputTop), Is.LessThan(0.1), "unrelated top fields must stay fixed");
        });
    }

    private static double largestFrameMovement(IEnumerable<double> values)
    {
        var frames = values.ToArray();
        return frames.Zip(frames.Skip(1), (before, after) => Math.Abs(after - before)).Max();
    }

    private static void assertNoReverseMotion(IEnumerable<double> values, string name)
    {
        var frames = values.ToArray();
        var increasing = frames[^1] >= frames[0];
        var backwards = frames.Zip(frames.Skip(1), (before, after) => increasing ? before - after : after - before).Max();
        Assert.That(backwards, Is.LessThan(0.2), $"{name} must not reverse while collapsing");
    }

    private sealed record FoldMotionSample(double Time, double CountInHeight, double InputTop, double CheckboxTop, double SummaryTop,
        double PanelTop, double PanelHeight, double StartButtonTop, double InputWidth, double InputLeft,
        double Scroll, double ScrollExtent, double ContentHeight, double ChartHeight);

    private partial class FoldMotionProbe : Container
    {
        internal Action? AfterLayout;

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();
            AfterLayout?.Invoke();
        }
    }
}
