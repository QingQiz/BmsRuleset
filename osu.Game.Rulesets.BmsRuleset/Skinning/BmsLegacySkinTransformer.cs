using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning.LegacyDrawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Skinning;

// TODO rank mark
/// <inheritdoc />
/// <summary>
/// Skin transformer applied over a user-supplied or embedded skin during BMS gameplay.
/// </summary>
/// <remarks>
/// Extends <see cref="T:osu.Game.Skinning.LegacySkinTransformer">LegacySkinTransformer</see> to handle BMS-specific component lookups
/// (<see cref="T:osu.Game.Rulesets.BmsRuleset.Skinning.BmsSkinComponentLookup">BmsSkinComponentLookup</see>, hit results, and the in-ruleset HUD container).
/// <para>
/// The transformer is instantiated by <see cref="M:osu.Game.Rulesets.BmsRuleset.BmsRuleset.CreateSkinTransformer(osu.Game.Skinning.ISkin,osu.Game.Beatmaps.IBeatmap)">BmsRuleset.CreateSkinTransformer</see> for
/// every concrete <see cref="T:osu.Game.Skinning.Skin">Skin</see> in the chain (user skins, unrecognised third-party skins).
/// For <see cref="T:osu.Game.Rulesets.BmsRuleset.Skinning.BmsEmbeddedSkin">BmsEmbeddedSkin</see> instances the ruleset returns <c>null</c> from
/// <c>CreateSkinTransformer</c>; those skins are wrapped here directly by
/// <see cref="T:osu.Game.Rulesets.BmsRuleset.Skinning.BmsEmbeddedSkinSource">BmsEmbeddedSkinSource</see>.
/// </para>
/// <para>
/// BMS skin configuration (<c>skin.ini</c>) is decoded once and cached in
/// <see cref="F:osu.Game.Rulesets.BmsRuleset.Skinning.BmsLegacySkinTransformer.skinConfigurations">skinConfigurations</see>. The config drives column widths, key images,
/// hit-explosion colours, and other per-column properties that are not covered by
/// the standard <see cref="T:osu.Game.Skinning.LegacyManiaSkinConfigurationLookup">LegacyManiaSkinConfigurationLookup</see> API.
/// </para>
/// </remarks>
public partial class BmsLegacySkinTransformer : LegacySkinTransformer, IBmsGameplaySkinDrawableSource
{

    /// <inheritdoc />
    /// <summary>
    /// Returns <c>true</c> if this transformer should supply legacy-style skin components.
    /// </summary>
    /// <remarks>
    /// Extends the base check (<see cref="P:osu.Game.Skinning.LegacySkinTransformer.IsProvidingLegacyResources">LegacySkinTransformer.IsProvidingLegacyResources</see>
    /// = has a legacy combo font) to also return <c>true</c> when the wrapped skin provides
    /// BMS-specific resources (<see cref="F:osu.Game.Rulesets.BmsRuleset.Skinning.BmsLegacySkinTransformer.hasBmsResources">hasBmsResources</see>). This allows skins that ship
    /// mania textures without a full legacy font to still activate the BMS legacy rendering path.
    /// </remarks>
    public override bool IsProvidingLegacyResources => base.IsProvidingLegacyResources || hasBmsResources.Value;

    internal const double HIT_EXPLOSION_FADE_IN_DURATION = 80;

    private readonly BmsLayoutVariant layoutVariant;
    private readonly int maniaKeyCount;

    /// <summary>
    /// Lazily evaluated flag that is <c>true</c> when the wrapped skin contains
    /// at least one BMS-specific resource — either a parsed <c>skin.ini</c>
    /// <c>[BMS]</c> or <c>[Mania]</c> section, or a <c>mania-key1</c> / <c>mania-keyS</c>
    /// texture animation.
    /// </summary>
    /// <remarks>
    /// Used by <see cref="IsProvidingLegacyResources"/> to extend the base check to
    /// skins that have BMS textures but no legacy font (which is what the base
    /// <see cref="LegacySkinTransformer.IsProvidingLegacyResources"/> checks for).
    /// Evaluated at most once per transformer instance.
    /// </remarks>
    private readonly Lazy<bool> hasBmsResources;

