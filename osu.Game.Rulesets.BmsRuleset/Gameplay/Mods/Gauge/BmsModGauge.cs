using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;

public abstract class BmsModGauge : Mod, IApplicableToHealthProcessor
{
    private static readonly Type[] gauge_mod_types =
    [
        typeof(BmsModAssistEasyGauge),
        typeof(BmsModEasyGauge),
        typeof(BmsModHardGauge),
        typeof(BmsModExHardGauge),
        typeof(BmsModHazardGauge),
        typeof(BmsModClassGauge),
        typeof(BmsModExClassGauge),
        typeof(BmsModExHardClassGauge),
    ];

    private static readonly Dictionary<BmsGaugeType, Type> gauge_mod_by_type = buildGaugeModByType();

    public abstract BmsGaugeType GaugeType { get; }

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.CourseInitialHealth), SettingControlType = typeof(BmsCourseGaugeSetting<double?>))]
    public Bindable<double?> InitialHealth { get; } = new();

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.CourseGaugeProfile), SettingControlType = typeof(BmsCourseGaugeSetting<BmsGaugeProfileFamily?>))]
    public Bindable<BmsGaugeProfileFamily?> GaugeProfileFamily { get; } = new();

    public override IEnumerable<(LocalisableString setting, LocalisableString value)> SettingDescription => [];

    public override Type[] IncompatibleMods => gauge_mod_types.Where(t => t != GetType()).ToArray();

    public void ApplyToHealthProcessor(HealthProcessor healthProcessor)
    {
        if (healthProcessor is not BmsHealthProcessor health)
            return;

        var family = GaugeProfileFamily.Value ?? health.ConfiguredProfileFamily;
        if (GaugeType is BmsGaugeType.Class or BmsGaugeType.ExClass or BmsGaugeType.ExHardClass)
            health.ConfigureGaugeContext(true, family);

        if (InitialHealth.Value is { } initialHealth)
            health.SetGaugeTypes([GaugeType], replaceExisting: true, profileFamilyOverride: family,
                initialStates: [new BmsGaugeStateSnapshot(GaugeType, initialHealth, false)]);
        else
            health.SetGaugeType(GaugeType, family);
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
        => BmsStrings.GaugeDescription(label);
}
