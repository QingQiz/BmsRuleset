using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning;

/// <summary>
/// Lookup key used to query BMS-specific skin configuration values from
/// <see cref="BmsLegacySkinTransformer.GetConfig{TLookup,TValue}"/>.
/// </summary>
/// <remarks>
/// BMS skin config overlaps heavily with osu!mania legacy skin config — keys such as
/// <c>NoteImage</c>, <c>KeyImage</c>, <c>ColumnWidth</c>, and <c>HitPosition</c> are
/// shared — so this lookup reuses <see cref="LegacyManiaSkinConfigurationLookups"/>
/// rather than duplicating the enum.
/// <para>
/// Resolution order in <see cref="BmsLegacySkinTransformer"/>:
/// <list type="number">
///   <item><description>
///     BMS-section configurations parsed from <c>[BMS]</c> sections of <c>skin.ini</c>,
///     matched by <c>BmsLayoutVariant</c>.
///   </description></item>
///   <item><description>
///     Mania-section configurations from <c>[Mania]</c> sections, matched by key count
///     (with special-style fallback for 5K/7K scratch variants).
///   </description></item>
///   <item><description>
///     The wrapped skin's native <see cref="LegacyManiaSkinConfigurationLookup"/> API,
///     using <see cref="BmsLegacySkinTransformer"/>'s <c>maniaKeyCount</c>.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// Column resolution:
/// When <see cref="ComponentLookup"/> is provided (the lookup originates from a specific
/// playfield component), column index is derived from it — using the BMS column index for
/// <c>[BMS]</c> sections and the mania column index for <c>[Mania]</c> sections.
/// When only <see cref="ColumnIndex"/> is set (lookups with no per-column context, such as
/// <c>HitPosition</c>), it is used directly for both section types.
/// </para>
/// <para>
/// <see cref="BmsBuiltInSkinTransformer"/> always returns <c>null</c> for this lookup
/// type — built-in skins carry no <c>skin.ini</c> BMS or mania configuration.
/// </para>
/// </remarks>
public class BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups lookup, BmsSkinComponentLookup? componentLookup = null, int? columnIndex = null)
{
    /// <summary>The mania configuration key to look up.</summary>
    public readonly LegacyManiaSkinConfigurationLookups Lookup = lookup;

    /// <summary>
    /// The component context this lookup originates from, or <c>null</c> for
    /// stage-wide lookups that have no per-column context (e.g. <c>HitPosition</c>).
    /// Carries both the BMS column index and the mapped mania column index.
    /// </summary>
    public readonly BmsSkinComponentLookup? ComponentLookup = componentLookup;

    /// <summary>
    /// Raw column index override used when <see cref="ComponentLookup"/> is <c>null</c>.
    /// </summary>
    public readonly int? ColumnIndex = columnIndex;
}