    private readonly Lazy<IReadOnlyList<BmsSkinConfiguration>> skinConfigurations;

    private static readonly (HitResult Result, LegacyManiaSkinConfigurationLookups Lookup, string Filename)[] hit_result_mappings =
    [
        // BMS → osu!mania legacy skin image mapping (matches LR2 / BMS convention):
        (HitResult.Perfect, LegacyManiaSkinConfigurationLookups.Hit300g, "mania-hit300g"), // PGREAT
        (HitResult.Great, LegacyManiaSkinConfigurationLookups.Hit300, "mania-hit300"),     // GREAT
        (HitResult.Good, LegacyManiaSkinConfigurationLookups.Hit200, "mania-hit200"),      // GOOD
        (HitResult.Ok, LegacyManiaSkinConfigurationLookups.Hit50, "mania-hit50"),          // BAD
        (HitResult.Meh, LegacyManiaSkinConfigurationLookups.Hit0, "mania-hit0"),           // POOR (note consumed: passive miss or in-POOR-zone keypress)
        (HitResult.Miss, LegacyManiaSkinConfigurationLookups.Hit0, "mania-hit0"),          // Empty POOR (keypress with no note) — same image as POOR
    ];

    /// <param name="skin">
    /// The skin to wrap. Maybe a user skin, a third-party skin, or a
    /// <see cref="BmsEmbeddedSkin"/> when created directly by <see cref="BmsEmbeddedSkinSource"/>.
    /// </param>
    /// <param name="beatmap">
    /// The current beatmap, used to derive the <see cref="BmsLayoutVariant"/> (column layout).
    /// A <see cref="BmsBeatmap"/> is preferred; other beatmap types fall back to
    /// <see cref="BeatmapInfo.Difficulty"/> <c>CircleSize</c>.
    /// </param>
    public BmsLegacySkinTransformer(ISkin skin, IBeatmap beatmap)
        : base(skin)
    {
        if (beatmap is BmsBeatmap bmsBeatmap)
        {
            layoutVariant = bmsBeatmap.LayoutVariant;
        }
        else
        {
            layoutVariant = BmsLayout.VariantFromTotalColumns(BmsDifficultyInfo.GetKeyCount(beatmap.BeatmapInfo.Difficulty));
        }

        maniaKeyCount = BmsLayout.GetManiaKeyCount(layoutVariant);

        skinConfigurations = new Lazy<IReadOnlyList<BmsSkinConfiguration>>(() =>
            Skin is BmsEmbeddedSkin embedded
                ? BmsSkinConfigurationDecoder.Decode(embedded.Resources)
                : BmsSkinConfigurationDecoder.Decode(Skin));

        hasBmsResources = new Lazy<bool>(()
            // A parsed [BMS] or [Mania] section is the strongest signal.
            => skinConfigurations.Value.Count > 0
               // mania-key1 is a standard mania image name — any legacy mania skin has it —
               // so it is a weak signal and is mostly redundant with base.IsProvidingLegacyResources
               // (which triggers on a legacy combo font that the same skin almost certainly ships).
               // Kept here only as a last-resort catch for skins that somehow ship mania-key1
               // without a combo font and without a skin.ini.
               || hasAnimation("mania-key1")
               // mania-keyS is a scratch-column key image; standard mania has no scratch lane,
               // so its presence unambiguously identifies a BMS skin even without a skin.ini.
               || hasAnimation("mania-keyS")
        );
    }

