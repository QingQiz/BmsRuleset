using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Mods.Gauge;

public abstract class BmsModGauge : Mod, IApplicableToHealthProcessor
{
    private static readonly Type[] gauge_mod_types =
    [
        typeof(BmsModAssistEasyGauge),
        typeof(BmsModEasyGauge),
        typeof(BmsModHardGauge),
        typeof(BmsModExHardGauge),
        typeof(BmsModHazardGauge),
    ];

    private static readonly Dictionary<BmsGaugeType, Type> gauge_mod_by_type = buildGaugeModByType();

    public abstract BmsGaugeType GaugeType { get; }

    public override Type[] IncompatibleMods => gauge_mod_types.Where(t => t != GetType()).ToArray();

    public void ApplyToHealthProcessor(HealthProcessor healthProcessor)
    {
        if (healthProcessor is BmsHealthProcessor bmsHealthProcessor)
            bmsHealthProcessor.SetGaugeType(GaugeType);
    }

    public static BmsModGauge? CreateForType(BmsGaugeType type) =>
        gauge_mod_by_type.TryGetValue(type, out var t) ? (BmsModGauge)Activator.CreateInstance(t)! : null;

    private static Dictionary<BmsGaugeType, Type> buildGaugeModByType()
    {
        var map = new Dictionary<BmsGaugeType, Type>();
        foreach (var t in gauge_mod_types)
            map[((BmsModGauge)Activator.CreateInstance(t)!).GaugeType] = t;
        return map;
    }

    protected static LocalisableString GaugeDescription(string label)
        => $"Use the {label} BMS gauge.";
}
