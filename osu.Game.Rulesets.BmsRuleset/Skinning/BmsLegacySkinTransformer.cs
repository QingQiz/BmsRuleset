using System;
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

// TODO remove osu native health bar, replace with bms native health bar, skinnable (only position)
// TODO mine
// TODO rank mark
public partial class BmsLegacySkinTransformer : SkinTransformer
{
    private const double hit_explosion_fade_in_duration = 80;

    private readonly BmsLayoutVariant layoutVariant;
    private readonly int maniaKeyCount;
    private readonly Lazy<bool> isLegacySkin;

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

        isLegacySkin = new Lazy<bool>(() => GetConfig<SkinConfiguration.LegacySetting, decimal>(SkinConfiguration.LegacySetting.Version) != null);
    }

    public override Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
    {
        if (lookup is GlobalSkinnableContainerLookup containerLookup && containerLookup.Lookup == GlobalSkinnableContainers.MainHUDComponents && containerLookup.Ruleset != null)
            return createLegacyHud();

        if (lookup is SkinComponentLookup<HitResult> resultLookup && isLegacySkin.Value)
            return getResult(resultLookup.Component) ?? base.GetDrawableComponent(lookup);

        if (lookup is not BmsSkinComponentLookup bmsLookup)
            return base.GetDrawableComponent(lookup);

        if (!isLegacySkin.Value)
            return null;

        return bmsLookup.Component switch
        {
            BmsSkinComponents.Note when hasAnimation(getNoteImageName(bmsLookup)) => new LegacyBmsNotePiece(this, bmsLookup),
            BmsSkinComponents.ColumnBackground => new LegacyBmsColumnBackground(this, bmsLookup),
            BmsSkinComponents.HitTarget when bmsLookup.ColumnIndex == null && hasAnimation(getHitTargetImageName()) => new LegacyBmsHitTarget(this),
            BmsSkinComponents.KeyArea when hasAnimation(getKeyImageName(bmsLookup, false)) => new LegacyBmsKeyArea(this, bmsLookup),
            BmsSkinComponents.HitExplosion when hasAnimation(getHitExplosionImageName(bmsLookup)) => new LegacyBmsHitExplosion(this, bmsLookup),
            BmsSkinComponents.StageBackground when hasAnyAnimation(getStageBackgroundImageNames()) => new LegacyBmsStageBackground(this),
            BmsSkinComponents.StageForeground when hasAnimation(getStageForegroundImageName()) => new LegacyBmsStageForeground(this),
            _ => null,
        };
    }

    public override IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
    {
        if (lookup is BmsSkinConfigurationLookup bmsLookup)
            return Skin.GetConfig<LegacyManiaSkinConfigurationLookup, TValue>(new LegacyManiaSkinConfigurationLookup(maniaKeyCount, bmsLookup.Lookup,
                bmsLookup.ComponentLookup?.ManiaColumnIndex ?? bmsLookup.ColumnIndex));

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
            new LegacyHealthDisplay(),
            new BarHitErrorMeter { Anchor = Anchor.BottomCentre, Origin = Anchor.CentreLeft, Rotation = -90 },
        ],
    };

    private Drawable? getAnimation(string name) => this.GetAnimation(name, WrapMode.ClampToEdge, WrapMode.ClampToEdge, true, true);

    private bool hasAnimation(string name) => getAnimation(name) != null;

    private bool hasAnyAnimation(params string[] names) => names.Any(hasAnimation);

    private string getNoteImageName(BmsSkinComponentLookup lookup) =>
        getManiaConfig<string>(LegacyManiaSkinConfigurationLookups.NoteImage, lookup)?.Value
        ?? $"mania-note{fallbackColumnIndex(lookup)}";

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
                d.Anchor = Anchor.BottomCentre;
                d.Origin = Anchor.BottomCentre;
                d.RelativeSizeAxes = Axes.X;
                d.Width = 1;
            });

            downSprite = transformer.getAnimation(transformer.getKeyImageName(lookup, true))?.With(d =>
            {
                d.Anchor = Anchor.BottomCentre;
                d.Origin = Anchor.BottomCentre;
                d.RelativeSizeAxes = Axes.X;
                d.Width = 1;
                d.Alpha = 0;
            });

            InternalChild = new Container
            {
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.BottomCentre,
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
        private Drawable? noteAnimation;

        public LegacyBmsNotePiece(BmsLegacySkinTransformer transformer, BmsSkinComponentLookup lookup)
        {
            this.transformer = transformer;
            this.lookup = lookup;
            widthForNoteHeightScale = transformer.getManiaConfig<float>(LegacyManiaSkinConfigurationLookups.WidthForNoteHeightScale)?.Value;

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            Origin = Anchor.BottomLeft;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            InternalChild = noteAnimation = transformer.getAnimation(transformer.getNoteImageName(lookup))?.With(d =>
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

            var noteHeight = widthForNoteHeightScale ?? DrawWidth;
            noteAnimation.Scale = Vector2.Divide(new Vector2(DrawWidth, noteHeight), texture.DisplayWidth);
        }
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
            var leftLineWidth = transformer.getManiaConfig<float>(LegacyManiaSkinConfigurationLookups.LeftLineWidth, lookup)?.Value ?? 1;
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
                new Box
                {
                    RelativeSizeAxes = Axes.Y,
                    Scale = new Vector2(0.740f, 1),
                    Width = leftLineWidth,
                    Colour = lineColour,
                    Alpha = leftLineWidth > 0 ? 1 : 0,
                },
                new Box
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    RelativeSizeAxes = Axes.Y,
                    Scale = new Vector2(0.740f, 1),
                    Width = rightLineWidth,
                    Colour = lineColour,
                    Alpha = rightLineWidth > 0 ? 1 : 0,
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

            var images = transformer.getStageBackgroundImageNames();

            InternalChildren =
            [
                leftSprite = transformer.getAnimation(images[0])?.With(d =>
                {
                    d.Anchor = Anchor.TopLeft;
                    d.Origin = Anchor.CentreRight;
                    d.X = 0.05f;
                }) ?? Empty(),
                rightSprite = transformer.getAnimation(images[1])?.With(d =>
                {
                    d.Anchor = Anchor.TopRight;
                    d.Origin = Anchor.CentreLeft;
                    d.X = -0.05f;
                }) ?? Empty(),
            ];
        }

        protected override void Update()
        {
            base.Update();

            if (leftSprite?.DrawHeight > 0)
                leftSprite.Scale = new Vector2(1, DrawHeight / leftSprite.DrawHeight);

            if (rightSprite?.DrawHeight > 0)
                rightSprite.Scale = new Vector2(1, DrawHeight / rightSprite.DrawHeight);
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
