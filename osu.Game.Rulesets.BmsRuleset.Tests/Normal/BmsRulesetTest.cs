using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Difficulty;
using osu.Game.Rulesets.BmsRuleset.Editor;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Mods.LongNoteMode;
using osu.Game.Rulesets.BmsRuleset.Result;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.Settings;
using osu.Game.Rulesets.BmsRuleset.SongSelect;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Tests.Beatmaps;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal;

[TestFixture]
public class BmsRulesetTest
{

    [SetUp]
    public void SetUp()
    {
        ruleset = new BmsRuleset();
    }

    private BmsRuleset ruleset = null!;

    [Test]
    public void TestAutomationModsIncludeAutoplay()
    {
        var mods = ruleset.GetModsFor(ModType.Automation).ToArray();

        Assert.That(mods.OfType<BmsModAutoplay>().SingleOrDefault(), Is.Not.Null);
    }

    [Test]
    public void TestCreateBeatmapConverter()
    {
        var converter = ruleset.CreateBeatmapConverter(new Beatmap());

        Assert.That(converter, Is.TypeOf<BmsBeatmapConverter>());
    }

    [Test]
    public void TestCreateConfig()
    {
        var config = ruleset.CreateConfig(null);

        Assert.That(config, Is.TypeOf<BmsRulesetConfigManager>());
    }

    [Test]
    public void TestDedicatedPreviewAudioIsEnabledByDefault()
    {
        using var config = new BmsRulesetConfigManager(null, ruleset.RulesetInfo);

        Assert.That(config.Get<bool>(BmsRulesetSetting.UseDedicatedPreviewAudio), Is.True);
    }

    [Test]
    public void TestFrameRateUnlockIsDisabledByDefault()
    {
        using var config = new BmsRulesetConfigManager(null, ruleset.RulesetInfo);

        Assert.That(config.Get<bool>(BmsRulesetSetting.UnlockFrameRateLimit), Is.False);
    }

    [Test]
    public void TestVisualOffsetDefaultsAndRange()
    {
        using var config = new BmsRulesetConfigManager(null, ruleset.RulesetInfo);

        Assert.That(config.Get<double>(BmsRulesetSetting.VisualOffset), Is.Zero);
        Assert.That(config.Get<bool>(BmsRulesetSetting.AutomaticallyAdjustVisualOffset), Is.False);

        config.SetValue(BmsRulesetSetting.VisualOffset, 1000d);
        Assert.That(config.Get<double>(BmsRulesetSetting.VisualOffset), Is.EqualTo(BmsRulesetConfigManager.MAX_VISUAL_OFFSET));

        config.SetValue(BmsRulesetSetting.VisualOffset, -1000d);
        Assert.That(config.Get<double>(BmsRulesetSetting.VisualOffset), Is.EqualTo(BmsRulesetConfigManager.MIN_VISUAL_OFFSET));
    }

    [Test]
    public void TestAutomaticVisualOffsetAppliesSuggestion()
    {
        using var config = new BmsRulesetConfigManager(null, ruleset.RulesetInfo);
        BmsRulesetRuntime.VisualOffsetSuggestions.Clear();

        try
        {
            config.SetValue(BmsRulesetSetting.VisualOffset, 10d);
            config.SetValue(BmsRulesetSetting.AutomaticallyAdjustVisualOffset, true);

            Assert.That(BmsDrawableRuleset.AddVisualOffsetSuggestion(config, 20), Is.EqualTo(30));
            Assert.That(config.Get<double>(BmsRulesetSetting.VisualOffset), Is.EqualTo(30));
        }
        finally
        {
            BmsRulesetRuntime.VisualOffsetSuggestions.Clear();
        }
    }

    [Test]
    public void TestDedicatedPreviewAudioSettingUpdatesCurrentValue()
    {
        using var config = (BmsRulesetConfigManager)ruleset.CreateConfig(null);

        try
        {
            config.SetValue(BmsRulesetSetting.UseDedicatedPreviewAudio, false);
            Assert.That(BmsRulesetRuntime.UseDedicatedPreviewAudio, Is.False);
        }
        finally
        {
            config.SetValue(BmsRulesetSetting.UseDedicatedPreviewAudio, true);
        }
    }