    public override Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
    {
        var hud = BmsDefaultHud.GetDrawableComponent(lookup);
        if (hud != null) return hud;

        // Judgement lookups are not BMS-specific lookup objects; osu! asks by HitResult. Map them
        // to BMS judgement assets only when this transformer is actually active for legacy resources.
        if (lookup is SkinComponentLookup<HitResult> resultLookup && IsProvidingLegacyResources)
            return getResult(resultLookup.Component) ?? base.GetDrawableComponent(lookup);

        if (lookup is not BmsSkinComponentLookup bmsLookup)
            return base.GetDrawableComponent(lookup);

        if (!IsProvidingLegacyResources)
            return null;

        return getDrawableFactory(bmsLookup)?.Create();
    }

    BmsResolvedDrawableFactory? IBmsGameplaySkinDrawableSource.GetDrawableFactory(BmsSkinComponentLookup lookup) => getDrawableFactory(lookup);

    private BmsResolvedDrawableFactory? getDrawableFactory(BmsSkinComponentLookup bmsLookup)
    {
        if (!IsProvidingLegacyResources)
            return null;

        // Component routing stops here. Rendering details live in the separate LegacyBms* drawables;
        // this class only decides which legacy asset family is available for the requested lookup.
        return bmsLookup.Component switch
        {
            BmsSkinComponents.Note
                => createNoteFactory(BmsLegacyTextureResolver.NoteImageCandidates(this, bmsLookup)),
            BmsSkinComponents.ColumnBackground
                => new BmsResolvedDrawableFactory(() => new LegacyBmsColumnBackground(this, bmsLookup)),
            BmsSkinComponents.HitTarget when bmsLookup.ColumnIndex == null && hasAnimation(GetHitTargetImageName())
                => new BmsResolvedDrawableFactory(() => new LegacyBmsHitTarget(this)),
            BmsSkinComponents.KeyArea when hasAnimation(GetKeyImageName(bmsLookup, false))
                => new BmsResolvedDrawableFactory(() => new LegacyBmsKeyArea(this, bmsLookup)),
            BmsSkinComponents.Mine
                => createNoteFactory(BmsLegacyTextureResolver.NoteImageCandidates(this, bmsLookup)),
            BmsSkinComponents.HitExplosion
                => createHitExplosionFactory(bmsLookup),
            BmsSkinComponents.StageBackground when hasAnyAnimation(GetStageBackgroundImageNames())
                => new BmsResolvedDrawableFactory(() => new LegacyBmsStageBackground(this)),
            BmsSkinComponents.StageForeground when hasAnimation(GetStageForegroundImageName())
                => new BmsResolvedDrawableFactory(() => new LegacyBmsStageForeground(this)),
            BmsSkinComponents.HoldNoteHead
                => createNoteFactory(BmsLegacyTextureResolver.NoteImageCandidates(this, bmsLookup)),
            BmsSkinComponents.HoldNoteTail
                => createNoteFactory(BmsLegacyTextureResolver.NoteImageCandidates(this, bmsLookup)),
            _ => null,
        };
    }

    private BmsResolvedDrawableFactory? createNoteFactory(IEnumerable<string?> imageNames)
    {
        Texture[] textures = [];

        foreach (var imageName in imageNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
        {
            textures = this.GetTextures(imageName!, WrapMode.ClampToEdge, WrapMode.ClampToEdge, true, "-", null, out _)
                .Where(t => t.DisplayWidth > 0 && t.DisplayHeight > 0)
                .ToArray();

            if (textures.Length > 0)
                break;
        }

        if (textures.Length == 0)
            return null;

        var widthForNoteHeightScale = GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.WidthForNoteHeightScale)?.Value;
        return new BmsResolvedDrawableFactory(() => new BmsResolvedNotePiece(textures, widthForNoteHeightScale));
    }

