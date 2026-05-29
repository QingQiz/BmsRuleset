using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Localisation;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Difficulty;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Settings;
using osu.Game.Rulesets.BmsRuleset.Skinning;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset;

public class BmsRuleset : Ruleset
{
    public override string Description => "BMS Ruleset";

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
        BmsLayoutVariant.Bme7K => "7K",
        BmsLayoutVariant.Pms9K => "9K",
        BmsLayoutVariant.Bms5KDouble => "10K",
        BmsLayoutVariant.Bme7KDouble => "14K",
        BmsLayoutVariant.Pms9KDouble => "18K",
        _ => string.Empty,
    };

    public override int GetVariantForBeatmap(IBeatmapInfo beatmapInfo, IReadOnlyList<Mod>? mods = null)
    {
        var keyCount = (int)Math.Round(beatmapInfo.Difficulty.CircleSize);
        return (int)BmsLayout.VariantFromTotalColumns(keyCount);
    }

    public override IEnumerable<KeyBinding> GetDefaultKeyBindings(int variant = 0) =>
        BmsKeyBindingConfiguration.GetDefaultKeyBindings(variant);

    public override ScoreProcessor CreateScoreProcessor() =>
        new BmsScoreProcessor();

    public override HealthProcessor CreateHealthProcessor(double drainStartTime) =>
        new BmsHealthProcessor(drainStartTime);

    public override IEnumerable<RulesetBeatmapAttribute> GetBeatmapAttributesForDisplay(IBeatmapInfo beatmapInfo, IReadOnlyCollection<Mod> mods)
    {
        var originalDifficulty = beatmapInfo.Difficulty;
        var adjustedDifficulty = GetAdjustedDisplayDifficulty(beatmapInfo, mods);

        yield return new RulesetBeatmapAttribute(SongSelectStrings.KeyCount, "KC", originalDifficulty.CircleSize, adjustedDifficulty.CircleSize, 18)
        {
            Description = "Affects the number of key columns on the playfield.",
        };

        yield return new RulesetBeatmapAttribute(SongSelectStrings.HPDrain, "HP", originalDifficulty.DrainRate, adjustedDifficulty.DrainRate, 10);
    }

    public override IEnumerable<Mod> GetModsFor(ModType type) => type switch
    {
        ModType.DifficultyReduction => [new BmsModNoFail(), new BmsModHalfTime()],
        ModType.DifficultyIncrease => [new BmsModDoubleTime()],
        ModType.Automation => [new BmsModAutoplay(), new BmsModCinema()],
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

    public override IRulesetConfigManager CreateConfig(SettingsStore? settings) =>
        new BmsRulesetConfigManager(settings, RulesetInfo);

    public override RulesetSettingsSubsection CreateSettings() =>
        new BmsSettingsSubsection(this);

    public override Drawable CreateIcon() => new SpriteIcon
    {
        Icon = OsuIcon.RulesetMania,
        Colour = Colour4.White,
    };
}