    [Test]
    public void TestCreateDifficultyCalculatorUsesNativeBmsImplementation()
    {
        var working = new TestWorkingBeatmap(new BmsBeatmap());
        var calculator = ruleset.CreateDifficultyCalculator(working);

        Assert.That(calculator, Is.TypeOf<BmsDifficultyCalculator>());
    }

    [Test]
    public void TestCreateDrawableRulesetUsesBeatmapColumnCount()
    {
        var beatmap = new BmsBeatmap
        {
            TotalColumns = 16,
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 15 },
            },
        };

        var drawableRuleset = (BmsDrawableRuleset)ruleset.CreateDrawableRulesetWith(beatmap);

        Assert.That(((BmsPlayfield)drawableRuleset.Playfield).TotalColumns, Is.EqualTo(16));
    }

    [Test]
    public void TestCreateDrawableRulesetUsesNativeBmsImplementation()
    {
        var drawableRuleset = ruleset.CreateDrawableRulesetWith(new BmsBeatmap());

        Assert.That(drawableRuleset, Is.TypeOf<BmsDrawableRuleset>());
    }

    [Test]
    public void TestCreateHealthProcessor()
    {
        var processor = ruleset.CreateHealthProcessor(0);

        Assert.That(processor, Is.TypeOf<BmsHealthProcessor>());
    }

    [Test]
    public void TestCreateIcon()
    {
        var icon = ruleset.CreateIcon();

        Assert.That(icon, Is.TypeOf<BmsRulesetIcon>());
    }

    [Test]
    public void TestCreateScoreProcessor()
    {
        var processor = ruleset.CreateScoreProcessor();

        Assert.That(processor, Is.TypeOf<BmsScoreProcessor>());
    }

    [Test]
    public void TestCreateSettingsIsNotManiaSettings()
    {
        var settings = ruleset.CreateSettings();

        Assert.That(settings, Is.Not.Null);
        Assert.That(settings, Is.TypeOf<BmsSettingsSubsection>());
    }

    [Test]
    public void TestInitialisationInstallsEditorDisablePatch()
    {
        Assert.That(BmsEditorPatcher.IsInstalled, Is.True);
    }

    [Test]
    public void TestInitialisationInstallsReplayPatch()
    {
        Assert.That(BmsReplayPatcher.IsInstalled, Is.True);
    }

    [Test]
    public void TestReplayPatchAllowsUnloadedFailIndicatorDisposal()
    {
        var indicator = new ReplayFailIndicator(new GameplayClockContainer(new TrackVirtual(60000), false, false));

        Assert.DoesNotThrow(() => indicator.Dispose());
    }

    [Test]
    public void TestInitialisationInstallsRankingHitResultColourPatch()
    {
        Assert.That(BmsRankingHitResultColourPatcher.IsInstalled, Is.True);
    }

    [Test]
    public void TestInitialisationInstallsLocalLeaderboardPatch()
    {
        Assert.That(BmsLocalLeaderboardPatcher.IsInstalled, Is.True);
    }

    [Test]
    public void TestInitialisationInstallsConvertedBeatmapFilterPatch()
    {
        Assert.That(BmsConvertedBeatmapFilterPatcher.IsInstalled, Is.True);
    }

    [Test]
    public void TestEditorDisablePatchPostsNotification()
    {
        var dependencies = new DependencyContainer();
        var notifications = new TestNotificationOverlay();
        dependencies.CacheAs<INotificationOverlay>(notifications);

        BmsEditorPatcher.PostEditorUnavailableNotification(dependencies);

        Assert.That(notifications.PostedNotifications.Single(), Is.TypeOf<SimpleNotification>());
        Assert.That(notifications.PostedNotifications.Single().Text.ToString(), Is.EqualTo("The BMS editor is not supported yet."));
    }

    [Test]
    public void TestEmptyPoorDisplayNameIsEPoor()
    {
        Assert.That(ruleset.GetDisplayNameForHitResult(HitResult.Miss).ToString(), Is.EqualTo("E-POOR"));
    }

    [Test]
    public void TestFrameworkHitResultColoursRemainOsuDefaults()
    {
        var colours = new OsuColour();

        Assert.That(colours.ForHitResult(HitResult.Good), Is.EqualTo(colours.GreenLight));
        Assert.That(colours.ForHitResult(HitResult.Ok), Is.EqualTo(colours.Green));
        Assert.That(colours.ForHitResult(HitResult.Meh), Is.EqualTo(colours.Yellow));
        Assert.That(colours.ForHitResult(HitResult.Miss), Is.EqualTo(colours.Red));
    }

    [Test]
    public void TestBmsHitResultColoursUseBmsJudgementSemantics()
    {
        var colours = new OsuColour();

        Assert.That(BmsHitResultColours.ForHitResult(HitResult.Good), Is.EqualTo(colours.Green));
        Assert.That(BmsHitResultColours.ForHitResult(HitResult.Ok), Is.EqualTo(colours.Yellow));
        Assert.That(BmsHitResultColours.ForHitResult(HitResult.Meh), Is.EqualTo(colours.Red));
        Assert.That(BmsHitResultColours.ForHitResult(HitResult.Miss), Is.EqualTo(Color4.Gray));
    }

    [Test]
    public void TestScoreResultColoursUseBmsSemanticsOnlyForBmsScores()
    {
        var colours = new OsuColour();

        Assert.That(BmsHitResultColours.ForScore(new ScoreInfo { Ruleset = new RulesetInfo { ShortName = Constant.SHORT_NAME } }, HitResult.Meh), Is.EqualTo(colours.Red));
        Assert.That(BmsHitResultColours.ForScore(new ScoreInfo { Ruleset = new RulesetInfo { ShortName = "mania" } }, HitResult.Meh), Is.EqualTo(colours.Yellow));
    }

    [Test]
    public void TestFunModsContainAllThreeLongNoteModeMods()
    {
        var mods = ruleset.GetModsFor(ModType.Fun).ToArray();

        Assert.That(mods.OfType<BmsModLongNote>().SingleOrDefault(), Is.Not.Null);
        Assert.That(mods.OfType<BmsModChargeNote>().SingleOrDefault(), Is.Not.Null);
        Assert.That(mods.OfType<BmsModHellChargeNote>().SingleOrDefault(), Is.Not.Null);
    }

    [Test]
    public void TestGaugeModAppliesGaugeTypeToHealthProcessor()
    {
        var processor = new BmsHealthProcessor();
        var mod = new BmsModExHardGauge();

        mod.ApplyToHealthProcessor(processor);

        Assert.That(processor.GaugeType, Is.EqualTo(BmsGaugeType.ExHard));
    }

    [Test]
    public void TestGaugeModsAreMutuallyIncompatible()
    {
        var hard = new BmsModHardGauge();

        Assert.That(hard.IncompatibleMods, Does.Contain(typeof(BmsModAssistEasyGauge)));
        Assert.That(hard.IncompatibleMods, Does.Contain(typeof(BmsModEasyGauge)));
        Assert.That(hard.IncompatibleMods, Does.Contain(typeof(BmsModExHardGauge)));
        Assert.That(hard.IncompatibleMods, Does.Contain(typeof(BmsModHazardGauge)));
    }

    [Test]
    public void TestGaugeModsRegisteredWithExpectedAcronyms()
    {
        var reduction = ruleset.GetModsFor(ModType.DifficultyReduction).ToArray();
        var increase = ruleset.GetModsFor(ModType.DifficultyIncrease).ToArray();

        Assert.That(reduction.OfType<BmsModAssistEasyGauge>().Single().Acronym, Is.EqualTo("E2"));
        Assert.That(reduction.OfType<BmsModEasyGauge>().Single().Acronym, Is.EqualTo("E1"));
        Assert.That(increase.OfType<BmsModHardGauge>().Single().Acronym, Is.EqualTo("H1"));
        Assert.That(increase.OfType<BmsModExHardGauge>().Single().Acronym, Is.EqualTo("H2"));
        Assert.That(increase.OfType<BmsModHazardGauge>().Single().Acronym, Is.EqualTo("H3"));
    }

    [Test]
    public void TestLnModeDisplayAttributeHiddenWhenUndefined()
    {
        var beatmapInfo = new BeatmapInfo();
        new BmsDifficultyInfo { Rank = 2, KeyCount = 9, LockedLongNoteMode = BmsLongNoteMode.Undefined }
            .WriteToOsuDifficulty(beatmapInfo);

        var attributes = ruleset.GetBeatmapAttributesForDisplay(beatmapInfo, Array.Empty<Mod>());
        var lnModeAttr = attributes.SingleOrDefault(a => a.Acronym == "LM");

        Assert.That(lnModeAttr, Is.Null);
    }

    [Test]
    public void TestLnModeDisplayAttributeShowsWhenLocked()
    {
        var beatmapInfo = new BeatmapInfo();
        new BmsDifficultyInfo { Rank = 2, KeyCount = 9, LockedLongNoteMode = BmsLongNoteMode.ChargeNote }
            .WriteToOsuDifficulty(beatmapInfo);

        var attributes = ruleset.GetBeatmapAttributesForDisplay(beatmapInfo, Array.Empty<Mod>());
        var lnModeAttr = attributes.SingleOrDefault(a => a.Acronym == "LM");

        Assert.That(lnModeAttr, Is.Not.Null);
        Assert.That(lnModeAttr.AdditionalMetrics[0].Value.ToString(), Is.EqualTo("CN"));
    }

    [Test]
    public void TestMania7KConvertAttributesUseConvertedBmsDifficulty()
    {
        var beatmapInfo = new BeatmapInfo(
            new RulesetInfo { OnlineID = 3, ShortName = "mania" },
            new BeatmapDifficulty
            {
                CircleSize = 7,
                OverallDifficulty = 8,
                ApproachRate = 9,
                DrainRate = 5,
            })
        {
            TotalObjectCount = 200,
        };
        var expectedTotal = BmsGaugeCalculator.CalculateDefaultTotal(beatmapInfo.TotalObjectCount);
        var adjustedDifficulty = ruleset.GetAdjustedDisplayDifficulty(beatmapInfo, Array.Empty<Mod>());
        var attributes = ruleset.GetBeatmapAttributesForDisplay(beatmapInfo, Array.Empty<Mod>()).ToArray();
        var rank = attributes.Single(attribute => attribute.Acronym == "RK");
        var total = attributes.Single(attribute => attribute.Acronym == "TL");
        var lnMode = attributes.Single(attribute => attribute.Acronym == "LM");

        Assert.Multiple(() =>
        {
            Assert.That(adjustedDifficulty.CircleSize, Is.EqualTo(BmsLayout.BME7_KEY_COLUMNS));
            Assert.That(adjustedDifficulty.OverallDifficulty, Is.EqualTo(2));
            Assert.That(adjustedDifficulty.ApproachRate, Is.EqualTo(expectedTotal).Within(0.0001));
            Assert.That(adjustedDifficulty.DrainRate, Is.EqualTo(2));
            Assert.That(rank.OriginalValue, Is.EqualTo(2));
            Assert.That(rank.AdjustedValue, Is.EqualTo(2));
            Assert.That(total.OriginalValue, Is.EqualTo(expectedTotal).Within(0.0001));
            Assert.That(total.AdjustedValue, Is.EqualTo(expectedTotal).Within(0.0001));
            Assert.That(lnMode.OriginalValue, Is.EqualTo(2));
            Assert.That(lnMode.AdditionalMetrics[0].Value.ToString(), Is.EqualTo("CN"));
        });
    }

    [Test]
    public void TestMetadata()
    {
        Assert.That(ruleset.ShortName, Is.EqualTo(Constant.SHORT_NAME));
        Assert.That(ruleset.Description, Is.EqualTo("BMS"));
        Assert.That(ruleset.RulesetAPIVersionSupported, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void TestRankAttributeMetricsIncludeScratchAndLongNoteTailWindows()
    {
        var beatmapInfo = new BeatmapInfo();
        new BmsDifficultyInfo { Rank = 3, KeyCount = 8 }.WriteToOsuDifficulty(beatmapInfo);

        var rank = ruleset.GetBeatmapAttributesForDisplay(beatmapInfo, Array.Empty<Mod>())
            .Single(attribute => attribute.Acronym == "RK");

        var metrics = rank.AdditionalMetrics.ToDictionary(metric => metric.Name.ToString(), metric => metric.Value.ToString());

        Assert.That(metrics["Normal note PGREAT"], Is.EqualTo("-20 to +20 ms"));
        Assert.That(metrics["Scratch BAD"], Is.EqualTo("-230 to +290 ms"));
        Assert.That(metrics["LN tail PGREAT"], Is.EqualTo("-120 to +120 ms"));
        Assert.That(metrics["Scratch LN tail GOOD"], Is.EqualTo("-210 to +210 ms"));
    }

    [Test]
    public void TestExRankRoundTripsThroughOverallDifficulty()
    {
        // EXRANK encodes into OD as sentinel + pct (>= 100), kept distinct from RANK OD (0-4).
        var beatmapInfo = new BeatmapInfo();
        new BmsDifficultyInfo { Rank = 2, ExRank = 200, KeyCount = 8 }.WriteToOsuDifficulty(beatmapInfo);

        Assert.That(beatmapInfo.Difficulty.OverallDifficulty, Is.EqualTo(300f).Within(0.001));

        var decoded = BmsDifficultyInfo.FromOsuDifficulty(beatmapInfo.Difficulty);
        Assert.That(decoded.ExRank, Is.EqualTo(200).Within(0.001));
        Assert.That(decoded.Rank, Is.EqualTo(2)); // normalised to NORMAL while EXRANK is the source of truth
    }

    [Test]
    public void TestRankRoundTripsUnchangedThroughOverallDifficulty()
    {
        var beatmapInfo = new BeatmapInfo();
        new BmsDifficultyInfo { Rank = 0, KeyCount = 8 }.WriteToOsuDifficulty(beatmapInfo);

        Assert.That(beatmapInfo.Difficulty.OverallDifficulty, Is.EqualTo(0f));
        var decoded = BmsDifficultyInfo.FromOsuDifficulty(beatmapInfo.Difficulty);
        Assert.That(decoded.Rank, Is.EqualTo(0));
        Assert.That(decoded.ExRank, Is.Null);
    }

    [Test]
    public void TestExRankDisplayAttributeShowsPercentageHeadlineAndScaledWindows()
    {
        var beatmapInfo = new BeatmapInfo();
        new BmsDifficultyInfo { Rank = 2, ExRank = 200, KeyCount = 8 }.WriteToOsuDifficulty(beatmapInfo);

        var attributes = ruleset.GetBeatmapAttributesForDisplay(beatmapInfo, Array.Empty<Mod>());
        var exRankAttr = attributes.SingleOrDefault(a => a.Acronym == "EX");

        Assert.That(exRankAttr, Is.Not.Null, "EXRANK attribute should be shown when ExRank is set");
        Assert.That(attributes.SingleOrDefault(a => a.Acronym == "RK"), Is.Null, "RANK attribute should be hidden in EXRANK mode");
        Assert.That(exRankAttr.AdjustedValue, Is.EqualTo(200f));
        Assert.That(exRankAttr.MaxValue, Is.EqualTo(200f));

        // EXRANK 200 -> rate 1.5 -> 7K head PGREAT 20 * 1.5 = +/-30ms.
        var metrics = exRankAttr.AdditionalMetrics.ToDictionary(m => m.Name.ToString(), m => m.Value.ToString());
        Assert.That(metrics["Normal note PGREAT"], Is.EqualTo("-30 to +30 ms"));
    }

    private class TestNotificationOverlay : INotificationOverlay
    {
        public List<Notification> PostedNotifications { get; } = new();

        public void Post(Notification notification) => PostedNotifications.Add(notification);

        public void Hide()
        {
        }

        public IBindable<int> UnreadCount { get; } = new Bindable<int>();

        public IEnumerable<Notification> AllNotifications => PostedNotifications;
    }
}
