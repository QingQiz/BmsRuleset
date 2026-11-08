#nullable enable
using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Scoring;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsResultScreenStatistics
{
    private Action? restoreStatisticLocale;

    [TearDown]
    public void RestoreStatisticLocale()
    {
        restoreStatisticLocale?.Invoke();
        restoreStatisticLocale = null;
    }

    [Test]
    public void TestGeneratedStatisticLabelsUpdateWithoutRebuildingCharts()
    {
        BmsHitOffsetStatistic offset = null!;
        BmsHitScatterStatistic scatter = null!;
        BmsTimelineStatistic timeline = null!;
        SpriteText[] labels = [];
        AddStep("load charts from generated statistics", () =>
        {
            var config = Dependencies.Get<FrameworkConfigManager>();
            var originalLocale = config.Get<string>(FrameworkSetting.Locale);
            restoreStatisticLocale = () => config.SetValue(FrameworkSetting.Locale, originalLocale);
            config.SetValue(FrameworkSetting.Locale, "en");
            var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bms5K, TotalColumns = 6 };
            var hits = createGaugeHitEvents();
            offset = new BmsHitOffsetStatistic(BmsHitOffsetStatistic.CreateStatistics(beatmap, hits));
            scatter = new BmsHitScatterStatistic(BmsHitScatterStatistic.CreateStatistics(beatmap, hits));
            timeline = new BmsTimelineStatistic(BmsTimelineStatistic.CreateData(new ScoreInfo { HitEvents = hits.ToList() }, beatmap));
            Child = new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                Content = new[] { new Drawable[] { offset, scatter, timeline } },
            };
        });
        AddUntilStep("charts loaded", () => offset.IsLoaded && scatter.IsLoaded && timeline.IsLoaded);
        AddStep("expand offset rows", () =>
        {
            InputManager.MoveMouseTo(offset);
            InputManager.Click(MouseButton.Left);
        });
        AddStep("expand scatter rows", () =>
        {
            InputManager.MoveMouseTo(scatter);
            InputManager.Click(MouseButton.Left);
        });
        AddStep("retain actual label drawables", () =>
        {
            SpriteText label(Drawable chart, LocalisableString text) => chart.ChildrenOfType<SpriteText>().First(sprite => sprite.Text == text);
            labels =
            [
                label(offset, BmsStrings.Scratch), label(offset, BmsStrings.Key(1)),
                label(scatter, BmsStrings.Scratch), label(scatter, BmsStrings.Key(1)),
                label(timeline, BmsStrings.Scratch), label(timeline, BmsStrings.Note), label(timeline, BmsStrings.Fast),
            ];
        });
        AddUntilStep("English labels displayed", () => labels.Select(label => label.Current.Value)
            .SequenceEqual(["Scratch", "Key 1", "Scratch", "Key 1", "Scratch", "note", "fast"]));
        AddStep("switch existing charts to Chinese", () => Dependencies.Get<FrameworkConfigManager>().SetValue(FrameworkSetting.Locale, "zh"));
        AddUntilStep("same label drawables display Chinese", () => labels.Select(label => label.Current.Value)
            .SequenceEqual(["皿", "按键 1", "皿", "按键 1", "皿", "单键", "偏快"]));
        AddStep("switch existing charts back to English", () => Dependencies.Get<FrameworkConfigManager>().SetValue(FrameworkSetting.Locale, "en"));
        AddUntilStep("same label drawables return to English", () => labels.Select(label => label.Current.Value)
            .SequenceEqual(["Scratch", "Key 1", "Scratch", "Key 1", "Scratch", "note", "fast"]));
    }
}
