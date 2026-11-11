using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Configuration;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Localisation;

[TestFixture]
public class BmsLocalisationTest
{
    private const string resource_prefix = "osu.Game.Rulesets.BmsRuleset.Resources.Localisation.BmsStrings";

    private TestFrameworkConfigManager config = null!;
    private LocalisationManager localisation = null!;

    [SetUp]
    public void SetUp()
    {
        config = new TestFrameworkConfigManager();
        localisation = new LocalisationManager(config);
        localisation.AddLanguage("en", new TestLocalisationStore("en"));
        localisation.AddLanguage("zh", new TestLocalisationStore("zh"));
    }

    [TearDown]
    public void TearDown()
    {
        localisation.Dispose();
        config.Dispose();
    }

    [Test]
    public void TestStatisticLabelsRetainIdentityAcrossLanguagesAndStageMerging()
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bms5K,
            TotalColumns = 6,
            HitObjects = [new BmsNote { StartTime = 1000, Column = 1 }],
        };
        var first = BmsTimelineStatistic.CreateData(new ScoreInfo(), beatmap);
        var offset = BmsHitOffsetStatistic.CreateStatistics(beatmap, []);
        var scatter = BmsHitScatterStatistic.CreateStatistics(beatmap, []);
        var key = localisation.GetLocalisedBindableString(offset.Keys[1].Label);
        var scratch = localisation.GetLocalisedBindableString(scatter.Keys[0].Label);
        var note = localisation.GetLocalisedBindableString(first.Notes.Categories.Single(category => category.Label == BmsStrings.Note).Label);
        Assert.That(new[] { key.Value, scratch.Value, note.Value }, Is.EqualTo(new[] { "Key 1", "Scratch", "note" }));

        config.SetValue(FrameworkSetting.Locale, "zh");
        Assert.That(new[] { key.Value, scratch.Value, note.Value }, Is.EqualTo(new[] { "按键 1", "皿", "单键" }));
        var second = BmsTimelineStatistic.CreateData(new ScoreInfo(), beatmap);
        second = second with
        {
            Notes = second.Notes with { Categories = second.Notes.Categories.Where(category => category.Label != BmsStrings.Scratch).ToArray() },
        };
        var combined = BmsTimelineStatistic.CreateCourseData([first, second]);
        Assert.Multiple(() =>
        {
            Assert.That(combined.Notes.Categories.Select(category => category.Label), Is.EqualTo(new[] { BmsStrings.Scratch, BmsStrings.LongNote, BmsStrings.Note }));
            Assert.That(combined.Notes.BucketWeights, Is.EqualTo(first.Notes.BucketWeights.Concat(second.Notes.BucketWeights)));
            Assert.That(combined.StageBoundaries, Is.EqualTo(new[] { 0.5f }));
            Assert.That(combined.Duration, Is.EqualTo(first.Duration + second.Duration));
            foreach (var category in first.Notes.Categories)
            {
                var merged = combined.Notes.Categories.Single(item => item.Label == category.Label);
                var next = second.Notes.Categories.SingleOrDefault(item => item.Label == category.Label);
                Assert.That(merged.Buckets, Is.EqualTo(category.Buckets.Concat(next?.Buckets ?? new int[category.Buckets.Length])));
                Assert.That(merged.Colour, Is.EqualTo(category.Colour));
            }
            Assert.That(first.FastSlow.Categories.Select(category => category.Label.GetHashCode()),
                Is.EqualTo(second.FastSlow.Categories.Select(category => category.Label.GetHashCode())));
            Assert.That(combined.FastSlow.Categories.Select(category => category.Label), Is.EqualTo(new[] { BmsStrings.Fast, BmsStrings.Slow }));
        });

        config.SetValue(FrameworkSetting.Locale, "en");
        Assert.That(new[] { key.Value, scratch.Value, note.Value }, Is.EqualTo(new[] { "Key 1", "Scratch", "note" }));
    }

    [Test]
    public void TestTextUpdatesWhenLanguageChanges()
    {
        var text = localisation.GetLocalisedBindableString(BmsStrings.ImportBmsFiles);

        Assert.That(text.Value, Is.EqualTo("Import BMS files"));

        config.SetValue(FrameworkSetting.Locale, "zh");
        Assert.That(text.Value, Is.EqualTo("导入 BMS 文件"));

        config.SetValue(FrameworkSetting.Locale, "en");
        Assert.That(text.Value, Is.EqualTo("Import BMS files"));
    }

    [Test]
    public void TestFormattedTextUsesEmbeddedChineseResource()
    {
        config.SetValue(FrameworkSetting.Locale, "zh");

        Assert.That(
            localisation.GetLocalisedString(BmsStrings.LoadedTable("Satellite", 3)),
            Is.EqualTo("已载入难度表：Satellite（3 张谱面）"));
    }

    [Test]
    public void TestSkippedChartDiagnosticsRetainDetailsWhenLanguageChanges()
    {
        const string details = "chart.bmson\nBMSON level must be a non-negative integer; received -99.";
        var text = localisation.GetLocalisedBindableString(BmsStrings.ImportSkippedCharts(19, details));
        Assert.That(text.Value, Does.StartWith("Skipped 19 unreadable or unsupported charts. Details for up to 5 charts:\n"));
        Assert.That(text.Value, Does.Contain(details));

        config.SetValue(FrameworkSetting.Locale, "zh");
        Assert.That(text.Value, Does.StartWith("已跳过 19 张无法读取或不支持的谱面，以下列出最多 5 张的原因：\n"));
        Assert.That(text.Value, Does.Contain(details));
        Assert.That(text.Value, Does.EndWith("点击打开 BMS 日志，查看完整列表及错误详情。"));
    }

    [TestCase("en", "2.000 s–3.000 s · 12.0 notes/s")]
    [TestCase("zh", "2.000 秒–3.000 秒 · 每秒 12.0 个音符")]
    public void TestPracticeMeasureAndDensityUseLocalisedNotation(string locale, string density)
    {
        config.SetValue(FrameworkSetting.Locale, locale);
        Assert.That(localisation.GetLocalisedString(BmsStrings.PracticeMeasure(12)), Is.EqualTo("#12"));
        Assert.That(localisation.GetLocalisedString(BmsStrings.PracticeDensityWindow(2000, 3000, 12)), Is.EqualTo(density));
    }

    [Test]
    public void TestFormattedInstancesKeepIndependentArgumentsWhenLanguageChanges()
    {
        var first = localisation.GetLocalisedBindableString(BmsStrings.LoadedTable("Satellite", 3));
        var second = localisation.GetLocalisedBindableString(BmsStrings.LoadedTable("Stella", 7));

        config.SetValue(FrameworkSetting.Locale, "zh");
        Assert.That(first.Value, Is.EqualTo("已载入难度表：Satellite（3 张谱面）"));
        Assert.That(second.Value, Is.EqualTo("已载入难度表：Stella（7 张谱面）"));

        var chinese = second.Value;
        config.SetValue(FrameworkSetting.Locale, "en");
        Assert.That(first.Value, Does.Contain("Satellite").And.Contain("3"));
        Assert.That(second.Value, Does.Contain("Stella").And.Contain("7"));
        Assert.That(second.Value, Is.Not.EqualTo(chinese));

        config.SetValue(FrameworkSetting.Locale, "zh");
        Assert.That(second.Value, Is.EqualTo(chinese));
    }

    [Test]
    public void TestSharedTemplatesSupportConcurrentIndependentReaders()
    {
        var results = new string[1000];

        Parallel.For(0, results.Length, i =>
        {
            // Descriptors retain their own arguments even when readers share immutable templates.
            results[i] = BmsStrings.LoadedTable("Satellite", i).ToString();
        });

        for (var i = 0; i < results.Length; i++)
            Assert.That(results[i], Is.EqualTo(BmsStrings.LoadedTable("Satellite", i).ToString()));
    }

    [Test]
    public void TestStandardDeviationUpdatesWhenLanguageChanges()
    {
        var text = localisation.GetLocalisedBindableString(BmsStrings.StandardDeviation(12.34));

        Assert.That(text.Value, Is.EqualTo("SD 12.3 ms"));

        config.SetValue(FrameworkSetting.Locale, "zh");
        Assert.That(text.Value, Is.EqualTo("标准差 12.3 ms"));
    }

    [Test]
    public void TestNoGoodAndNoGreatDescriptionsUseLocalisation()
    {
        var noGood = new BmsModNoGood();
        var noGreat = new BmsModNoGreat();

        Assert.That(localisation.GetLocalisedString(noGood.Description), Is.EqualTo("GOOD counts as BAD."));
        Assert.That(localisation.GetLocalisedString(noGreat.Description), Is.EqualTo("GREAT and GOOD count as BAD."));

        config.SetValue(FrameworkSetting.Locale, "zh");

        Assert.That(localisation.GetLocalisedString(noGood.Description), Is.EqualTo("将 GOOD 计为 BAD。"));
        Assert.That(localisation.GetLocalisedString(noGreat.Description), Is.EqualTo("将 GREAT 和 GOOD 计为 BAD。"));
    }

    [TestCase(-60, "just now", "刚刚")]
    [TestCase(59, "just now", "刚刚")]
    [TestCase(60, "1min ago", "1分钟前")]
    [TestCase(300, "5min ago", "5分钟前")]
    [TestCase(3599, "59min ago", "59分钟前")]
    [TestCase(3600, "1hr ago", "1小时前")]
    [TestCase(86400, "1d ago", "1天前")]
    [TestCase(2678400, "1mo ago", "1个月前")]
    [TestCase(31536000, "1yr ago", "1年前")]
    public void TestLeaderboardTimeAgoUpdatesWhenLanguageChanges(int seconds, string english, string chinese)
    {
        var now = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        var date = now.AddSeconds(-seconds).ToOffset(TimeSpan.FromHours(8));
        var text = localisation.GetLocalisedBindableString(BmsStrings.LeaderboardTimeAgo(date, now));

        Assert.That(text.Value, Is.EqualTo(english));

        config.SetValue(FrameworkSetting.Locale, "zh");
        Assert.That(text.Value, Is.EqualTo(chinese));
    }

    [Test]
    public void TestResourceKeysAndFormatArgumentsMatch()
    {
        var english = getResourceValues(resource_prefix);
        var chinese = getResourceValues($"{resource_prefix}.zh");

        Assert.That(english, Is.Not.Empty);
        Assert.That(chinese.Keys, Is.EquivalentTo(english.Keys));

        Assert.Multiple(() =>
        {
            foreach (var (key, englishValue) in english)
            {
                var englishFormat = CompositeFormat.Parse(englishValue);
                var chineseFormat = CompositeFormat.Parse(chinese[key]);

                Assert.That(chineseFormat.MinimumArgumentCount, Is.EqualTo(englishFormat.MinimumArgumentCount), $"Format arguments differ for '{key}'");
            }
        });
    }

    [TestCase("en", "Play with LR2 judgement, long-note and gauge rules.")]
    [TestCase("zh", "使用 LR2 的判定、长条和血量规则游玩。")]
    public void TestLR2DescriptionUsesLocalisation(string locale, string expected)
    {
        config.SetValue(FrameworkSetting.Locale, locale);
        Assert.That(localisation.GetLocalisedString(new BmsModLr2().Description), Is.EqualTo(expected));
    }

    private static IReadOnlyDictionary<string, string> getResourceValues(string resourceName)
    {
        var manager = new ResourceManager(resourceName, typeof(BmsStrings).Assembly);
        var resources = manager.GetResourceSet(CultureInfo.InvariantCulture, true, true)!;

        return resources.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
    }

    private sealed class TestFrameworkConfigManager() : FrameworkConfigManager(null)
    {
        protected override string Filename => null;

        protected override void InitialiseDefaults()
        {
            SetDefault(FrameworkSetting.Locale, "en");
            SetDefault(FrameworkSetting.ShowUnicode, true);
        }
    }

    private sealed class TestLocalisationStore(string culture) : ILocalisationStore
    {
        public CultureInfo EffectiveCulture { get; } = CultureInfo.GetCultureInfo(culture);

        public string Get(string name) => null;

        public Task<string> GetAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult<string>(null);

        public Stream GetStream(string name) => null;

        public IEnumerable<string> GetAvailableResources() => [];

        public void Dispose()
        {
        }
    }
}
