#nullable enable

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Game.Configuration;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.UI.Practice;
using osu.Game.Tests.Beatmaps;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsPracticeSetup
{
    private Action? restoreScalePreferences;

    [TearDown]
    public void RestorePracticeScalePreferences()
    {
        var restore = restoreScalePreferences;
        restoreScalePreferences = null;
        if (restore != null)
            Schedule(restore);
    }

    [TestCase(1920, 1080, "en")]
    [TestCase(1920, 1080, "zh")]
    [TestCase(1024, 768, "en")]
    [TestCase(1024, 768, "zh")]
    [TestCase(760, 900, "en")]
    [TestCase(760, 900, "zh")]
    public void RepeatLabelAndControlsFitNativeUIScales(int width, int height, string locale)
    {
        BmsPracticeSetupOverlay setup = null!;
        ScalingContainer.ScalingDrawSizePreservingFillContainer scaling = null!;
        OsuScrollContainer scroll = null!;
        OsuTextFlowContainer label = null!;
        OsuConfigManager config = null!;
        BmsPracticeSettings originalSettings = null!;

        AddStep("open practice under the native osu UI scale container", () =>
        {
            config = Dependencies.Get<OsuConfigManager>();
            var frameworkConfig = Dependencies.Get<FrameworkConfigManager>();
            var originalScale = config.Get<float>(OsuSetting.UIScale);
            var originalLocale = frameworkConfig.Get<string>(FrameworkSetting.Locale);
            restoreScalePreferences = () =>
            {
                config.SetValue(OsuSetting.UIScale, originalScale);
                frameworkConfig.SetValue(FrameworkSetting.Locale, originalLocale);
            };
            frameworkConfig.SetValue(FrameworkSetting.Locale, locale);
            config.SetValue(OsuSetting.UIScale, 1f);
            Ruleset.Value = new BmsRuleset().RulesetInfo;
            var beatmap = BmsTestBeatmaps.CreateBeatmap();
            Child = new Container
            {
                Size = new Vector2(width, height),
                Scale = new Vector2(Math.Min(Content.DrawWidth / width, Content.DrawHeight / height)),
                Child = scaling = new ScalingContainer.ScalingDrawSizePreservingFillContainer(true)
                {
                    Child = setup = new BmsPracticeSetupOverlay(new TestWorkingBeatmap(beatmap, audioManager: Audio), [],
                        BmsPracticeSettings.CreateDefault(beatmap) with { SkipResults = true, CountIn = 4 }),
                },
            };
            setup.Show();
        });
        AddUntilStep("practice ready", () => setup.IsReady && setup.CountInGroup.DrawHeight > 60);
        AddStep("find scrollable label and remember settings", () =>
        {
            scroll = setup.ChildrenOfType<OsuScrollContainer>().Single(container => container.ChildrenOfType<OsuTextBox>().Contains(setup.StartTimeInput));
            label = setup.SkipResultsInput.ChildrenOfType<OsuTextFlowContainer>().Single();
            originalSettings = setup.Settings;
        });

        foreach (var scale in new[] { 0.8f, 1f, 1.25f, 1.6f, 0.8f })
        {
            AddStep($"set UI scale {scale:P0}", () => config.SetValue(OsuSetting.UIScale, scale));
            AddUntilStep("native scale settles", () => Math.Abs(scaling.Scale.X - scale) < 0.0001f);
            AddWaitStep("layout settles after scale transition", 2);
            AddStep("scroll repeat option into view", () => scroll.ScrollIntoView(setup.SkipResultsInput, false));
            AddWaitStep("scroll position settles", 2);
            AddStep("full label fits beside its switch", () =>
            {
                var texts = label.ChildrenOfType<SpriteText>().Where(text => text.DrawWidth > 0).ToArray();
                var checkboxBounds = setup.SkipResultsInput.ScreenSpaceDrawQuad.AABBFloat;
                var switchBounds = setup.SkipResultsInput.ChildrenOfType<Nub>().Single().ScreenSpaceDrawQuad.AABBFloat;
                Assert.That(texts, Is.Not.Empty);
                foreach (var text in texts)
                {
                    var bounds = text.ScreenSpaceDrawQuad.AABBFloat;
                    Assert.That(bounds.Left, Is.GreaterThanOrEqualTo(checkboxBounds.Left - 0.1f), text.Text.ToString());
                    Assert.That(bounds.Top, Is.GreaterThanOrEqualTo(checkboxBounds.Top - 0.1f), text.Text.ToString());
                    Assert.That(bounds.Bottom, Is.LessThanOrEqualTo(checkboxBounds.Bottom + 0.1f), text.Text.ToString());
                    Assert.That(bounds.Right, Is.LessThan(switchBounds.Left), text.Text.ToString());
                }
            });
            AddStep("repeat control is visible and start remains accessible", () =>
            {
                var scrollBounds = scroll.ScreenSpaceDrawQuad.AABBFloat;
                var checkboxBounds = setup.SkipResultsInput.ScreenSpaceDrawQuad.AABBFloat;
                Assert.That(checkboxBounds.Top, Is.GreaterThanOrEqualTo(scrollBounds.Top - 0.1f), $"checkbox {checkboxBounds}; scroll {scrollBounds}; scale {scale}");
                Assert.That(checkboxBounds.Bottom, Is.LessThanOrEqualTo(scrollBounds.Bottom + 0.1f), $"checkbox {checkboxBounds}; scroll {scrollBounds}; scale {scale}");
                Assert.That(setup.ScreenSpaceDrawQuad.AABBFloat.Contains(setup.StartButton.ScreenSpaceDrawQuad.AABBFloat), Is.True, "start button outside setup");
                Assert.That(setup.SelectionChart.DrawHeight, Is.GreaterThanOrEqualTo(100), $"chart height at scale {scale}");
                Assert.That(scrollBounds.Bottom, Is.LessThan(setup.StartButton.ScreenSpaceDrawQuad.AABBFloat.Top), "scroll overlaps actions");
            });
            AddStep("click full label to disable automatic repeats", () =>
            {
                InputManager.MoveMouseTo(label.ChildrenOfType<SpriteText>().First(text => text.DrawWidth > 0));
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("count-in collapses at this scale", () => !setup.Settings.SkipResults && !setup.CountInGroup.IsPresent
                && setup.CountInGroup.DrawHeight == 0);
            AddStep("scroll folded option into view", () => scroll.ScrollIntoView(setup.SkipResultsInput, false));
            AddStep("click switch to restore automatic repeats", () =>
            {
                InputManager.MoveMouseTo(setup.SkipResultsInput.ChildrenOfType<Nub>().Single());
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("count-in expands at this scale", () => setup.Settings.SkipResults && setup.CountInGroup.DrawHeight > 60
                && !setup.CountInGroup.Transforms.Any());
            AddStep("scroll count-in choices into view", () => scroll.ScrollIntoView(setup.CountInGroup, false));
            AddStep("count-in choices stay separated from label and actions", () =>
            {
                var checkboxBounds = setup.SkipResultsInput.ScreenSpaceDrawQuad.AABBFloat;
                var countInBounds = setup.CountInGroup.ScreenSpaceDrawQuad.AABBFloat;
                Assert.That(countInBounds.Left >= checkboxBounds.Right - 0.1f || countInBounds.Top >= checkboxBounds.Bottom - 0.1f,
                    Is.True, $"checkbox {checkboxBounds}; count-in {countInBounds}");
                var scrollBounds = scroll.ScreenSpaceDrawQuad.AABBFloat;
                foreach (var button in setup.CountInButtons)
                {
                    var bounds = button.ScreenSpaceDrawQuad.AABBFloat;
                    Assert.That(bounds.Top, Is.GreaterThanOrEqualTo(scrollBounds.Top - 0.1f), $"{button.Text}: {bounds}; scroll {scrollBounds}");
                    Assert.That(bounds.Bottom, Is.LessThanOrEqualTo(scrollBounds.Bottom + 0.1f), $"{button.Text}: {bounds}; scroll {scrollBounds}");
                }
                Assert.That(setup.CountInButtons.Zip(setup.CountInButtons.Skip(1))
                    .All(pair => pair.First.ScreenSpaceDrawQuad.AABBFloat.Right < pair.Second.ScreenSpaceDrawQuad.AABBFloat.Left), Is.True);
            });
            AddAssert("UI scale preserves selected range and countdown", () => setup.Settings, () => Is.EqualTo(originalSettings));
        }

        AddStep("restore preferences", RestorePracticeScalePreferences);
    }
}
