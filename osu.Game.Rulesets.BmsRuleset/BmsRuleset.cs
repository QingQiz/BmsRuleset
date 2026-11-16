using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Input.Bindings;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Conversion;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Database;
using osu.Game.Rulesets.BmsRuleset.Editor;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Difficulty;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.LongNoteMode;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Randomisation;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Icons;
using osu.Game.Rulesets.BmsRuleset.UI.Result;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.BmsRuleset.UI.Settings;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Legacy;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Filter;
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

    public override string ShortName => Constant.SHORT_NAME;

    public override string RulesetAPIVersionSupported => CURRENT_RULESET_API_VERSION;

    public override IEnumerable<int> GameplayVariants => BmsKeyBindingConfiguration.AvailableVariants;

    public override LocalisableString VariantDescription => BmsStrings.Layout;

    public static readonly IReadOnlyList<HitResult> STATIC_VALID_HIT_RESULTS =
    [
        HitResult.Perfect,
        HitResult.Great,
        HitResult.Good,
        HitResult.Ok,
        HitResult.Meh,
        HitResult.Miss,
    ];

    static BmsRuleset()
    {
        BmsBeatmapDecoder.Register();
        BmsEditorPatcher.InstallOnce();
        BmsReplayPatcher.InstallOnce();
        BmsSongSelectEntryPatcher.InstallOnce();
        BmsSkinEditorPatcher.InstallOnce();
        BmsBeatmapNotificationPatcher.InstallOnce();
        BmsResultsScreenEntryPatcher.InstallOnce();
        BmsWorkingBeatmapPatcher.InstallOnce();
        BmsBeatmapStatisticsPatcher.InstallOnce();
        BmsFrameStatisticsPatcher.InstallOnce();
        BmsDifficultyCalculationPatcher.InstallOnce();
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
        BmsLayoutVariant.Popn9K => BmsStrings.PopnLayout(9),
        BmsLayoutVariant.Popn5K => BmsStrings.PopnLayout(5),
        BmsLayoutVariant.Keyboard24K => BmsStrings.KeyboardLayout(24),
        BmsLayoutVariant.Keyboard24KDouble => BmsStrings.KeyboardDoubleLayout(24),
        var layout when BmsLayout.IsGeneric(layout) => BmsStrings.GenericLayout(BmsLayout.GetTotalColumns(layout)),
        BmsLayoutVariant.Bms5KDouble => "DP 5K",
        BmsLayoutVariant.Bme7KDouble => "DP 7K",
        BmsLayoutVariant.Popn9KDouble => BmsStrings.PopnDoubleLayout(9),
        _ => string.Empty,
    };

    public override int GetVariantForBeatmap(IBeatmapInfo beatmapInfo, IReadOnlyList<Mod>? mods = null)
    {
        return (int)(Mania7KBeatmapConverter.CanConvert(beatmapInfo)
            ? Mania7KBeatmapConverter.GetConvertedDifficultyInfo(beatmapInfo).LayoutVariant
            : BmsDifficultyInfo.GetLayout(beatmapInfo.Difficulty));
    }

    public override IEnumerable<KeyBinding> GetDefaultKeyBindings(int variant = 0) =>
        variant == EDITOR_VARIANT ? [] : BmsKeyBindingConfiguration.GetDefaultKeyBindings(variant);

    public override ScoreProcessor CreateScoreProcessor() =>
        new BmsScoreProcessor();

    public override HealthProcessor CreateHealthProcessor(double drainStartTime) =>
        new BmsHealthProcessor();

    public override StatisticItem[] CreateStatisticsForScore(ScoreInfo score, IBeatmap playableBeatmap) =>
    [
        new(BmsStrings.GaugeHistory, () => new BmsGaugeHistoryGraph(score, playableBeatmap), requiresHitEvents: true),
        new(BmsStrings.Timeline, () => new BmsTimelineStatistic(score, playableBeatmap), requiresHitEvents: true),
        new(BmsStrings.HitScatter, () => new BmsHitScatterStatistic(score, playableBeatmap), requiresHitEvents: true),
        new(BmsStrings.HitOffset, () => new BmsHitOffsetStatistic(score, playableBeatmap), requiresHitEvents: true),
    ];

    public override IEnumerable<Mod> GetModsFor(ModType type) => type switch
    {
        ModType.DifficultyReduction =>
        [
            new BmsModAssistEasyGauge(),
            new BmsModEasyGauge(),

            new BmsModHideScratch(),
            new BmsModNoMine(),
            new BmsModNoBad(),

            new BmsModHalfTime(),
            new BmsModConstant(),
        ],
        ModType.DifficultyIncrease =>
        [
            new BmsModHardGauge(),
            new BmsModExHardGauge(),
            new BmsModHazardGauge(),

            new BmsModNoGood(),
            new BmsModNoGreat(),

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
            new BmsModInvert(),
            new BmsModSecondPlayer(),
        ],
        ModType.Fun =>
        [
            new BmsModManiaProfile(),
            new BmsModLr2(),
            new BmsModLongNote(),
            new BmsModChargeNote(),
            new BmsModHellChargeNote(),
            new BmsModBackgroundKeysound(),
        ],
        ModType.System =>
        [
            new BmsModBranchReplay(),
            new BmsModPaused(),
            new BmsModClassGauge(),
            new BmsModExClassGauge(),
            new BmsModExHardClassGauge(),
        ],
        _ => [],
    };

    public override BeatmapDifficulty GetAdjustedDisplayDifficulty(IBeatmapInfo beatmapInfo, IReadOnlyCollection<Mod> mods)
    {
        var adjustedDifficulty = new BeatmapDifficulty(beatmapInfo.Difficulty);
        if (Mania7KBeatmapConverter.CanConvert(beatmapInfo))
            Mania7KBeatmapConverter.GetConvertedDifficultyInfo(beatmapInfo).WriteToOsuDifficulty(adjustedDifficulty);

        foreach (var mod in mods.OfType<IApplicableToDifficulty>())
            mod.ApplyToDifficulty(adjustedDifficulty);

        GameplayProfile.FromMods(mods).AdjustDisplayDifficulty(beatmapInfo, adjustedDifficulty);
        return adjustedDifficulty;
    }

    public override IEnumerable<RulesetBeatmapAttribute> GetBeatmapAttributesForDisplay(IBeatmapInfo beatmapInfo, IReadOnlyCollection<Mod> mods)
        => GameplayProfile.FromMods(mods).BeatmapAttributes(beatmapInfo, mods, GetAdjustedDisplayDifficulty(beatmapInfo, mods));

    public override ISkin? CreateSkinTransformer(ISkin skin, IBeatmap beatmap) => skin switch
    {
        LegacyBeatmapSkin => new BmsIgnoredBeatmapSkinTransformer(skin),
        ArgonSkin or ArgonProSkin or TrianglesSkin or DefaultLegacySkin or RetroSkin => new BmsBuiltInSkinTransformer(skin),
        Skin => new BmsLegacySkinTransformer(skin, beatmap),
        _ => null,
    };

    public override IEnumerable<HitResult> GetValidHitResults() => STATIC_VALID_HIT_RESULTS;

    public override LocalisableString GetDisplayNameForHitResult(HitResult result) =>
        STATIC_VALID_HIT_RESULTS.Contains(result) ? BmsGameplayProfile.INSTANCE.JudgementLabel(result) : base.GetDisplayNameForHitResult(result);

    public override IRulesetConfigManager CreateConfig(SettingsStore? settings)
    {
        return BmsRulesetRuntime.ConfigManager = new BmsRulesetConfigManager(settings, RulesetInfo);
    }

    public override RulesetSettingsSubsection CreateSettings() =>
        new BmsSettingsSubsection(this);

    public override IRulesetFilterCriteria CreateRulesetFilterCriteria() =>
        new BmsFilterCriteria(BmsRulesetRuntime.ConfigManager);

    public override Drawable CreateIcon() => new BmsRulesetIcon();
}
