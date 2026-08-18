using System;
using System.Linq;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.UI.Icons;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Mods.Gauge;

public class BmsModAutoGauge : Mod, IApplicableToHealthProcessor, IApplicableToScorePopulation
{
    /// <summary>
    /// The regular gameplay chain used by Auto Gauge, hardest first.
    /// </summary>
    internal static readonly BmsGaugeType[] AUTO_GAUGE_CHAIN =
    [
        BmsGaugeType.Hazard,
        BmsGaugeType.ExHard,
        BmsGaugeType.Hard,
        BmsGaugeType.Normal,
        BmsGaugeType.Easy,
        BmsGaugeType.AssistEasy,
    ];

    internal static readonly BmsGaugeType[] COURSE_AUTO_GAUGE_CHAIN =
    [
        BmsGaugeType.ExHardClass,
        BmsGaugeType.ExClass,
        BmsGaugeType.Class,
    ];

    private BmsHealthProcessor? resolvedHealthProcessor;

    public override string Name => "Auto Gauge";

    public override string Acronym => "AG";

    public override IconUsage? Icon => BmsIcons.AutoGauge;

    public override LocalisableString Description => BmsStrings.ModAutoGauge;

    public override ModType Type => ModType.Automation;

    /// <summary>
    /// Incompatible with all other gauge mods (same mutual-exclusion set as <see cref="BmsModGauge"/>).
    /// </summary>
    public override Type[] IncompatibleMods { get; } = [];

    public void ApplyToHealthProcessor(HealthProcessor healthProcessor)
    {
        if (healthProcessor is BmsHealthProcessor bmsHp)
        {
            resolvedHealthProcessor = bmsHp;
            bmsHp.SetGaugeTypes(
                bmsHp.IsCourseGaugeMode ? COURSE_AUTO_GAUGE_CHAIN : AUTO_GAUGE_CHAIN,
                profileFamilyOverride: bmsHp.ConfiguredProfileFamily);
        }
    }

    public void ApplyToScore(ScoreInfo score)
    {
        if (resolvedHealthProcessor is null)
            return;

        score.Mods = score.Mods.Where(m => m is not BmsModGauge).ToArray();

        if (resolvedHealthProcessor.GaugeHistory.Count > 0)
            BmsScoreGaugeHistoryStore.Set(score, resolvedHealthProcessor.GaugeHistory);

        // Normal is represented by AG alone, so an earlier live-population attribution must be removed.
        var mod = BmsModGauge.CreateForType(resolvedHealthProcessor.WorstGaugeType);
        if (mod is null)
            return;

        score.Mods = score.Mods.Append(mod).ToArray();
    }
}
