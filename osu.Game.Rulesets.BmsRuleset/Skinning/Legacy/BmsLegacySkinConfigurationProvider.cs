using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning.Configuration;
using osu.Game.Rulesets.BmsRuleset.Skinning.Embedded;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;

internal sealed class BmsLegacySkinConfigurationProvider
{
    private readonly ISkin skin;
    private readonly BmsLayoutVariant layoutVariant;
    private readonly int maniaKeyCount;
    private readonly Lazy<IReadOnlyList<BmsSkinConfiguration>> skinConfigurations;

    public BmsLegacySkinConfigurationProvider(ISkin skin, BmsLayoutVariant layoutVariant)
    {
        this.skin = skin;
        this.layoutVariant = layoutVariant;
        maniaKeyCount = BmsLayout.GetManiaKeyCount(layoutVariant);
        skinConfigurations = new Lazy<IReadOnlyList<BmsSkinConfiguration>>(() =>
            skin is BmsEmbeddedSkin embedded
                ? BmsSkinConfigurationDecoder.Decode(embedded.Resources)
                : BmsSkinConfigurationDecoder.Decode(skin));
    }

    public bool HasConfigurations => skinConfigurations.Value.Count > 0;

    public IBindable<TValue>? GetConfig<TValue>(BmsSkinConfigurationLookup lookup)
        where TValue : notnull
    {
        foreach (var configuration in getConfigurations())
        {
            var column = getConfigurationColumn(configuration, lookup);

            if (configuration.TryGet<TValue>(lookup.Lookup, column, out var value))
                return value;
        }

        return skin.GetConfig<LegacyManiaSkinConfigurationLookup, TValue>(new LegacyManiaSkinConfigurationLookup(maniaKeyCount, lookup.Lookup,
            lookup.ComponentLookup?.ManiaColumnIndex ?? lookup.ColumnIndex));
    }

    private IEnumerable<BmsSkinConfiguration> getConfigurations()
    {
        foreach (var configuration in skinConfigurations.Value.Where(c => c.Section == BmsSkinConfigurationSection.Bms && c.Layout == layoutVariant))
            yield return configuration;

        var layout1P = layoutVariant switch
        {
            BmsLayoutVariant.Bms5K2P => BmsLayoutVariant.Bms5K,
            BmsLayoutVariant.Bme7K2P => BmsLayoutVariant.Bme7K,
            _ => (BmsLayoutVariant?)null,
        };

        if (layout1P != null)
        {
            foreach (var configuration in skinConfigurations.Value.Where(c => c.Section == BmsSkinConfigurationSection.Bms && c.Layout == layout1P))
                yield return configuration;
        }

        foreach (var configuration in getManiaFallbackConfigurations())
            yield return configuration;
    }

    private IEnumerable<BmsSkinConfiguration> getManiaFallbackConfigurations()
    {
        var configurations = skinConfigurations.Value.Where(c => c.Section == BmsSkinConfigurationSection.Mania).ToArray();

        foreach (var keys in getSpecialStyleManiaFallbackKeys())
        {
            foreach (var configuration in configurations.Where(c => c.Keys == keys && c.SpecialStyle == 1))
                yield return configuration;
        }

        foreach (var configuration in configurations.Where(c => c.Keys == maniaKeyCount))
            yield return configuration;
    }

    private IEnumerable<int> getSpecialStyleManiaFallbackKeys()
    {
        switch (layoutVariant)
        {
            case BmsLayoutVariant.Bms5K:
            case BmsLayoutVariant.Bms5K2P:
                yield return 6;

                break;

            case BmsLayoutVariant.Bme7K:
            case BmsLayoutVariant.Bme7K2P:
                yield return 8;

                break;
        }
    }

    private int? getConfigurationColumn(BmsSkinConfiguration configuration, BmsSkinConfigurationLookup lookup)
    {
        if (configuration.Section == BmsSkinConfigurationSection.Bms || configuration.Keys != maniaKeyCount)
            return lookup.ComponentLookup?.ColumnIndex ?? lookup.ColumnIndex;

        return lookup.ComponentLookup?.ManiaColumnIndex ?? lookup.ColumnIndex;
    }
}
