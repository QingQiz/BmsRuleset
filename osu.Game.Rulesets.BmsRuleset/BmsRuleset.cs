using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Difficulty;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.Editor;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Mods.LongNoteMode;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Result;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.Settings;
using osu.Game.Rulesets.BmsRuleset.SongSelect;
using osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Filter;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Statistics;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset;

public partial class BmsRuleset : Ruleset
{
    public override string Description => "BMS";

    public override string ShortName => "bms";

    public override string RulesetAPIVersionSupported => CURRENT_RULESET_API_VERSION;

    public override IEnumerable<int> AvailableVariants => BmsKeyBindingConfiguration.AvailableVariants;

    public override LocalisableString VariantDescription => "Layout";

    public static readonly IReadOnlyDictionary<HitResult, string> HIT_RESULT_LABELS = new Dictionary<HitResult, string>
    {
        [HitResult.Perfect] = "PGREAT",
        [HitResult.Great] = "GREAT",
        [HitResult.Good] = "GOOD",
        [HitResult.Ok] = "BAD",
        [HitResult.Meh] = "POOR",
        [HitResult.Miss] = "E-POOR",
    };

    public static readonly IReadOnlyList<HitResult> STATIC_VALID_HIT_RESULTS =
    [
        HitResult.Perfect,
        HitResult.Great,
        HitResult.Good,
        HitResult.Ok,
        HitResult.Meh,
        HitResult.Miss, // Empty POOR counter (keypress with no note to consume)
    ];

    /// <summary>
    /// Shared difficulty table store, set by <see cref="Settings.BmsSettingsSubsection"/> on creation.
    /// </summary>
    internal static DifficultyTableStore? DifficultyTableStore { get; set; }

    private static BmsRulesetConfigManager? sharedConfigManager;

    static BmsRuleset()
    {
        BmsBeatmapDecoder.Register();
        BmsEditorPatcher.InstallOnce();
        BmsReplayPatcher.InstallOnce();
        BmsSongSelectLampPatcher.InstallOnce();
        BmsLocalLeaderboardPatcher.InstallOnce();
        BmsRankingHitResultColourPatcher.InstallOnce();
        BmsWorkingBeatmapPatcher.InstallOnce();
    }

    public override ScoreMultiplierCalculator CreateScoreMultiplierCalculator(ScoreMultiplierContext context) =>
        new BmsScoreMultiplierCalculator(context);

    public override DrawableRuleset CreateDrawableRulesetWith(IBeatmap beatmap, IReadOnlyList<Mod>? mods = null) =>
        new BmsDrawableRuleset(this, beatmap, mods);

