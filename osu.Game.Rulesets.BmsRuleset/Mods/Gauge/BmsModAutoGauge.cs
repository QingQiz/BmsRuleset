using System;
using System.Linq;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Mods.Gauge;

public class BmsModAutoGauge : Mod, IApplicableToHealthProcessor, IApplicableToScorePopulation
{
    /// <summary>
    /// The chain of gauge types used by Auto Gauge, hardest first.
    /// Gauge mods normally set a single type; Auto Gauge sets all six.
    /// </summary>
    private static readonly BmsGaugeType[] auto_gauge_chain =
    [
        BmsGaugeType.Hazard,
        BmsGaugeType.ExHard,
        BmsGaugeType.Hard,
        BmsGaugeType.Normal,
        BmsGaugeType.Easy,
        BmsGaugeType.AssistEasy,
    ];

    private BmsHealthProcessor? resolvedHealthProcessor;

    public override string Name => "Auto Gauge";

    public override string Acronym => "AG";

    public override LocalisableString Description =>
        "Start with the hardest gauge. When you fail, drop to the next tier.";

    public override ModType Type => ModType.Automation;

    /// <summary>
    /// Incompatible with all other gauge mods (same mutual-exclusion set as <see cref="BmsModGauge"/>).
    /// </summary>
    public override Type[] IncompatibleMods { get; } =
    [
        typeof(BmsModGauge),
        typeof(BmsModAssistEasyGauge),
        typeof(BmsModEasyGauge),
        typeof(BmsModHardGauge),
        typeof(BmsModExHardGauge),
        typeof(BmsModHazardGauge),
    ];

    public void ApplyToHealthProcessor(HealthProcessor healthProcessor)
    {
        if (healthProcessor is BmsHealthProcessor bmsHp)
        {
            resolvedHealthProcessor = bmsHp;
            bmsHp.SetGaugeTypes(auto_gauge_chain);
        }
    }

    public void ApplyToScore(ScoreInfo score)
    {
        if (resolvedHealthProcessor is null)
            return;

        // Normal (the baseline gauge) has no mod; CreateForType returns null and we leave the score as-is.
        var mod = BmsModGauge.CreateForType(resolvedHealthProcessor.WorstGaugeType);
        if (mod is null)
            return;

        if (score.Mods.All(m => m.GetType() != mod.GetType()))
            score.Mods = score.Mods.Append(mod).ToArray();
    }
}
