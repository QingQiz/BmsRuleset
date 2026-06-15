using System;
using System.Collections.Generic;
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
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Settings;
using osu.Game.Rulesets.BmsRuleset.Skinning;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Filter;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset;

public class BmsRuleset : Ruleset
{
    public override string Description => "BMS";

    public override string ShortName => "bms";

    public override string RulesetAPIVersionSupported => CURRENT_RULESET_API_VERSION;

    public override IEnumerable<int> AvailableVariants => BmsKeyBindingConfiguration.AvailableVariants;

    public override LocalisableString VariantDescription => "Layout";

    /// <summary>
    ///     Single source of truth for BMS judgement label names.
    ///     Used by <see cref="GetDisplayNameForHitResult"/> and the default judgement piece.
    ///     <list type="table">
    ///         <item><term>Perfect</term><description>PGREAT</description></item>
    ///         <item><term>Great</term><description>GREAT</description></item>
    ///         <item><term>Good</term><description>GOOD</description></item>
    ///         <item><term>Ok</term><description>BAD</description></item>
    ///         <item><term>Meh</term><description>POOR  (note consumed: passive miss or in-POOR-zone keypress)</description></item>
    ///         <item><term>Miss</term><description>E-POOR (Empty POOR: keypress with no note to consume)</description></item>
    ///     </list>
    /// </summary>
    public static readonly IReadOnlyDictionary<HitResult, string> HIT_RESULT_LABELS = new Dictionary<HitResult, string>
    {
        [HitResult.Perfect] = "PGREAT",
        [HitResult.Great] = "GREAT",
        [HitResult.Good] = "GOOD",
        [HitResult.Ok] = "BAD",
        [HitResult.Meh] = "POOR",
        [HitResult.Miss] = "E-POOR",
    };

    /// <summary>
    ///     Static version of <see cref="GetValidHitResults"/> for use by types that cannot hold
    ///     a <see cref="BmsRuleset"/> instance (e.g. <see cref="UI.BmsPlayfield"/> during load).
    /// </summary>
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
    }

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

    public override IEnumerable<RulesetBeatmapAttribute> GetBeatmapAttributesForDisplay(IBeatmapInfo beatmapInfo, IReadOnlyCollection<Mod> mods)
    {
        var original = BmsDifficultyInfo.FromOsuDifficulty(beatmapInfo.Difficulty);
        var adjustedDifficulty = GetAdjustedDisplayDifficulty(beatmapInfo, mods);
        var adjusted = BmsDifficultyInfo.FromOsuDifficulty(adjustedDifficulty);

        var w = BmsHitWindows.RANK_WINDOWS_LR2[Math.Clamp(original.Rank, 0, BmsHitWindows.RANK_WINDOWS_LR2.Length - 1)];

        yield return new RulesetBeatmapAttribute("RANK", "RK", original.Rank, adjusted.Rank, 4)
        {
            Description = $"Pgreat={w.pgreat}ms Great={w.great}ms Good={w.good}ms Bad={w.badEarly}ms",
        };

        yield return new RulesetBeatmapAttribute("TOTAL", "TL", (float)original.Total, (float)adjusted.Total, 300)
        {
            Description = "PGREAT/GREAT=+auto%  GOOD=+auto/2%  BAD=-4%  POOR=-6%  EPOOR=-2%",
        };
    }

    public override IEnumerable<Mod> GetModsFor(ModType type) => type switch
    {
        ModType.DifficultyReduction => [new BmsModNoFail(), new BmsModHalfTime(), new BmsModConstant(), new BmsModAutoScratch()],
        ModType.DifficultyIncrease => [new BmsModDoubleTime()],
        ModType.Automation => [new BmsModAutoplay(), new BmsModCinema()],
        ModType.Conversion => [new BmsModMirror(), new BmsModSecondPlayer(), new BmsModLaneRandom(), new BmsModNoteRandom(), new BmsModRotationRandom()],
        ModType.System => [new BmsModBranchReplay()],
        _ => [],
    };

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
        var config = new BmsRulesetConfigManager(settings, RulesetInfo);
        sharedConfigManager = config;
        return config;
    }

    public override RulesetSettingsSubsection CreateSettings() =>
        new BmsSettingsSubsection(this);

    public override IRulesetFilterCriteria CreateRulesetFilterCriteria() =>
        new BmsFilterCriteria(sharedConfigManager);

    public override Drawable CreateIcon() => new SpriteIcon
    {
        Icon = OsuIcon.RulesetMania,
        Colour = Colour4.White,
    };
}
