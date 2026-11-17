// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Configuration;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.BmsRuleset.Localisation;
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

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.HitErrorMeterColourBarHeight), nameof(BmsStrings.HitErrorMeterColourBarHeightDescription))]
    public BindableNumber<float> ColourBarHeight { get; } = new BindableFloat(bar_height)
    {
        MinValue = 0,
        MaxValue = 20,
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

    private BmsHitErrorMeterDomain domain;
    private GameplayProfile? profile;
    private IReadOnlyList<BmsHitErrorWindow> windows = [];
    private double fastUntimedDisplayOffset;
    private double slowUntimedDisplayOffset;
    private double floatingAverage;
    private bool averageUpdatePending;
    private BmsScoreProcessor? scoreProcessor;
    private IBmsGameplayEvents? gameplayEvents;

    private Triangle arrow = null!;
    private Box background = null!;
    private readonly List<Box> emptyInputColourBars = [];
    private Container windowColourBar = null!;

    internal JudgementsLayer Judgements { get; private set; } = null!;

    public BmsHitErrorMeter()
    {
        Height = bar_height * 4 * LegacySkin.STABLE_MAGIC_SCALE_FACTOR;
    }

    [BackgroundDependencyLoader(true)]
    private void load(DrawableRuleset? drawableRuleset, ScoreProcessor? scoreProcessor)
    {
        this.scoreProcessor = scoreProcessor as BmsScoreProcessor;
        gameplayEvents = (drawableRuleset as BmsDrawableRuleset)?.GameplayEvents;
        if (gameplayEvents != null) gameplayEvents.ReplaySeeked += resetReplayDisplay;
        var engineChart = this.scoreProcessor?.EngineChart;
        profile = engineChart?.Profile;
        windows = engineChart == null ? [] : CreateWindows(engineChart);
        domain = CreateDomain(windows);

        // Use legacy's time-to-width scale by default while respecting dimensions saved in user skins.
        if (Width == 0)
            Width = (float)(domain.SlowOffset - domain.FastOffset) / 2 * LegacySkin.STABLE_MAGIC_SCALE_FACTOR;

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
            Judgements = new JudgementsLayer(this)
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

        createColourBar(windowColourBar);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (scoreProcessor != null)
            scoreProcessor.NonConsumingJudgementRegistered += onNonConsumingJudgementRegistered;

        ShowEmptyPoor.BindValueChanged(visible =>
        {
            foreach (var bar in emptyInputColourBars)
                bar.Alpha = visible.NewValue ? 1 : 0;
            (fastUntimedDisplayOffset, slowUntimedDisplayOffset) = GetUntimedDisplayOffsets(windows, visible.NewValue);

            Judgements.UpdateBoundaryPositions(domain.RelativePosition(fastUntimedDisplayOffset), domain.RelativePosition(slowUntimedDisplayOffset));
        }, true);
        BackgroundOpacity.BindValueChanged(opacity => background.Alpha = opacity.NewValue, true);
        ColourBarHeight.BindValueChanged(height =>
        {
            windowColourBar.Height = height.NewValue * LegacySkin.STABLE_MAGIC_SCALE_FACTOR;
            windowColourBar.Alpha = height.NewValue > 0 ? 1 : 0;
        }, true);
        JudgementLineThickness.BindValueChanged(_ => Judgements.Invalidate(Invalidation.DrawSize));
    }

    private void resetReplayDisplay()
    {
        arrow.ClearTransforms();
        Clear();
    }

    private void createColourBar(Container target)
    {
        foreach (var window in windows.OrderByDescending(window => window.SlowOffset - window.FastOffset))
        {
            var start = domain.RelativePosition(window.FastOffset);
            var end = domain.RelativePosition(window.SlowOffset);
            var bar = new Box
            {
                Name = $"{window.Result} window",
                RelativePositionAxes = Axes.X,
                RelativeSizeAxes = Axes.Both,
                X = start,
                Width = Math.Max(0, end - start),
                Colour = profile!.JudgementColour(window.Result),
            };
            target.Add(bar);
            if (window.Kind == ResultTimingKind.EmptyInput)
                emptyInputColourBars.Add(bar);
        }
    }

    protected override void OnNewJudgement(JudgementResult judgement)
    {
        if (!judgement.Type.IsScorable() || judgement.Type.IsBonus() || judgement.HitObject is BmsLandmine)
            return;

        if (judgement is BmsJudgementResult { SuppressPenalty: true })
            return;

        if (judgement is BmsLongNoteJudgementResult longNoteResult)
        {
            foreach (var endpoint in longNoteResult.EndpointResults)
                processObservation(endpoint.TimeOffset, endpoint.Result);

            return;
        }

        processObservation(judgement.TimeOffset, judgement.Type);
    }

    private void processObservation(double offset, HitResult result)
    {
        if (profile == null)
            return;

        var kind = profile.TimingKindFor(result);
        var fastMiss = kind == ResultTimingKind.Untimed && result == HitResult.Miss && offset < 0;
        var displayOffset = GetDisplayOffset(offset, kind, fastUntimedDisplayOffset,
            slowUntimedDisplayOffset, ShowPoor.Value, ShowEmptyPoor.Value);
        if (displayOffset == null)
            return;

        // An early Mania MISS comes from a press with a meaningful offset. Keep its position
        // without including misses in the moving average or attaching it to a window boundary.
        addJudgement(fastMiss ? offset : displayOffset.Value, result, kind == ResultTimingKind.Timed,
            kind == ResultTimingKind.Untimed && !fastMiss ? offset < 0 : null);
    }

    private void onNonConsumingJudgementRegistered(BmsTimingObservation observation)
        => Schedule(() => processObservation(observation.TimeOffset, observation.Result));

    private void addJudgement(double timeOffset, HitResult result, bool affectMovingAverage, bool? boundaryIsFast = null)
    {
        var relativePosition = domain.RelativePosition(timeOffset);

        Judgements.AddJudgement(relativePosition, profile!.JudgementColour(result), boundaryIsFast);

        if (affectMovingAverage)
        {
            floatingAverage = floatingAverage * 0.8 + (relativePosition - 0.5f) * 0.2;
            averageUpdatePending = true;
        }
    }

    protected override void Update()
    {
        base.Update();

        // The scheduler has delivered this frame's judgements. Preserve every average
        // observation, but create only the final animation before the arrow updates.
        if (averageUpdatePending)
        {
            averageUpdatePending = false;
            arrow.MoveToX((float)floatingAverage + 0.5f, 800, Easing.Out);
        }
    }

    internal static double? GetDisplayOffset(double offset, ResultTimingKind kind, double fastBoundary, double slowBoundary,
        bool showUntimed, bool showEmptyInput) => kind switch
    {
        ResultTimingKind.Timed => offset,
        ResultTimingKind.Untimed => showUntimed ? offset < 0 ? fastBoundary : slowBoundary : null,
        ResultTimingKind.EmptyInput => showEmptyInput ? offset : null,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal static (double FastOffset, double SlowOffset) GetUntimedDisplayOffsets(IReadOnlyList<BmsHitErrorWindow> windows, bool showEmptyInput)
    {
        var visible = windows.Where(window => showEmptyInput || window.Kind != ResultTimingKind.EmptyInput).ToArray();
        return (visible.Select(window => window.FastOffset).DefaultIfEmpty(0).Min(),
            visible.Select(window => window.SlowOffset).DefaultIfEmpty(0).Max());
    }

    internal static IReadOnlyList<BmsHitErrorWindow> CreateWindows(BmsEngineChart chart)
        => chart.Objects.Where(note => note is not BmsLandmine)
            .Select(chart.TimingFor)
            .Distinct()
            .SelectMany(timing => timing.TailWindows is { } tail
                ? new[] { (Windows: timing.HeadWindows, Deadline: timing.HeadDeadline), (Windows: tail, Deadline: timing.TailDeadline) }
                : [(Windows: timing.HeadWindows, Deadline: timing.HeadDeadline)])
            .SelectMany(table => table.Windows.Entries.Select(window =>
            {
                var result = chart.Profile.HitResultFor(window.Result);
                var kind = chart.Profile.TimingKindFor(result);
                // A consuming MISS can be pressed early, but its late side expires at the passive deadline.
                var late = result == HitResult.Miss && kind == ResultTimingKind.Untimed ? Math.Min(window.Late, table.Deadline) : window.Late;
                return new BmsHitErrorWindow(result, kind, window.Early / 1000.0, late / 1000.0);
            }))
            .GroupBy(window => window.Result)
            .Select(group => new BmsHitErrorWindow(group.Key, chart.Profile.TimingKindFor(group.Key),
                group.Min(window => window.FastOffset), group.Max(window => window.SlowOffset)))
            .ToArray();

    internal static BmsHitErrorMeterDomain CreateDomain(IReadOnlyList<BmsHitErrorWindow> windows)
    {
        // A standalone skin preview has no compiled chart or judgement regions.
        var extent = Math.Max(1, windows.Select(window => Math.Max(-window.FastOffset, window.SlowOffset)).DefaultIfEmpty(150).Max());
        return new BmsHitErrorMeterDomain(-extent, extent);
    }

    public override void Clear()
    {
        Judgements.Clear();

        averageUpdatePending = false;
        floatingAverage = 0;
        arrow.MoveToX(domain.RelativePosition(0));
    }

    protected override void Dispose(bool isDisposing)
    {
        if (gameplayEvents != null) gameplayEvents.ReplaySeeked -= resetReplayDisplay;
        if (scoreProcessor != null)
            scoreProcessor.NonConsumingJudgementRegistered -= onNonConsumingJudgementRegistered;

        base.Dispose(isDisposing);
    }
}

internal readonly record struct BmsHitErrorWindow(HitResult Result, ResultTimingKind Kind, double FastOffset, double SlowOffset);

internal readonly record struct BmsHitErrorMeterDomain(double FastOffset, double SlowOffset)
{
    public float RelativePosition(double timeOffset)
        => Math.Clamp((float)((timeOffset - FastOffset) / (SlowOffset - FastOffset)), 0, 1);
}