    private BmsResolvedDrawableFactory? createHitExplosionFactory(BmsSkinComponentLookup lookup)
    {
        var textures = this.GetTextures(GetHitExplosionImageName(lookup), default, default, true, "-", null, out _)
            .Where(t => t.DisplayWidth > 0 && t.DisplayHeight > 0)
            .ToArray();

        if (textures.Length == 0)
            return null;

        var frameLength = Math.Max(1000 / 60.0, 170.0 / textures.Length);
        var scale = GetManiaConfig<float>(
            lookup.IsLongNote ? LegacyManiaSkinConfigurationLookups.HoldNoteLightScale : LegacyManiaSkinConfigurationLookups.ExplosionScale, lookup);
        var colour = GetManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnLightColour, lookup);
        var hitPosition = GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.HitPosition);

        return new BmsResolvedDrawableFactory(() => new BmsResolvedHitExplosion(
            textures,
            frameLength,
            scale?.Value ?? 1,
            colour?.Value ?? Color4.White,
            hitPosition?.Value ?? 0));
    }

    public override IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
    {
        if (lookup is BmsSkinConfigurationLookup bmsLookup)
        {
            // BMS config resolution is stricter than plain mania: exact [BMS] layout first, then
            // special-style [Mania] fallbacks (6K/8K with scratch), then plain mania key-count
            // sections, and finally the wrapped skin's own LegacyManiaSkinConfigurationLookup API.
            foreach (var configuration in getConfigurations())
            {
                var column = getConfigurationColumn(configuration, bmsLookup);

                if (configuration.TryGet<TValue>(bmsLookup.Lookup, column, out var value))
                    return value;
            }

            return Skin.GetConfig<LegacyManiaSkinConfigurationLookup, TValue>(new LegacyManiaSkinConfigurationLookup(maniaKeyCount, bmsLookup.Lookup,
                bmsLookup.ComponentLookup?.ManiaColumnIndex ?? bmsLookup.ColumnIndex));
        }

        return base.GetConfig<TLookup, TValue>(lookup);
    }

    internal IBindable<T>? GetManiaConfig<T>(LegacyManiaSkinConfigurationLookups lookup, BmsSkinComponentLookup? componentLookup = null, int? columnIndex = null)
        where T : notnull
        => GetConfig<BmsSkinConfigurationLookup, T>(new BmsSkinConfigurationLookup(lookup, componentLookup, columnIndex));

    internal Drawable? GetLegacyAnimation(string name) =>
        this.GetAnimation(name, WrapMode.ClampToEdge, WrapMode.ClampToEdge, true, true);

    internal string GetNoteImageName(BmsSkinComponentLookup lookup) =>
        GetManiaConfig<string>(LegacyManiaSkinConfigurationLookups.NoteImage, lookup)?.Value
        ?? $"mania-note{BmsLegacyTextureResolver.FallbackColumnIndex(lookup)}";

    internal string GetHoldNoteHeadImageName(BmsSkinComponentLookup lookup) =>
        getFirstAnimationName(getHoldNoteHeadImageNames(lookup)) ?? GetNoteImageName(lookup);

    internal string GetHoldNoteTailImageName(BmsSkinComponentLookup lookup) =>
        getFirstAnimationName(getHoldNoteTailImageNames(lookup)) ?? GetHoldNoteHeadImageName(lookup);

    internal string GetMineImageName(BmsSkinComponentLookup lookup) =>
        GetManiaConfig<string>(LegacyManiaSkinConfigurationLookups.Hit100, lookup)?.Value
        ?? "mania-noteS";

    internal string GetKeyImageName(BmsSkinComponentLookup lookup, bool down) =>
        GetManiaConfig<string>(down ? LegacyManiaSkinConfigurationLookups.KeyImageDown : LegacyManiaSkinConfigurationLookups.KeyImage, lookup)?.Value
        ?? $"mania-key{BmsLegacyTextureResolver.FallbackColumnIndex(lookup)}{(down ? "D" : string.Empty)}";

    internal string GetHitExplosionImageName(BmsSkinComponentLookup lookup) =>
        lookup.IsLongNote
            ? GetManiaConfig<string>(LegacyManiaSkinConfigurationLookups.HoldNoteLightImage, lookup)?.Value ?? "lightingL"
            : GetManiaConfig<string>(LegacyManiaSkinConfigurationLookups.ExplosionImage, lookup)?.Value ?? "lightingN";

    internal string GetHitTargetImageName() =>
        GetManiaConfig<string>(LegacyManiaSkinConfigurationLookups.HitTargetImage)?.Value ?? "mania-stage-hint";

    internal string[] GetStageBackgroundImageNames() =>
    [
        GetManiaConfig<string>(LegacyManiaSkinConfigurationLookups.LeftStageImage)?.Value ?? "mania-stage-left",
        GetManiaConfig<string>(LegacyManiaSkinConfigurationLookups.RightStageImage)?.Value ?? "mania-stage-right",
    ];

    internal string GetStageForegroundImageName() =>
        GetManiaConfig<string>(LegacyManiaSkinConfigurationLookups.BottomStageImage)?.Value ?? "mania-stage-bottom";

    private bool hasAnimation(string name) => GetLegacyAnimation(name) != null;

    private bool hasAnyAnimation(params string[] names) => names.Any(hasAnimation);

    private IEnumerable<BmsSkinConfiguration> getConfigurations()
    {
        // [BMS] sections use BMS column indices directly and are layout-specific. They must win over
        // [Mania] sections because a skin may include both generic mania and BMS-specialised values.
        foreach (var configuration in skinConfigurations.Value.Where(c => c.Section == BmsSkinConfigurationSection.Bms && c.Layout == layoutVariant))
            yield return configuration;

        // 2P variants (5K2P, 7K2P) share the same column semantics as their 1P counterparts
        // (scratch=0, keys=1..N) — only visual column order differs. Fall back to the 1P
        // skin.ini section so NoteImage*, KeyImage*, ColumnWidth etc. still apply.
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

        // Stable BMS skins often represent 5K/7K with scratch using mania SpecialStyle sections:
        // 6K special for 5K+scratch and 8K special for 7K+scratch. Prefer those over plain 5K/7K.
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
        // BMS sections are indexed by raw BMS columns (including scratch). Plain mania sections are
        // indexed by mapped mania columns, but special-style fallback sections use BMS-style indices.
        if (configuration.Section == BmsSkinConfigurationSection.Bms || configuration.Keys != maniaKeyCount)
            return lookup.ComponentLookup?.ColumnIndex ?? lookup.ColumnIndex;

        return lookup.ComponentLookup?.ManiaColumnIndex ?? lookup.ColumnIndex;
    }

    private string[] getHoldNoteHeadImageNames(BmsSkinComponentLookup lookup) =>
    [
        GetManiaConfig<string>(LegacyManiaSkinConfigurationLookups.HoldNoteHeadImage, lookup)?.Value ?? string.Empty,
        GetNoteImageName(lookup),
    ];

    private string[] getHoldNoteTailImageNames(BmsSkinComponentLookup lookup) =>
    [
        GetManiaConfig<string>(LegacyManiaSkinConfigurationLookups.HoldNoteTailImage, lookup)?.Value ?? string.Empty,
        GetManiaConfig<string>(LegacyManiaSkinConfigurationLookups.HoldNoteHeadImage, lookup)?.Value ?? string.Empty,
        GetNoteImageName(lookup),
    ];

    private string? getFirstAnimationName(IEnumerable<string> names)
        => names.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name) && hasAnimation(name));

    private Drawable? getResult(HitResult result)
    {
        foreach (var (mappedResult, lookup, filename) in hit_result_mappings)
        {
            if (mappedResult != result)
                continue;

            var image = GetManiaConfig<string>(lookup)?.Value ?? filename;
            var animation = this.GetAnimation(image, true, true, frameLength: 1000 / 20d);
            return animation == null ? null : new LegacyBmsJudgementPiece(result, animation);
        }

        return null;
    }
}
