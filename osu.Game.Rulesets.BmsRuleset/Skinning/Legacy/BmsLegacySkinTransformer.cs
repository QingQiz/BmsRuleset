using System;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Configuration;
using osu.Game.Rulesets.BmsRuleset.Skinning.Drawables;
using osu.Game.Rulesets.BmsRuleset.Skinning.LegacyDrawables;
using osu.Game.Rulesets.BmsRuleset.Skinning.NoteTextures;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;

/// <summary>
/// Skin transformer applied over a user-supplied or embedded skin during BMS gameplay.
/// </summary>
/// <remarks>
/// Extends LegacySkinTransformer to handle BMS-specific component lookups
/// (BmsSkinComponentLookup, hit results, and the in-ruleset HUD container).
/// <para>
/// The transformer is instantiated by BmsRuleset.CreateSkinTransformer for
/// every concrete Skin in the chain (user skins, unrecognised third-party skins).
/// For BmsEmbeddedSkin instances the ruleset returns <c>null</c> from
/// <c>CreateSkinTransformer</c>; those skins are wrapped here directly by
/// BmsEmbeddedSkinSource.
/// </para>
/// <para>
/// BMS skin configuration (<c>skin.ini</c>) is decoded once and cached in
/// skinConfigurations. The config drives column widths, key images,
/// hit-explosion colours, and other per-column properties that are not covered by
/// the standard LegacyManiaSkinConfigurationLookup API.
/// </para>
/// </remarks>
public partial class BmsLegacySkinTransformer : LegacySkinTransformer, IBmsGameplaySkinDrawableSource
{

    /// <inheritdoc />
    /// <summary>
    /// Returns <c>true</c> if this transformer should supply legacy-style skin components.
    /// </summary>
    /// <remarks>
    /// Extends the base check (LegacySkinTransformer.IsProvidingLegacyResources
    /// = has a legacy combo font) to also return <c>true</c> when the wrapped skin provides
    /// BMS-specific resources (hasBmsResources). This allows skins that ship
    /// mania textures without a full legacy font to still activate the BMS legacy rendering path.
    /// </remarks>
    public override bool IsProvidingLegacyResources => base.IsProvidingLegacyResources || hasBmsResources.Value;

    internal const double HIT_EXPLOSION_FADE_IN_DURATION = 80;

    private readonly BmsLegacySkinConfigurationProvider configurationProvider;
    private readonly BmsLegacySkinResourceNames resourceNames;