    public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap) =>
        new BmsBeatmapConverter(beatmap, this);

    public override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap) =>
        new BmsDifficultyCalculator(RulesetInfo, beatmap);

    public override LocalisableString GetVariantName(int variant) => (BmsLayoutVariant)variant switch
    {
        BmsLayoutVariant.Bms5K => "5K",
        BmsLayoutVariant.Bms5K2P => "5K (2P)",
        BmsLayoutVariant.Bme7K => "7K",
        BmsLayoutVariant.Bme7K2P => "7K (2P)",
        BmsLayoutVariant.Pms9K => "9K",
        BmsLayoutVariant.Bms5KDouble => "DP 5K",
        BmsLayoutVariant.Bme7KDouble => "DP 7K",
        BmsLayoutVariant.Pms9KDouble => "DP 9K",
        _ => string.Empty,
    };

    public override int GetVariantForBeatmap(IBeatmapInfo beatmapInfo, IReadOnlyList<Mod>? mods = null)
        => (int)BmsLayout.VariantFromTotalColumns(BmsDifficultyInfo.GetKeyCount(beatmapInfo.Difficulty));

    public override IEnumerable<KeyBinding> GetDefaultKeyBindings(int variant = 0) =>
        BmsKeyBindingConfiguration.GetDefaultKeyBindings(variant);

    public override ScoreProcessor CreateScoreProcessor() =>
        new BmsScoreProcessor();

    public override HealthProcessor CreateHealthProcessor(double drainStartTime) =>
        new BmsHealthProcessor();

    public override StatisticItem[] CreateStatisticsForScore(ScoreInfo score, IBeatmap playableBeatmap) =>
    [
        new("Gauge History", () => new BmsGaugeHistoryGraph(score, playableBeatmap), requiresHitEvents: true),
        new("Timeline", () => new BmsTimelineStatistic(score, playableBeatmap), requiresHitEvents: true),
        new("Hit Scatter", () => new BmsHitScatterStatistic(score.HitEvents, playableBeatmap), requiresHitEvents: true),
        new("Hit Offset", () => new BmsHitOffsetStatistic(score.HitEvents, playableBeatmap), requiresHitEvents: true),
    ];

    public override IEnumerable<Mod> GetModsFor(ModType type) => type switch
    {
        ModType.DifficultyReduction =>
        [
            new BmsModAssistEasyGauge(),
            new BmsModEasyGauge(),

            new BmsModHideScratch(),

            new BmsModNoFail(),
            new BmsModHalfTime(),
            new BmsModConstant(),
        ],
        ModType.DifficultyIncrease =>
        [
            new BmsModHardGauge(),
            new BmsModExHardGauge(),
            new BmsModHazardGauge(),

            new BmsModDoubleTime(),
        ],
        ModType.Automation =>
        [
            new BmsModAutoplay(),

            new BmsModAutoScratch(),
            new BmsModAutoGauge(),
        ],
        ModType.Conversion =>
        [
            new BmsModLaneRandom(),
            new BmsModNoteRandom(),
            new BmsModRotationRandom(),

            new BmsModMirror(),
            new BmsModSecondPlayer(),
        ],
        ModType.Fun =>
        [
            new BmsModLongNote(),
            new BmsModChargeNote(),
            new BmsModHellChargeNote(),
            new BmsModBackgroundKeysound(),
        ],
        ModType.System => [new BmsModBranchReplay()],
        _ => [],
    };

    #region Attributes display

    /// <summary>Bar maximum for the EXRANK attribute (200% = full bar; 100% = NORMAL at midpoint).</summary>
    private const float exrank_display_max = 200;

    public override IEnumerable<RulesetBeatmapAttribute> GetBeatmapAttributesForDisplay(IBeatmapInfo beatmapInfo, IReadOnlyCollection<Mod> mods)
    {
        var original = BmsDifficultyInfo.FromOsuDifficulty(beatmapInfo.Difficulty);
        var adjustedDifficulty = GetAdjustedDisplayDifficulty(beatmapInfo, mods);
        var adjusted = BmsDifficultyInfo.FromOsuDifficulty(adjustedDifficulty);
        var colours = new OsuColour();
        var layout = BmsLayout.VariantFromTotalColumns(adjusted.KeyCount);

        if (adjusted.ExRank is { } exRank)
        {
            var rate = BmsJudgementProfileProvider.RateForExRank(layout, exRank);
            yield return new RulesetBeatmapAttribute("EXRANK", "EX", (float)original.ExRank.GetValueOrDefault(exRank), (float)exRank, exrank_display_max)
            {
                Description = $"EXRANK {exRank:0.##}% timing windows.",
                AdditionalMetrics = createRankMetrics(rate, adjusted.KeyCount),
            };
        }
        else
        {
            var rate = BmsJudgementProfileProvider.RateForRank(layout, adjusted.Rank);
            yield return new RulesetBeatmapAttribute("RANK", "RK", original.Rank, adjusted.Rank, 4)
            {
                Description = $"RANK {adjusted.Rank} timing windows.",
                AdditionalMetrics = createRankMetrics(rate, adjusted.KeyCount),
            };
        }

        if (original.LockedLongNoteMode != BmsLongNoteMode.Undefined)
        {
            yield return new RulesetBeatmapAttribute("LNMODE", "LM", (float)original.LockedLongNoteMode, (float)original.LockedLongNoteMode, 3)
            {
                Description = $"Locked long-note mode: {formatLongNoteMode(original.LockedLongNoteMode)}",
                AdditionalMetrics =
                [
                    new("Locked mode", formatLongNoteMode(original.LockedLongNoteMode), colours.Gray4),
                ],
            };
        }

        yield return new RulesetBeatmapAttribute("TOTAL", "TL", (float)original.Total, (float)adjusted.Total, 300)
        {
            Description = createTotalDescription(beatmapInfo, adjusted, mods),
            AdditionalMetrics = createTotalMetrics(beatmapInfo, adjusted, mods),
        };
    }

    public override ISkin? CreateSkinTransformer(ISkin skin, IBeatmap beatmap) => skin switch
    {
        ArgonSkin or ArgonProSkin or TrianglesSkin or DefaultLegacySkin or RetroSkin => new BmsBuiltInSkinTransformer(skin),
        Skin => new BmsLegacySkinTransformer(skin, beatmap),
        _ => null,
    };

    public override IEnumerable<HitResult> GetValidHitResults() => STATIC_VALID_HIT_RESULTS;

    public override LocalisableString GetDisplayNameForHitResult(HitResult result) =>
        HIT_RESULT_LABELS.TryGetValue(result, out var label) ? label : base.GetDisplayNameForHitResult(result);

    public override IRulesetConfigManager CreateConfig(SettingsStore? settings)
    {
        return sharedConfigManager = new BmsRulesetConfigManager(settings, RulesetInfo);
    }

    internal static BmsReferenceBpmMode CurrentReferenceBpmMode =>
        sharedConfigManager?.Get<BmsReferenceBpmMode>(BmsRulesetSetting.ReferenceBpmMode) ?? BmsReferenceBpmMode.MainBpm;

    internal static bool UseDedicatedPreviewAudio =>
        sharedConfigManager?.Get<bool>(BmsRulesetSetting.UseDedicatedPreviewAudio) ?? true;

    public override RulesetSettingsSubsection CreateSettings() =>
        new BmsSettingsSubsection(this);

    public override IRulesetFilterCriteria CreateRulesetFilterCriteria() =>
        new BmsFilterCriteria(sharedConfigManager);

    public override Drawable CreateIcon() => new SpriteIcon
    {
        Icon = OsuIcon.RulesetMania,
        Colour = Colour4.White,
    };

    private static LocalisableString createTotalDescription(IBeatmapInfo beatmapInfo, BmsDifficultyInfo difficulty, IReadOnlyCollection<Mod> mods)
    {
        var gaugeType = mods.OfType<BmsModGauge>().FirstOrDefault()?.GaugeType ?? BmsGaugeType.Normal;
        var profile = BmsGaugeProfileFactory.Create(gaugeType);
        var noteCount = Math.Max(1, beatmapInfo.TotalObjectCount);
        var calculator = new BmsGaugeCalculator(profile, difficulty.Total, noteCount);

        var gutsText = profile.GutsRules.Count > 0
            ? "\nDamage shown at initial HP; low-HP guts may reduce penalties."
            : string.Empty;

        return $"{formatGaugeName(gaugeType)} gauge\n{noteCount} objects, {formatTotal(difficulty, calculator)}{gutsText}";

        static string formatGaugeName(BmsGaugeType gaugeType) => gaugeType switch
        {
            BmsGaugeType.AssistEasy => "Assist Easy",
            BmsGaugeType.Easy => "Easy",
            BmsGaugeType.Normal => "Normal",
            BmsGaugeType.Hard => "Hard",
            BmsGaugeType.ExHard => "ExHard",
            BmsGaugeType.Hazard => "Hazard",
            BmsGaugeType.Class => "Class",
            BmsGaugeType.ExClass => "ExClass",
            BmsGaugeType.ExHardClass => "ExHard Class",
            _ => gaugeType.ToString(),
        };

        static string formatTotal(BmsDifficultyInfo difficulty, BmsGaugeCalculator calculator) => difficulty.Total > 0
            ? $"#TOTAL {calculator.Total:0.###}"
            : $"default TOTAL {calculator.Total:0.###}";
    }

    private static RulesetBeatmapAttribute.AdditionalMetric[] createRankMetrics(double rate, int keyCount)
    {
        var layout = BmsLayout.VariantFromTotalColumns(keyCount);
        var metrics = new List<RulesetBeatmapAttribute.AdditionalMetric>();

        addHeadMetrics("Normal note", BmsJudgementProfileProvider.GetTable(layout, column: 1, judgementRate: rate, tail: false));

        if (tryGetScratchColumn(layout, out var scratchColumn))
            addHeadMetrics("Scratch", BmsJudgementProfileProvider.GetTable(layout, scratchColumn, rate, tail: false));

        addTailMetrics("LN tail", BmsJudgementProfileProvider.GetTable(layout, column: 1, judgementRate: rate, tail: true));

        if (tryGetScratchColumn(layout, out scratchColumn))
            addTailMetrics("Scratch LN tail", BmsJudgementProfileProvider.GetTable(layout, scratchColumn, rate, tail: true));

        return metrics.ToArray();

        void addHeadMetrics(string prefix, BmsJudgementWindowTable table)
        {
            addJudgementMetrics(prefix, table);
            metrics.Add(new RulesetBeatmapAttribute.AdditionalMetric($"{prefix} E-POOR early zone", $"-{formatMilliseconds(table.EarlyWindowFor(HitResult.Miss))} to -{formatMilliseconds(table.EarlyWindowFor(HitResult.Ok))} ms", BmsHitResultColours.ForHitResult(HitResult.Miss)));
        }

        void addTailMetrics(string prefix, BmsJudgementWindowTable table)
        {
            addJudgementMetrics(prefix, table);
        }

        void addJudgementMetrics(string prefix, BmsJudgementWindowTable table)
        {
            foreach (var result in new[] { HitResult.Perfect, HitResult.Great, HitResult.Good, HitResult.Ok })
                metrics.Add(new RulesetBeatmapAttribute.AdditionalMetric($"{prefix} {HIT_RESULT_LABELS[result]}", formatWindow(table, result), BmsHitResultColours.ForHitResult(result)));
        }

        static string formatWindow(BmsJudgementWindowTable table, HitResult result)
            => $"-{formatMilliseconds(table.EarlyWindowFor(result))} to +{formatMilliseconds(table.LateWindowFor(result))} ms";

        static string formatMilliseconds(double value) => $"{value:0.##}";

        static bool tryGetScratchColumn(BmsLayoutVariant layout, out int scratchColumn)
        {
            for (var column = 0; column < BmsLayout.GetTotalColumns(layout); column++)
            {
                if (!BmsLayout.IsScratchColumn(column, layout))
                    continue;

                scratchColumn = column;
                return true;
            }

            scratchColumn = -1;
            return false;
        }
    }

    private static RulesetBeatmapAttribute.AdditionalMetric[] createTotalMetrics(IBeatmapInfo beatmapInfo, BmsDifficultyInfo difficulty, IReadOnlyCollection<Mod> mods)
    {
        var gaugeType = mods.OfType<BmsModGauge>().FirstOrDefault()?.GaugeType ?? BmsGaugeType.Normal;
        var profile = BmsGaugeProfileFactory.Create(gaugeType);
        var noteCount = Math.Max(1, beatmapInfo.TotalObjectCount);
        var calculator = new BmsGaugeCalculator(profile, difficulty.Total, noteCount);
        var referenceHealth = profile.InitialHealth;

        return
        [
            new("PGREAT/GREAT", formatDelta(calculator.GetDeltaFor(HitResult.Perfect, referenceHealth)), BmsHitResultColours.ForHitResult(HitResult.Perfect)),
            new("GOOD", formatDelta(calculator.GetDeltaFor(HitResult.Good, referenceHealth)), BmsHitResultColours.ForHitResult(HitResult.Good)),
            new("BAD", formatDelta(calculator.GetDeltaFor(HitResult.Ok, referenceHealth)), BmsHitResultColours.ForHitResult(HitResult.Ok)),
            new("POOR", formatDelta(calculator.GetDeltaFor(HitResult.Meh, referenceHealth)), BmsHitResultColours.ForHitResult(HitResult.Meh)),
            new("E-POOR", formatDelta(calculator.GetDeltaFor(HitResult.Miss, referenceHealth)), BmsHitResultColours.ForHitResult(HitResult.Miss)),
        ];

        static string formatDelta(double delta) => $"{delta * 100:+0.###;-0.###;0}%";
    }

    private static string formatLongNoteMode(BmsLongNoteMode mode) => mode switch
    {
        BmsLongNoteMode.LongNote => "LN",
        BmsLongNoteMode.ChargeNote => "CN",
        BmsLongNoteMode.HellChargeNote => "HCN",
        _ => "??",
    };

    #endregion

}
