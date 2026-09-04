// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Pooling;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Localisation.HUD;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play.HUD.HitErrorMeters;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

[Cached]
public partial class BmsHitErrorMeter : HitErrorMeter
{
    [SettingSource(typeof(BarHitErrorMeterStrings), nameof(BarHitErrorMeterStrings.JudgementLineThickness), nameof(BarHitErrorMeterStrings.JudgementLineThicknessDescription))]
    public BindableNumber<float> JudgementLineThickness { get; } = new(4)
    {
        MinValue = 1,
        MaxValue = 8,
        Precision = 0.1f,
    };

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.HitErrorMeterFadeDuration), nameof(BmsStrings.HitErrorMeterFadeDurationDescription))]
    public BindableNumber<float> JudgementFadeDuration { get; } = new BindableFloat(5)
    {
        MinValue = 0.1f,
        MaxValue = 20,
        Precision = 0.1f,
    };

    [SettingSource(typeof(BarHitErrorMeterStrings), nameof(BarHitErrorMeterStrings.ColourBarVisibility))]
    public Bindable<bool> ColourBarVisibility { get; } = new BindableBool(true);

    [SettingSource(typeof(BarHitErrorMeterStrings), nameof(BarHitErrorMeterStrings.ShowMovingAverage), nameof(BarHitErrorMeterStrings.ShowMovingAverageDescription))]
    public Bindable<bool> ShowMovingAverage { get; } = new BindableBool(true);

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.HitErrorMeterShowEmptyPoor), nameof(BmsStrings.HitErrorMeterShowEmptyPoorDescription))]
    public Bindable<bool> ShowEmptyPoor { get; } = new BindableBool(true);

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.HitErrorMeterShowPoor), nameof(BmsStrings.HitErrorMeterShowPoorDescription))]
    public Bindable<bool> ShowPoor { get; } = new BindableBool(true);

    [SettingSource(typeof(BarHitErrorMeterStrings), nameof(BarHitErrorMeterStrings.CentreMarkerStyle), nameof(BarHitErrorMeterStrings.CentreMarkerStyleDescription))]
    public Bindable<BarHitErrorMeter.CentreMarkerStyles> CentreMarkerStyle { get; } = new(BarHitErrorMeter.CentreMarkerStyles.Circle);

    [SettingSource(typeof(BarHitErrorMeterStrings), nameof(BarHitErrorMeterStrings.LabelStyle), nameof(BarHitErrorMeterStrings.LabelStyleDescription))]
    public Bindable<BarHitErrorMeter.LabelStyles> LabelStyle { get; } = new(BarHitErrorMeter.LabelStyles.Icons);

    private const int judgement_line_width = 14;
    private const int max_concurrent_judgements = 50;
    private const int centre_marker_size = 8;
    private const float chevron_size = 8;
    private const float component_padding = 2;
    private const float default_bar_length = 200;
    private const float minimum_width = 44;
    private const float minimum_height = component_padding * 2 + chevron_size + centre_marker_size;

    private readonly DrawablePool<JudgementLine> judgementLinePool = new(max_concurrent_judgements);

    private BmsHitErrorMeterDomain domain;
    private double fastPoorDisplayOffset;
    private double slowPoorDisplayOffset;
    private double floatingAverage;
    private BmsScoreProcessor? scoreProcessor;

    private SpriteIcon arrow = null!;
    private Container rotatedContent = null!;
    private Container arrowContainer = null!;
    private Container colourBars = null!;
    private Container windowColourBar = null!;
    private Box emptyPoorColourBar = null!;
    private Container judgementsContainer = null!;
    private UprightAspectMaintainingContainer labelFast = null!;
    private UprightAspectMaintainingContainer labelSlow = null!;
    private Drawable[]? centreMarkerDrawables;

    public override Vector2 Size
    {
        get => base.Size;
        set => base.Size = new Vector2(Math.Max(minimum_width, value.X), Math.Max(minimum_height, value.Y));
    }

    public override float Width
    {
        get => base.Width;
        set => base.Width = Math.Max(minimum_width, value);
    }

    public override float Height
    {
        get => base.Height;
        set => base.Height = Math.Max(minimum_height, value);
    }

    public BmsHitErrorMeter()
    {
        AutoSizeAxes = Axes.None;
        Size = new Vector2(component_padding * 2 + default_bar_length, component_padding * 2 + chevron_size + judgement_line_width);
    }

    [BackgroundDependencyLoader(true)]
    private void load(DrawableRuleset? drawableRuleset, ScoreProcessor? scoreProcessor)
    {
        const int colour_bar_width = 2;

        var beatmap = (drawableRuleset as BmsDrawableRuleset)?.Beatmap as BmsBeatmap;
        this.scoreProcessor = scoreProcessor as BmsScoreProcessor;
        var layout = beatmap?.LayoutVariant ?? BmsLayoutVariant.Bme7K;
        var judgementRate = beatmap?.HitObjects.FirstOrDefault(h => h is not BmsLandmine)?.EffectiveJudgementRate
                            ?? BmsJudgementProfileProvider.RateForRank(layout, beatmap?.Rank ?? 2);
        domain = CreateDomain(layout, judgementRate);

        var headWindows = BmsJudgementProfileProvider.GetTable(layout, 1, judgementRate, tail: false);
        fastPoorDisplayOffset = -headWindows.FastWindowFor(HitResult.Ok);
        slowPoorDisplayOffset = headWindows.SlowWindowFor(HitResult.Ok);

        InternalChild = rotatedContent = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Rotation = -90,
            Size = new Vector2(Height, Width),
            Child = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding(component_padding),
                Children =
                [
                    judgementLinePool,
                    colourBars = new Container
                    {
                        Name = "colour axis",
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Left = chevron_size },
                        Children =
                        [
                            windowColourBar = new Container
                            {
                                Name = "judgement windows",
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Width = colour_bar_width,
                                RelativeSizeAxes = Axes.Y,
                            },
                            judgementsContainer = new Container
                            {
                                Name = "judgements",
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                RelativeSizeAxes = Axes.Both,
                            },
                            labelFast = new UprightAspectMaintainingContainer
                            {
                                Name = "fast label",
                                AutoSizeAxes = Axes.Both,
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.Centre,
                                Y = -10,
                            },
                            labelSlow = new UprightAspectMaintainingContainer
                            {
                                Name = "slow label",
                                AutoSizeAxes = Axes.Both,
                                Anchor = Anchor.BottomCentre,
                                Origin = Anchor.Centre,
                                Y = 10,
                            },
                        ],
                    },
                    arrowContainer = new Container
                    {
                        Name = "average chevron",
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreRight,
                        Width = chevron_size,
                        X = chevron_size,
                        RelativeSizeAxes = Axes.Y,
                        Alpha = 0,
                        Scale = new Vector2(0, 1),
                        Child = arrow = new SpriteIcon
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.Centre,
                            RelativePositionAxes = Axes.Y,
                            Y = domain.RelativePosition(0),
                            Icon = FontAwesome.Solid.ChevronRight,
                            Size = new Vector2(chevron_size),
                        },
                    },
                ],
            },
        };

        createColourBar(windowColourBar, headWindows);
    }

    protected override void Update()
    {
        base.Update();
        rotatedContent.Size = new Vector2(Height, Width);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (scoreProcessor != null)
            scoreProcessor.EmptyPoorRegistered += onEmptyPoorRegistered;

        colourBars.Height = 0;
        colourBars.ResizeHeightTo(1, 800, Easing.OutQuint);

        CentreMarkerStyle.BindValueChanged(style => recreateCentreMarker(style.NewValue), true);
        LabelStyle.BindValueChanged(style => recreateLabels(style.NewValue), true);
        ShowEmptyPoor.BindValueChanged(visible =>
        {
            emptyPoorColourBar.FadeTo(visible.NewValue ? 1 : 0, 500, Easing.OutQuint);
        }, true);
        ColourBarVisibility.BindValueChanged(visible =>
        {
            windowColourBar.FadeTo(visible.NewValue ? 1 : 0, 500, Easing.OutQuint);
        }, true);

        using (arrowContainer.BeginDelayedSequence(450))
        {
            ShowMovingAverage.BindValueChanged(visible =>
            {
                arrowContainer.FadeTo(visible.NewValue ? 1 : 0, 250, Easing.OutQuint);
                arrowContainer.ScaleTo(visible.NewValue ? Vector2.One : new Vector2(0, 1), 250, Easing.OutQuint);
            }, true);
        }
    }

    private void createColourBar(Container target, BmsJudgementWindowTable windows)
    {
        HitResult[] results = [HitResult.Ok, HitResult.Good, HitResult.Great, HitResult.Perfect];

        var emptyPoorTop = domain.RelativePosition(-windows.FastWindowFor(HitResult.Miss));
        var emptyPoorBottom = domain.RelativePosition(-windows.FastWindowFor(HitResult.Ok));

        target.Add(emptyPoorColourBar = new Box
        {
            Name = "empty poor window",
            RelativePositionAxes = Axes.Y,
            RelativeSizeAxes = Axes.Both,
            Y = emptyPoorTop,
            Height = Math.Max(0, emptyPoorBottom - emptyPoorTop),
            Colour = BmsHitResultColours.ForHitResult(HitResult.Miss),
        });

        foreach (var result in results)
        {
            var top = domain.RelativePosition(-windows.FastWindowFor(result));
            var bottom = domain.RelativePosition(windows.SlowWindowFor(result));

            target.Add(new Box
            {
                Name = $"{result} window",
                RelativePositionAxes = Axes.Y,
                RelativeSizeAxes = Axes.Both,
                Y = top,
                Height = Math.Max(0, bottom - top),
                Colour = BmsHitResultColours.ForHitResult(result),
            });
        }
    }

    private void recreateCentreMarker(BarHitErrorMeter.CentreMarkerStyles style)
    {
        if (centreMarkerDrawables != null)
        {
            foreach (var drawable in centreMarkerDrawables)
            {
                drawable.ScaleTo(0, 500, Easing.OutQuint).FadeOut(500, Easing.OutQuint);
                drawable.Expire();
            }

            centreMarkerDrawables = null;
        }

        var position = domain.RelativePosition(0);

        switch (style)
        {
            case BarHitErrorMeter.CentreMarkerStyles.None:
                break;

            case BarHitErrorMeter.CentreMarkerStyles.Circle:
                centreMarkerDrawables =
                [
                    createCentreCircle("middle marker behind", centre_marker_size, Colour4.White, float.MaxValue, position),
                    createCentreCircle("middle marker in front", centre_marker_size / 2f, Colour4.White, float.MinValue, position),
                ];
                break;

            case BarHitErrorMeter.CentreMarkerStyles.Line:
                const float border_size = 1.5f;
                centreMarkerDrawables =
                [
                    createCentreLine("middle marker behind", judgement_line_width, centre_marker_size / 3f, Colour4.White, float.MaxValue, position),
                    createCentreLine("middle marker in front", judgement_line_width - border_size, centre_marker_size / 3f - border_size,
                        Colour4.White, float.MinValue, position),
                ];
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(style), style, null);
        }

        if (centreMarkerDrawables == null)
            return;

        foreach (var drawable in centreMarkerDrawables)
        {
            colourBars.Add(drawable);
            drawable.FadeInFromZero(500).ScaleTo(0).ScaleTo(1, 1000, Easing.OutElasticHalf);
        }
    }

    private static Circle createCentreCircle(string name, float size, Colour4 colour, float depth, float position) => new()
    {
        Name = name,
        Colour = colour,
        Anchor = Anchor.TopCentre,
        Origin = Anchor.Centre,
        RelativePositionAxes = Axes.Y,
        Y = position,
        Depth = depth,
        Size = new Vector2(size),
    };

    private static Box createCentreLine(string name, float width, float height, Colour4 colour, float depth, float position) => new()
    {
        Name = name,
        Colour = colour,
        Anchor = Anchor.TopCentre,
        Origin = Anchor.Centre,
        RelativePositionAxes = Axes.Y,
        Y = position,
        Depth = depth,
        Size = new Vector2(width, height),
    };

    private void recreateLabels(BarHitErrorMeter.LabelStyles style)
    {
        const float icon_size = 14;

        switch (style)
        {
            case BarHitErrorMeter.LabelStyles.None:
                labelFast.Clear();
                labelSlow.Clear();
                break;

            case BarHitErrorMeter.LabelStyles.Icons:
                labelFast.Child = new SpriteIcon { Size = new Vector2(icon_size), Icon = OsuIcon.Hare };
                labelSlow.Child = new SpriteIcon { Size = new Vector2(icon_size), Icon = OsuIcon.Tortoise };
                break;

            case BarHitErrorMeter.LabelStyles.Text:
                labelFast.Child = createLabel(BmsStrings.Fast);
                labelSlow.Child = createLabel(BmsStrings.Slow);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(style), style, null);
        }

        labelFast.FadeInFromZero(500);
        labelSlow.FadeInFromZero(500);
    }

    private static OsuSpriteText createLabel(LocalisableString text) => new()
    {
        Text = text,
        Font = OsuFont.Default.With(size: 10),
        Height = 12,
    };

    protected override void OnNewJudgement(JudgementResult judgement)
    {
        if (!judgement.Type.IsScorable() || judgement.Type.IsBonus() || judgement.HitObject is BmsLandmine)
            return;

        foreach (var observation in GetTimingObservations(judgement))
        {
            var displayOffset = GetDisplayOffset(observation, fastPoorDisplayOffset, slowPoorDisplayOffset, ShowPoor.Value);

            if (displayOffset == null)
                continue;

            addJudgement(displayOffset.Value, observation.Result, AffectsMovingAverage(observation.Result));
        }
    }

    private void onEmptyPoorRegistered(BmsTimingObservation observation)
        => Schedule(() =>
        {
            if (ShowEmptyPoor.Value)
                addJudgement(observation.TimeOffset, observation.Result, affectMovingAverage: false);
        });

    private void addJudgement(double timeOffset, HitResult result, bool affectMovingAverage)
    {
        const int arrow_move_duration = 800;

        if (judgementsContainer.Count >= max_concurrent_judgements)
        {
            var old = judgementsContainer.FirstOrDefault();

            if (old != null)
            {
                old.ClearTransforms();
                judgementsContainer.Remove(old, disposeImmediately: false);
            }
        }

        judgementLinePool.Get(drawableJudgement =>
        {
            drawableJudgement.Y = domain.RelativePosition(timeOffset);
            drawableJudgement.Colour = BmsHitResultColours.ForHitResult(result);
            judgementsContainer.Add(drawableJudgement);
        });

        if (affectMovingAverage)
        {
            floatingAverage = floatingAverage * 0.9 + timeOffset * 0.1;
            arrow.MoveToY(domain.RelativePosition(floatingAverage), arrow_move_duration, Easing.OutQuint);
        }
    }

    internal static IReadOnlyList<BmsHitErrorTimingObservation> GetTimingObservations(JudgementResult judgement)
    {
        if (judgement is BmsLongNoteJudgementResult longNoteResult)
        {
            return longNoteResult.EndpointResults
                .Select(endpoint => new BmsHitErrorTimingObservation(endpoint.TimeOffset, endpoint.Result))
                .ToArray();
        }

        return [new BmsHitErrorTimingObservation(judgement.TimeOffset, judgement.Type)];
    }

    internal static double? GetDisplayOffset(
        BmsHitErrorTimingObservation observation,
        double fastPoorDisplayOffset,
        double slowPoorDisplayOffset,
        bool showPoor)
    {
        // POOR has no finite miss-side edge, so retain its timing direction at the corresponding BAD boundary.
        if (observation.Result == HitResult.Meh)
            return showPoor
                ? observation.TimeOffset < 0 ? fastPoorDisplayOffset : slowPoorDisplayOffset
                : null;

        return observation.Result.IsHit() ? observation.TimeOffset : null;
    }

    internal static bool AffectsMovingAverage(HitResult result) => result.IsHit() && result != HitResult.Meh;

    internal static BmsHitErrorMeterDomain CreateDomain(BmsLayoutVariant layout, double judgementRate)
    {
        int[] columns = BmsLayout.IsScratchColumn(0, layout) ? [0, 1] : [1];
        var tables = columns.SelectMany(column => new[]
        {
            BmsJudgementProfileProvider.GetTable(layout, column, judgementRate, tail: false),
            BmsJudgementProfileProvider.GetTable(layout, column, judgementRate, tail: true),
        });

        var fastExtent = tables.Max(table => Math.Max(table.FastWindowFor(HitResult.Ok), table.FastWindowFor(HitResult.Miss)));
        var slowExtent = tables.Max(table => Math.Max(table.SlowWindowFor(HitResult.Ok), table.SlowWindowFor(HitResult.Miss)));
        var extent = Math.Max(1, Math.Max(fastExtent, slowExtent));
        return new BmsHitErrorMeterDomain(-extent, extent);
    }

    public override void Clear()
    {
        foreach (var judgement in judgementsContainer)
        {
            judgement.ClearTransforms();
            judgement.Expire();
        }

        floatingAverage = 0;
        arrow.MoveToY(domain.RelativePosition(0));
    }

    protected override void Dispose(bool isDisposing)
    {
        if (scoreProcessor != null)
            scoreProcessor.EmptyPoorRegistered -= onEmptyPoorRegistered;

        base.Dispose(isDisposing);
    }

    internal partial class JudgementLine : PoolableDrawable
    {
        public readonly BindableNumber<float> JudgementLineThickness = new BindableFloat();

        [Resolved]
        private BmsHitErrorMeter hitErrorMeter { get; set; } = null!;

        public JudgementLine()
        {
            RelativeSizeAxes = Axes.X;
            RelativePositionAxes = Axes.Y;
            Blending = BlendingParameters.Additive;
            Origin = Anchor.Centre;
            Anchor = Anchor.TopCentre;
            InternalChild = new Circle { RelativeSizeAxes = Axes.Both };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            JudgementLineThickness.BindTo(hitErrorMeter.JudgementLineThickness);
            JudgementLineThickness.BindValueChanged(thickness => Height = thickness.NewValue, true);
        }

        protected override void PrepareForUse()
        {
            base.PrepareForUse();

            const int judgement_fade_in_duration = 100;
            var judgementFadeOutDuration = hitErrorMeter.JudgementFadeDuration.Value * 1000;

            Alpha = 0;
            Width = 0;
            this.FadeTo(0.6f, judgement_fade_in_duration, Easing.OutQuint)
                .ResizeWidthTo(1, judgement_fade_in_duration, Easing.OutQuint)
                .Then()
                .FadeOut(judgementFadeOutDuration)
                .ResizeWidthTo(0, judgementFadeOutDuration, Easing.InQuint)
                .Expire();
        }
    }
}

internal readonly record struct BmsHitErrorTimingObservation(double TimeOffset, HitResult Result);

internal readonly record struct BmsHitErrorMeterDomain(double FastOffset, double SlowOffset)
{
    public float RelativePosition(double timeOffset)
        => Math.Clamp((float)((timeOffset - FastOffset) / (SlowOffset - FastOffset)), 0, 1);
}