    /// <summary>
    /// Lazily evaluated flag that is <c>true</c> when the wrapped skin contains
    /// at least one BMS-specific resource — either a parsed <c>skin.ini</c>
    /// <c>[BMS]</c> or <c>[Mania]</c> section, or a <c>mania-key1</c> / <c>mania-keyS</c>
    /// texture animation.
    /// </summary>
    /// <remarks>
    /// Used by IsProvidingLegacyResources to extend the base check to
    /// skins that have BMS textures but no legacy font (which is what the base
    /// LegacySkinTransformer.IsProvidingLegacyResources checks for).
    /// Evaluated at most once per transformer instance.
    /// </remarks>
    private readonly Lazy<bool> hasBmsResources;

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
    /// BmsEmbeddedSkin when created directly by BmsEmbeddedSkinSource.
    /// </param>
    /// <param name="beatmap">
    /// The current beatmap, used to derive the BmsLayoutVariant (column layout).
    /// A BmsBeatmap is preferred; other beatmap types fall back to
    /// BeatmapInfo.Difficulty <c>CircleSize</c>.
    /// </param>
    public BmsLegacySkinTransformer(ISkin skin, IBeatmap beatmap)
        : base(skin)
    {
        BmsLayoutVariant layoutVariant1;
        if (beatmap is BmsBeatmap bmsBeatmap)
        {
            layoutVariant1 = bmsBeatmap.LayoutVariant;
        }
        else
        {
            layoutVariant1 = BmsLayout.VariantFromTotalColumns(BmsDifficultyInfo.GetKeyCount(beatmap.BeatmapInfo.Difficulty));
        }

        configurationProvider = new BmsLegacySkinConfigurationProvider(Skin, layoutVariant1);
        resourceNames = new BmsLegacySkinResourceNames((lookup, componentLookup, columnIndex) => GetManiaConfig<string>(lookup, componentLookup, columnIndex)?.Value);

        hasBmsResources = new Lazy<bool>(()
            // A parsed [BMS] or [Mania] section is the strongest signal.
            => configurationProvider.HasConfigurations
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
        if (BmsDefaultHud.TryGetMainHudWithStage(lookup, () => base.GetDrawableComponent(lookup), out var mainHud))
            return mainHud;

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

    public override IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
    {
        if (lookup is BmsSkinConfigurationLookup bmsLookup)
            return configurationProvider.GetConfig<TValue>(bmsLookup);

        return base.GetConfig<TLookup, TValue>(lookup);
    }

    internal IBindable<T>? GetManiaConfig<T>(LegacyManiaSkinConfigurationLookups lookup, BmsSkinComponentLookup? componentLookup = null, int? columnIndex = null)
        where T : notnull
        => GetConfig<BmsSkinConfigurationLookup, T>(new BmsSkinConfigurationLookup(lookup, componentLookup, columnIndex));

    internal Drawable? GetLegacyAnimation(string name) =>
        this.GetAnimation(name, WrapMode.ClampToEdge, WrapMode.ClampToEdge, true, true);

    internal string GetKeyImageName(BmsSkinComponentLookup lookup, bool down) =>
        resourceNames.GetKeyImageName(lookup, down);

    internal string GetHitExplosionImageName(BmsSkinComponentLookup lookup) =>
        resourceNames.GetHitExplosionImageName(lookup);

    internal string GetHitTargetImageName() =>
        resourceNames.GetHitTargetImageName();

    internal string[] GetStageBackgroundImageNames() =>
        resourceNames.GetStageBackgroundImageNames();

    internal string GetStageForegroundImageName() =>
        resourceNames.GetStageForegroundImageName();

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
                => createNoteFactory(bmsLookup),
            BmsSkinComponents.ColumnBackground
                => new BmsResolvedDrawableFactory(() => new LegacyBmsColumnBackground(this, bmsLookup)),
            BmsSkinComponents.ColumnLight
                => new BmsResolvedDrawableFactory(() => new LegacyBmsColumnLight(this, bmsLookup)),
            BmsSkinComponents.HitTarget when bmsLookup.ColumnIndex == null
                => new BmsResolvedDrawableFactory(() => new LegacyBmsHitTarget(this)),
            BmsSkinComponents.KeyArea when hasAnimation(GetKeyImageName(bmsLookup, false))
                => new BmsResolvedDrawableFactory(() => new LegacyBmsKeyArea(this, bmsLookup)),
            BmsSkinComponents.Mine
                => createNoteFactory(bmsLookup),
            BmsSkinComponents.HitExplosion
                => hasAnimation(GetHitExplosionImageName(bmsLookup))
                    ? new BmsResolvedDrawableFactory(() => new LegacyBmsHitExplosion(this, bmsLookup))
                    : null,
            BmsSkinComponents.StageBackground when hasAnyAnimation(GetStageBackgroundImageNames())
                => new BmsResolvedDrawableFactory(() => new LegacyBmsStageBackground(this)),
            BmsSkinComponents.StageForeground when hasAnimation(GetStageForegroundImageName())
                => new BmsResolvedDrawableFactory(() => new LegacyBmsStageForeground(this)),
            BmsSkinComponents.HoldNoteHead
                => createNoteFactory(bmsLookup),
            BmsSkinComponents.HoldNoteTail
                => createNoteFactory(bmsLookup),
            _ => null,
        };
    }

    private BmsResolvedDrawableFactory? createNoteFactory(BmsSkinComponentLookup lookup)
    {
        var textures = BmsLegacyTextureResolver.ResolveNoteTextures(this, lookup);

        if (textures.Length == 0)
            return null;

        var widthForNoteHeightScale = GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.WidthForNoteHeightScale)?.Value;
        return new BmsResolvedDrawableFactory(() => new BmsResolvedNotePiece(textures, widthForNoteHeightScale));
    }

    private bool hasAnimation(string name) => GetLegacyAnimation(name) != null;

    private bool hasAnyAnimation(params string[] names) => names.Any(hasAnimation);

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
