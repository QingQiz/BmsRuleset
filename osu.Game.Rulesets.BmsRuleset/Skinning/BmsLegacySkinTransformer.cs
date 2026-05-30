using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play.HUD.HitErrorMeters;
using osu.Game.Skinning;
using osuTK;
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
public partial class BmsLegacySkinTransformer : LegacySkinTransformer
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

    private const double hit_explosion_fade_in_duration = 80;

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
            layoutVariant = BmsLayout.VariantFromTotalColumns(Math.Max(1, (int)Math.Round(beatmap.BeatmapInfo.Difficulty.CircleSize)));
        }

        maniaKeyCount = BmsSkinComponentLookup.GetManiaKeyCount(layoutVariant);

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
        if (lookup is GlobalSkinnableContainerLookup containerLookup
            && containerLookup.Lookup == GlobalSkinnableContainers.MainHUDComponents)
        {
            if (containerLookup.Ruleset != null)
                return IsProvidingLegacyResources ? createLegacyHud() : BmsBuiltInSkinTransformer.WithoutHealthDisplay(base.GetDrawableComponent(lookup));

            return BmsBuiltInSkinTransformer.WithoutHealthDisplay(base.GetDrawableComponent(lookup));
        }

        if (lookup is SkinComponentLookup<HitResult> resultLookup && IsProvidingLegacyResources)
            return getResult(resultLookup.Component) ?? base.GetDrawableComponent(lookup);

        if (lookup is not BmsSkinComponentLookup bmsLookup)
            return base.GetDrawableComponent(lookup);

        if (!IsProvidingLegacyResources)
            return null;

        return bmsLookup.Component switch
        {
            BmsSkinComponents.Note when hasAnimation(getNoteImageName(bmsLookup))
                => new LegacyBmsNotePiece(this, bmsLookup),
            BmsSkinComponents.ColumnBackground
                => new LegacyBmsColumnBackground(this, bmsLookup),
            BmsSkinComponents.HitTarget when bmsLookup.ColumnIndex == null && hasAnimation(getHitTargetImageName())
                => new LegacyBmsHitTarget(this),
            BmsSkinComponents.KeyArea when hasAnimation(getKeyImageName(bmsLookup, false))
                => new LegacyBmsKeyArea(this, bmsLookup),
            BmsSkinComponents.Mine when hasAnimation(getMineImageName(bmsLookup))
                => new LegacyBmsNotePiece(this, bmsLookup),
            BmsSkinComponents.HitExplosion when hasAnimation(getHitExplosionImageName(bmsLookup))
                => new LegacyBmsHitExplosion(this, bmsLookup),
            BmsSkinComponents.StageBackground when hasAnyAnimation(getStageBackgroundImageNames())
                => new LegacyBmsStageBackground(this),
            BmsSkinComponents.StageForeground when hasAnimation(getStageForegroundImageName())
                => new LegacyBmsStageForeground(this),
            BmsSkinComponents.HoldNoteHead or BmsSkinComponents.HoldNoteTail or BmsSkinComponents.HoldNoteBody
                or BmsSkinComponents.BarLine
                => throw new UnsupportedSkinComponentException(lookup),
            _ => null,
        };
    }

    public override IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
    {
        if (lookup is BmsSkinConfigurationLookup bmsLookup)
        {
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

    private IBindable<T>? getManiaConfig<T>(LegacyManiaSkinConfigurationLookups lookup, BmsSkinComponentLookup? componentLookup = null, int? columnIndex = null)
        where T : notnull
        => GetConfig<BmsSkinConfigurationLookup, T>(new BmsSkinConfigurationLookup(lookup, componentLookup, columnIndex));

    private string fallbackColumnIndex(BmsSkinComponentLookup lookup)
    {
        if (lookup.IsScratch)
            return "S";

        var maniaColumnsPerStage = layoutVariant switch
        {
            BmsLayoutVariant.Bms5KDouble => 5,
            BmsLayoutVariant.Bme7KDouble => 7,
            BmsLayoutVariant.Pms9KDouble => 9,
            _ => maniaKeyCount,
        };
        var columnInStage = Math.Clamp(lookup.ManiaColumnIndex ?? 0, 0, Math.Max(0, maniaColumnsPerStage - 1)) % maniaColumnsPerStage;
        var distanceToEdge = Math.Min(columnInStage, maniaColumnsPerStage - 1 - columnInStage);
        return distanceToEdge % 2 == 0 ? "1" : "2";
    }

    private Drawable? getAnimation(string name) =>
        this.GetAnimation(name, WrapMode.ClampToEdge, WrapMode.ClampToEdge, true, true);

    private bool hasAnimation(string name) => getAnimation(name) != null;

    private bool hasAnyAnimation(params string[] names) => names.Any(hasAnimation);

    private Drawable createLegacyHud() => new DefaultSkinComponentsContainer(container =>
    {
        foreach (var d in container.OfType<ISerialisableDrawable>())
            d.UsesFixedAnchor = true;
    })
    {
        Children =
        [
            new LegacyScoreCounter(),
            new LegacyAccuracyCounter(),
            new LegacySongProgress(),
            new BarHitErrorMeter { Anchor = Anchor.BottomCentre, Origin = Anchor.CentreLeft, Rotation = -90 },
        ],
    };

    private IEnumerable<BmsSkinConfiguration> getConfigurations()
    {
        foreach (var configuration in skinConfigurations.Value.Where(c => c.Section == BmsSkinConfigurationSection.Bms && c.Layout == layoutVariant))
            yield return configuration;

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
                yield return 6;

                break;

            case BmsLayoutVariant.Bme7K:
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

    private string getNoteImageName(BmsSkinComponentLookup lookup) =>
        getManiaConfig<string>(LegacyManiaSkinConfigurationLookups.NoteImage, lookup)?.Value
        ?? $"mania-note{fallbackColumnIndex(lookup)}";

    private string getMineImageName(BmsSkinComponentLookup lookup) =>
        getManiaConfig<string>(LegacyManiaSkinConfigurationLookups.Hit100, lookup)?.Value
        ?? "mania-noteS";

    private string getKeyImageName(BmsSkinComponentLookup lookup, bool down) =>
        getManiaConfig<string>(down ? LegacyManiaSkinConfigurationLookups.KeyImageDown : LegacyManiaSkinConfigurationLookups.KeyImage, lookup)?.Value
        ?? $"mania-key{fallbackColumnIndex(lookup)}{(down ? "D" : string.Empty)}";

    private string getHitExplosionImageName(BmsSkinComponentLookup lookup) =>
        lookup.IsLongNote
            ? getManiaConfig<string>(LegacyManiaSkinConfigurationLookups.HoldNoteLightImage, lookup)?.Value ?? "lightingL"
            : getManiaConfig<string>(LegacyManiaSkinConfigurationLookups.ExplosionImage, lookup)?.Value ?? "lightingN";

    private string getHitTargetImageName() =>
        getManiaConfig<string>(LegacyManiaSkinConfigurationLookups.HitTargetImage)?.Value ?? "mania-stage-hint";

    private Drawable? getResult(HitResult result)
    {
        foreach (var (mappedResult, lookup, filename) in hit_result_mappings)
        {
            if (mappedResult != result)
                continue;

            var image = getManiaConfig<string>(lookup)?.Value ?? filename;
            var animation = this.GetAnimation(image, true, true, frameLength: 1000 / 20d);
            return animation == null ? null : new LegacyBmsJudgementPiece(result, animation);
        }

        return null;
    }

    private string[] getStageBackgroundImageNames() =>
    [
        getManiaConfig<string>(LegacyManiaSkinConfigurationLookups.LeftStageImage)?.Value ?? "mania-stage-left",
        getManiaConfig<string>(LegacyManiaSkinConfigurationLookups.RightStageImage)?.Value ?? "mania-stage-right",
    ];

    private string getStageForegroundImageName() =>
        getManiaConfig<string>(LegacyManiaSkinConfigurationLookups.BottomStageImage)?.Value ?? "mania-stage-bottom";

    private sealed partial class LegacyBmsJudgementPiece : CompositeDrawable, IAnimatableJudgement
    {
        private readonly HitResult result;
        private readonly Drawable animation;

        public LegacyBmsJudgementPiece(HitResult result, Drawable animation)
        {
            this.result = result;
            this.animation = animation;

            Origin = Anchor.Centre;
            AutoSizeAxes = Axes.Both;
        }

        public void PlayAnimation()
        {
            (animation as IFramedAnimation)?.GotoFrame(0);

            this.FadeInFromZero(20, Easing.Out)
                .Then().Delay(160)
                .FadeOutFromOne(40, Easing.In);

            if (result is HitResult.Meh or HitResult.Miss)
            {
                animation.ScaleTo(1.2f).Then().ScaleTo(1, 100, Easing.Out);
                return;
            }

            animation.ScaleTo(0.8f)
                .Then().ScaleTo(1, 40)
                .Then().ScaleTo(0.85f)
                .Then().ScaleTo(0.7f, 40)
                .Then().Delay(100)
                .Then().ScaleTo(0.4f, 40, Easing.In);
        }

        public Drawable? GetAboveHitObjectsProxiedContent() => null;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            InternalChild = animation.With(d =>
            {
                d.Anchor = Anchor.Centre;
                d.Origin = Anchor.Centre;
            });
        }
    }

    private sealed partial class LegacyBmsKeyArea : CompositeDrawable, IKeyBindingHandler<BmsAction>
    {
        private readonly BmsSkinComponentLookup lookup;
        private readonly Drawable? upSprite;
        private readonly Drawable? downSprite;

        public LegacyBmsKeyArea(BmsLegacySkinTransformer transformer, BmsSkinComponentLookup lookup)
        {
            this.lookup = lookup;

            RelativeSizeAxes = Axes.Both;

            upSprite = transformer.getAnimation(transformer.getKeyImageName(lookup, false))?.With(d =>
            {
                d.Anchor = Anchor.TopCentre;
                d.Origin = Anchor.TopCentre;
                d.RelativeSizeAxes = Axes.X;
                d.Width = 1;
            });

            downSprite = transformer.getAnimation(transformer.getKeyImageName(lookup, true))?.With(d =>
            {
                d.Anchor = Anchor.TopCentre;
                d.Origin = Anchor.TopCentre;
                d.RelativeSizeAxes = Axes.X;
                d.Width = 1;
                d.Alpha = 0;
            });

            InternalChild = new Container
            {
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.TopCentre,
                Y = -(transformer.getManiaConfig<float>(LegacyManiaSkinConfigurationLookups.HitPosition)?.Value ?? 0),
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Children =
                [
                    upSprite ?? Empty(),
                    downSprite ?? Empty(),
                ],
            };
        }

        public bool OnPressed(KeyBindingPressEvent<BmsAction> e)
        {
            if (lookup.ColumnIndex == null || BmsKeyBindingConfiguration.ActionToColumn(e.Action, lookup.LayoutVariant) != lookup.ColumnIndex)
                return false;

            if (downSprite == null)
                return false;

            upSprite?.FadeTo(0);
            downSprite.FadeTo(1);
            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<BmsAction> e)
        {
            if (lookup.ColumnIndex == null || BmsKeyBindingConfiguration.ActionToColumn(e.Action, lookup.LayoutVariant) != lookup.ColumnIndex)
                return;

            upSprite?.Delay(hit_explosion_fade_in_duration).FadeTo(1);
            downSprite?.Delay(hit_explosion_fade_in_duration).FadeTo(0);
        }
    }

    private sealed partial class LegacyBmsHitExplosion : CompositeDrawable
    {
        public LegacyBmsHitExplosion(BmsLegacySkinTransformer transformer, BmsSkinComponentLookup lookup)
        {
            RelativeSizeAxes = Axes.Both;

            var tmp = transformer.GetAnimation(transformer.getHitExplosionImageName(lookup), true, false);
            double frameLength = 0;

            if (tmp is IFramedAnimation tmpAnimation && tmpAnimation.FrameCount > 0)
                frameLength = Math.Max(1000 / 60.0, 170.0 / tmpAnimation.FrameCount);

            var scale = transformer
                            .getManiaConfig<float>(
                                lookup.IsLongNote ? LegacyManiaSkinConfigurationLookups.HoldNoteLightScale : LegacyManiaSkinConfigurationLookups.ExplosionScale, lookup)
                            ?.Value
                        ?? 1;
            var colour = transformer.getManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnLightColour, lookup)?.Value ?? Color4.White;
            var hitPosition = transformer.getManiaConfig<float>(LegacyManiaSkinConfigurationLookups.HitPosition)?.Value ?? 0;

            InternalChild = transformer.GetAnimation(transformer.getHitExplosionImageName(lookup), true, false, frameLength: frameLength)?.With(d =>
            {
                d.Anchor = Anchor.BottomCentre;
                d.Origin = Anchor.Centre;
                d.Y = -hitPosition;
                d.Blending = BlendingParameters.Additive;
                d.Colour = LegacyColourCompatibility.DisallowZeroAlpha(colour);
                d.Scale = new Vector2(scale);
            }) ?? Empty();
        }
    }

    private sealed partial class LegacyBmsNotePiece : CompositeDrawable
    {
        private readonly BmsLegacySkinTransformer transformer;
        private readonly BmsSkinComponentLookup lookup;
        private readonly float? widthForNoteHeightScale;
        private readonly float columnWidthForNoteHeightScale;
        private Drawable? noteAnimation;

        public LegacyBmsNotePiece(BmsLegacySkinTransformer transformer, BmsSkinComponentLookup lookup)
        {
            this.transformer = transformer;
            this.lookup = lookup;
            widthForNoteHeightScale = transformer.getManiaConfig<float>(LegacyManiaSkinConfigurationLookups.WidthForNoteHeightScale)?.Value;
            columnWidthForNoteHeightScale = transformer.getManiaConfig<float>(LegacyManiaSkinConfigurationLookups.ColumnWidth, lookup)?.Value
                                            ?? (lookup.IsScratch ? 42 : 48);

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            Origin = Anchor.BottomLeft;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            InternalChild = noteAnimation = transformer.getAnimation(getImageName())?.With(d =>
            {
                d.Anchor = Anchor.BottomLeft;
                d.Origin = Anchor.BottomLeft;
            }) ?? Empty();
        }

        protected override void Update()
        {
            base.Update();

            if (noteAnimation == null)
                return;

            var texture = noteAnimation switch
            {
                Sprite sprite => sprite.Texture,
                TextureAnimation animation when animation.FrameCount > 0 => animation.CurrentFrame,
                _ => null,
            };

            if (texture == null)
                return;

            var noteHeight = widthForNoteHeightScale == null
                ? DrawWidth
                : widthForNoteHeightScale.Value * DrawWidth / Math.Max(1, columnWidthForNoteHeightScale);
            noteAnimation.Scale = Vector2.Divide(new Vector2(DrawWidth, noteHeight), texture.DisplayWidth);
        }

        private string getImageName() => lookup.Component switch
        {
            BmsSkinComponents.Mine => transformer.getMineImageName(lookup),
            _ => transformer.getNoteImageName(lookup),
        };
    }

    private sealed partial class LegacyBmsColumnBackground : CompositeDrawable, IKeyBindingHandler<BmsAction>
    {
        private readonly BmsSkinComponentLookup lookup;
        private readonly Drawable? light;

        public LegacyBmsColumnBackground(BmsLegacySkinTransformer transformer, BmsSkinComponentLookup lookup)
        {
            this.lookup = lookup;
            RelativeSizeAxes = Axes.Both;

            var lineColour = transformer.getManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnLineColour, lookup)?.Value ?? Color4.White;
            var backgroundColour = transformer.getManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour, lookup)?.Value ?? Color4.Black;
            var lightColour = transformer.getManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnLightColour, lookup)?.Value ?? Color4.White;
            var lightImage = transformer.getManiaConfig<string>(LegacyManiaSkinConfigurationLookups.LightImage, lookup)?.Value ?? "mania-stage-light";
            var lightPosition = transformer.getManiaConfig<float>(LegacyManiaSkinConfigurationLookups.LightPosition, lookup)?.Value ?? 0;
            var lightFramePerSecond = transformer.getManiaConfig<int>(LegacyManiaSkinConfigurationLookups.LightFramePerSecond, lookup)?.Value ?? 60;
            var leftLineWidth = lookup.ColumnIndex == 0 ? transformer.getManiaConfig<float>(LegacyManiaSkinConfigurationLookups.LeftLineWidth, lookup)?.Value ?? 1 : 0;
            var rightLineWidth = transformer.getManiaConfig<float>(LegacyManiaSkinConfigurationLookups.RightLineWidth, lookup)?.Value ?? 1;
            light = transformer.GetAnimation(lightImage, true, true, frameLength: 1000d / lightFramePerSecond)?.With(d =>
            {
                d.Anchor = Anchor.BottomCentre;
                d.Origin = Anchor.BottomCentre;
                d.Y = -lightPosition;
                d.RelativeSizeAxes = Axes.X;
                d.Width = 1;
                d.Colour = LegacyColourCompatibility.DisallowZeroAlpha(lightColour);
                d.Alpha = 0;
            });

            InternalChildren =
            [
                LegacyColourCompatibility.ApplyWithDoubledAlpha(new Box
                {
                    RelativeSizeAxes = Axes.Both,
                }, backgroundColour),
                light ?? Empty(),
                new BmsColumnSeparator(leftLineWidth, lineColour)
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                },
                new BmsColumnSeparator(rightLineWidth, lineColour)
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                },
            ];
        }

        public bool OnPressed(KeyBindingPressEvent<BmsAction> e)
        {
            if (lookup.ColumnIndex == null || BmsKeyBindingConfiguration.ActionToColumn(e.Action, lookup.LayoutVariant) != lookup.ColumnIndex)
                return false;

            light?.FadeIn();
            light?.ScaleTo(Vector2.One);
            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<BmsAction> e)
        {
            if (lookup.ColumnIndex == null || BmsKeyBindingConfiguration.ActionToColumn(e.Action, lookup.LayoutVariant) != lookup.ColumnIndex)
                return;

            light?.FadeTo(0, 250);
            light?.ScaleTo(new Vector2(1, 0), 250);
        }
    }

    private sealed partial class LegacyBmsHitTarget : CompositeDrawable
    {
        public LegacyBmsHitTarget(BmsLegacySkinTransformer transformer)
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            var targetImage = transformer.getManiaConfig<string>(LegacyManiaSkinConfigurationLookups.HitTargetImage)?.Value ?? "mania-stage-hint";
            var showJudgementLine = transformer.getManiaConfig<bool>(LegacyManiaSkinConfigurationLookups.ShowJudgementLine)?.Value ?? true;
            var lineColour = transformer.getManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.JudgementLineColour)?.Value ?? Color4.White;
            var target = transformer.getAnimation(targetImage);

            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Children =
                [
                    target?.With(d =>
                    {
                        d.RelativeSizeAxes = Axes.X;
                        d.Width = 1;
                        d.Scale = new Vector2(1, 1.44225f);
                    }) ?? Empty(),
                    new Box
                    {
                        Anchor = Anchor.CentreLeft,
                        RelativeSizeAxes = Axes.X,
                        Height = 1,
                        Colour = lineColour,
                        Alpha = showJudgementLine ? 0.9f : 0,
                    },
                ],
            };
        }
    }

    private sealed partial class LegacyBmsStageBackground : CompositeDrawable
    {
        private readonly Drawable? leftSprite;
        private readonly Drawable? rightSprite;

        public LegacyBmsStageBackground(BmsLegacySkinTransformer transformer)
        {
            RelativeSizeAxes = Axes.Both;
            Masking = false;

            var images = transformer.getStageBackgroundImageNames();

            InternalChildren =
            [
                leftSprite = transformer.getAnimation(images[0])?.With(d =>
                {
                    d.Anchor = Anchor.TopLeft;
                    d.Origin = Anchor.TopRight;
                }) ?? Empty(),
                rightSprite = transformer.getAnimation(images[1])?.With(d =>
                {
                    d.Anchor = Anchor.TopRight;
                    d.Origin = Anchor.TopLeft;
                }) ?? Empty(),
            ];
        }

        protected override void Update()
        {
            base.Update();

            if (leftSprite != null)
                scaleStageSide(leftSprite);

            if (rightSprite != null)
                scaleStageSide(rightSprite);
        }

        private void scaleStageSide(Drawable sprite)
        {
            var height = sprite switch
            {
                Sprite s when s.Texture != null => s.Texture.DisplayHeight,
                TextureAnimation a when a.CurrentFrame != null => a.CurrentFrame.DisplayHeight,
                _ => sprite.Height,
            };

            if (height > 0)
                sprite.Scale = new Vector2(1, DrawHeight / height);
        }
    }

    private sealed partial class BmsColumnSeparator : CompositeDrawable
    {
        public BmsColumnSeparator(float width, Color4 colour)
        {
            RelativeSizeAxes = Axes.Y;
            Width = width;
            Alpha = width > 0 ? 1 : 0;

            InternalChild = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = colour,
            };
        }
    }

    private sealed partial class LegacyBmsStageForeground : CompositeDrawable
    {
        public LegacyBmsStageForeground(BmsLegacySkinTransformer transformer)
        {
            RelativeSizeAxes = Axes.Both;

            InternalChild = transformer.getAnimation(transformer.getStageForegroundImageName())?.With(d =>
            {
                d.Anchor = Anchor.BottomCentre;
                d.Origin = Anchor.BottomCentre;
                d.Scale = new Vector2(1.6f);
            }) ?? Empty();
        }
    }
}
