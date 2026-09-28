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
using osu.Game.Configuration;
using osu.Game.Overlays.Settings;
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
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

[Cached]
public partial class BmsHitErrorMeter : HitErrorMeter
{
    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.HitErrorMeterLineThickness), nameof(BmsStrings.HitErrorMeterLineThicknessDescription))]
    public BindableNumber<float> JudgementLineThickness { get; } = new BindableFloat(3)
    {
        MinValue = 1,
        MaxValue = 8,
        Precision = 0.1f,
    };

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.HitErrorMeterBackgroundOpacity), nameof(BmsStrings.HitErrorMeterBackgroundOpacityDescription),
        SettingControlType = typeof(SettingsPercentageSlider<float>))]
    public BindableNumber<float> BackgroundOpacity { get; } = new BindableFloat(0.6f)
    {
        MinValue = 0,
        MaxValue = 1,
        Precision = 0.01f,
    };

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.HitErrorMeterFadeDuration), nameof(BmsStrings.HitErrorMeterFadeDurationDescription))]
    public BindableNumber<float> JudgementFadeDuration { get; } = new BindableFloat(10)
    {
        MinValue = 0.1f,
        MaxValue = 20,
        Precision = 0.1f,
    };

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.HitErrorMeterShowEmptyPoor), nameof(BmsStrings.HitErrorMeterShowEmptyPoorDescription))]
    public Bindable<bool> ShowEmptyPoor { get; } = new BindableBool(true);

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.HitErrorMeterShowPoor), nameof(BmsStrings.HitErrorMeterShowPoorDescription))]
    public Bindable<bool> ShowPoor { get; } = new BindableBool(true);

    private const float bar_height = 3;

    private readonly DrawablePool<JudgementLine> judgementLinePool = new(50);

    private BmsHitErrorMeterDomain domain;
    private double fastPoorDisplayOffset;
    private double slowPoorDisplayOffset;
    private double floatingAverage;
    private BmsScoreProcessor? scoreProcessor;

    private Triangle arrow = null!;
    private Box background = null!;
    private Box emptyPoorColourBar = null!;
    private Container judgementsContainer = null!;

    public BmsHitErrorMeter()
    {
        Height = bar_height * 4 * LegacySkin.STABLE_MAGIC_SCALE_FACTOR;
    }

    [BackgroundDependencyLoader(true)]
    private void load(DrawableRuleset? drawableRuleset, ScoreProcessor? scoreProcessor)
    {
        var beatmap = (drawableRuleset as BmsDrawableRuleset)?.Beatmap as BmsBeatmap;
        this.scoreProcessor = scoreProcessor as BmsScoreProcessor;
        var layout = beatmap?.LayoutVariant ?? BmsLayoutVariant.Bme7K;
        var judgementRate = beatmap?.HitObjects.FirstOrDefault(h => h is not BmsLandmine)?.EffectiveJudgementRate
                            ?? BmsJudgementProfileProvider.RateForRank(layout, beatmap?.Rank ?? 2);
        domain = CreateDomain(layout, judgementRate);

        var headWindows = BmsJudgementProfileProvider.GetTable(layout, 1, judgementRate, tail: false);
        fastPoorDisplayOffset = -headWindows.FastWindowFor(HitResult.Ok);
        slowPoorDisplayOffset = headWindows.SlowWindowFor(HitResult.Ok);

        // Use legacy's time-to-width scale by default while respecting dimensions saved in user skins.
        if (Width == 0)
            Width = (float)(domain.SlowOffset - domain.FastOffset) / 2 * LegacySkin.STABLE_MAGIC_SCALE_FACTOR;

        Container windowColourBar;
        InternalChildren =
        [
            background = new Box
            {
                Name = "background",
                RelativeSizeAxes = Axes.Both,
                Colour = Colour4.Black,
            },
            windowColourBar = new Container
            {
                Name = "judgement windows",
                RelativeSizeAxes = Axes.X,
                Height = bar_height * LegacySkin.STABLE_MAGIC_SCALE_FACTOR,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
            },
            new Box
            {
                Name = "centre marker",
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Y,
                Width = 1.5f * LegacySkin.STABLE_MAGIC_SCALE_FACTOR,
                Height = 1,
            },
            judgementLinePool,
            judgementsContainer = new Container
            {
                Name = "judgements",
                RelativeSizeAxes = Axes.Both,
            },
            arrow = new Triangle
            {
                Name = "average arrow",
                Size = new Vector2(17, 8) * 0.6f,
                Origin = Anchor.BottomCentre,
                RelativePositionAxes = Axes.X,
                X = domain.RelativePosition(0),
                Scale = new Vector2(1, -1),
            },
        ];

        createColourBar(windowColourBar, headWindows);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (scoreProcessor != null)
            scoreProcessor.NonConsumingJudgementRegistered += onNonConsumingJudgementRegistered;

        ShowEmptyPoor.BindValueChanged(visible => emptyPoorColourBar.Alpha = visible.NewValue ? 1 : 0, true);
        BackgroundOpacity.BindValueChanged(opacity => background.Alpha = opacity.NewValue, true);
    }

    private void createColourBar(Container target, BmsJudgementWindowTable windows)
    {
        HitResult[] results = [HitResult.Ok, HitResult.Good, HitResult.Great, HitResult.Perfect];

        var emptyPoorStart = domain.RelativePosition(-windows.FastWindowFor(HitResult.Miss));
        var emptyPoorEnd = domain.RelativePosition(-windows.FastWindowFor(HitResult.Ok));

        target.Add(emptyPoorColourBar = new Box
        {
            Name = "empty poor window",
            RelativePositionAxes = Axes.X,
            RelativeSizeAxes = Axes.Both,
            X = emptyPoorStart,
            Width = Math.Max(0, emptyPoorEnd - emptyPoorStart),
            Colour = BmsHitResultColours.ForHitResult(HitResult.Miss),
        });

        foreach (var result in results)
        {
            var start = domain.RelativePosition(-windows.FastWindowFor(result));
            var end = domain.RelativePosition(windows.SlowWindowFor(result));

            target.Add(new Box
            {
                Name = $"{result} window",
                RelativePositionAxes = Axes.X,
                RelativeSizeAxes = Axes.Both,
                X = start,
                Width = Math.Max(0, end - start),
                Colour = BmsHitResultColours.ForHitResult(result),
            });
        }
    }

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

    private void onNonConsumingJudgementRegistered(BmsTimingObservation observation)
        => Schedule(() =>
        {
            if (observation.Result != HitResult.Miss || ShowEmptyPoor.Value)
                addJudgement(observation.TimeOffset, observation.Result, AffectsMovingAverage(observation.Result));
        });

    private void addJudgement(double timeOffset, HitResult result, bool affectMovingAverage)
    {
        var relativePosition = domain.RelativePosition(timeOffset);

        judgementLinePool.Get(drawableJudgement =>
        {
            drawableJudgement.X = relativePosition;
            drawableJudgement.Colour = BmsHitResultColours.ForHitResult(result);
            judgementsContainer.Add(drawableJudgement);
        });

        if (affectMovingAverage)
        {
            floatingAverage = floatingAverage * 0.8 + (relativePosition - 0.5f) * 0.2;
            arrow.MoveToX((float)floatingAverage + 0.5f, 800, Easing.Out);
        }
    }

    internal static IReadOnlyList<BmsHitErrorTimingObservation> GetTimingObservations(JudgementResult judgement)
    {
        if (judgement is BmsJudgementResult { SuppressPenalty: true })
            return [];

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
        arrow.MoveToX(domain.RelativePosition(0));
    }

    protected override void Dispose(bool isDisposing)
    {
        if (scoreProcessor != null)
            scoreProcessor.NonConsumingJudgementRegistered -= onNonConsumingJudgementRegistered;

        base.Dispose(isDisposing);
    }

    internal partial class JudgementLine : PoolableDrawable
    {
        private readonly BindableNumber<float> judgementLineThickness = new BindableFloat();

        [Resolved]
        private BmsHitErrorMeter hitErrorMeter { get; set; } = null!;

        public JudgementLine()
        {
            RelativeSizeAxes = Axes.Y;
            Height = 1;
            RelativePositionAxes = Axes.X;
            Blending = BlendingParameters.Additive;
            Anchor = Anchor.CentreLeft;
            Origin = Anchor.Centre;
            InternalChild = new Box { RelativeSizeAxes = Axes.Both };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            judgementLineThickness.BindTo(hitErrorMeter.JudgementLineThickness);
            judgementLineThickness.BindValueChanged(thickness => Width = thickness.NewValue, true);
        }

        protected override void PrepareForUse()
        {
            base.PrepareForUse();

            this.FadeTo(0.4f)
                .Then()
                .FadeOut(hitErrorMeter.JudgementFadeDuration.Value * 1000)
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
