// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using RectangleF = osu.Framework.Graphics.Primitives.RectangleF;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Layout;
using osu.Framework.Utils;
using osu.Game.Configuration;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
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

    private readonly JudgementLinePool judgementLinePool = new();

    private BmsHitErrorMeterDomain domain;
    private BmsJudgementWindowTable headWindows = null!;
    private double fastPoorDisplayOffset;
    private double slowPoorDisplayOffset;
    private double floatingAverage;
    private BmsScoreProcessor? scoreProcessor;

    private Triangle arrow = null!;
    private Box background = null!;
    private Box emptyPoorColourBar = null!;
    private Container windowColourBar = null!;
    private JudgementsContainer judgementsContainer = null!;

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

        headWindows = BmsJudgementProfileProvider.GetTable(layout, 1, judgementRate, tail: false);

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
            judgementLinePool,
            judgementsContainer = new JudgementsContainer(this)
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

        ShowEmptyPoor.BindValueChanged(visible =>
        {
            emptyPoorColourBar.Alpha = visible.NewValue ? 1 : 0;
            (fastPoorDisplayOffset, slowPoorDisplayOffset) = GetPoorDisplayOffsets(headWindows, visible.NewValue);

            foreach (var line in judgementsContainer.OfType<JudgementLine>())
            {
                if (line.PoorIsFast is { } isFast)
                    line.X = domain.RelativePosition(isFast ? fastPoorDisplayOffset : slowPoorDisplayOffset);
            }

            judgementsContainer.RequestUpdate();
        }, true);
        BackgroundOpacity.BindValueChanged(opacity => background.Alpha = opacity.NewValue, true);
        ColourBarHeight.BindValueChanged(height =>
        {
            windowColourBar.Height = height.NewValue * LegacySkin.STABLE_MAGIC_SCALE_FACTOR;
            windowColourBar.Alpha = height.NewValue > 0 ? 1 : 0;
        }, true);
        JudgementLineThickness.BindValueChanged(_ => judgementsContainer.RequestUpdate());
    }

    private void createColourBar(Container target, BmsJudgementWindowTable windows)
    {
        HitResult[] results = [HitResult.Ok, HitResult.Good, HitResult.Great, HitResult.Perfect];

        // Layer valid hit windows over the full E-POOR interval so removing BAD exposes only
        // offsets inside E-POOR, including its finite slow-side limit.
        var emptyPoorStart = domain.RelativePosition(-windows.FastWindowFor(HitResult.Miss));
        var emptyPoorEnd = domain.RelativePosition(windows.SlowWindowFor(HitResult.Miss));

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

        if (judgement is BmsJudgementResult { SuppressPenalty: true })
            return;

        // Keep the allocation-free path here because this method runs for every scored object;
        // GetTimingObservations remains a convenient materialised view for callers and tests.
        if (judgement is BmsLongNoteJudgementResult longNoteResult)
        {
            foreach (var endpoint in longNoteResult.EndpointResults)
                processObservation(new BmsHitErrorTimingObservation(endpoint.TimeOffset, endpoint.Result));

            return;
        }

        processObservation(new BmsHitErrorTimingObservation(judgement.TimeOffset, judgement.Type));
    }

    private void processObservation(BmsHitErrorTimingObservation observation)
    {
        var displayOffset = GetDisplayOffset(observation, fastPoorDisplayOffset, slowPoorDisplayOffset, ShowPoor.Value);

        if (displayOffset == null)
            return;

        addJudgement(displayOffset.Value, observation.Result, AffectsMovingAverage(observation.Result),
            observation.Result == HitResult.Meh ? observation.TimeOffset < 0 : null);
    }

    private void onNonConsumingJudgementRegistered(BmsTimingObservation observation)
        => Schedule(() =>
        {
            if (observation.Result != HitResult.Miss || ShowEmptyPoor.Value)
                addJudgement(observation.TimeOffset, observation.Result, AffectsMovingAverage(observation.Result));
        });

    private void addJudgement(double timeOffset, HitResult result, bool affectMovingAverage, bool? poorIsFast = null)
    {
        var relativePosition = domain.RelativePosition(timeOffset);

        var drawableJudgement = judgementLinePool.Get();
        drawableJudgement.X = relativePosition;
        drawableJudgement.PoorIsFast = poorIsFast;
        drawableJudgement.Colour = BmsHitResultColours.ForHitResult(result);
        judgementsContainer.AddJudgement(drawableJudgement);

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
        // POOR has no finite miss-side edge, so retain its timing direction at the displayed colour bar boundary.
        if (observation.Result == HitResult.Meh)
            return showPoor
                ? observation.TimeOffset < 0 ? fastPoorDisplayOffset : slowPoorDisplayOffset
                : null;

        return observation.Result.IsHit() ? observation.TimeOffset : null;
    }

    internal static bool AffectsMovingAverage(HitResult result) => result.IsHit() && result != HitResult.Meh;

    internal static (double FastOffset, double SlowOffset) GetPoorDisplayOffsets(BmsJudgementWindowTable windows, bool showEmptyPoor)
    {
        var fastOffset = 0d;
        var slowOffset = 0d;

        foreach (var window in windows.HitWindows)
        {
            // NB retains an inverted BAD interval for passive POOR timing, but that interval has no colour bar.
            if (window.SlowDTime > window.FastDTime)
                continue;

            fastOffset = Math.Min(fastOffset, -window.FastOffset);
            slowOffset = Math.Max(slowOffset, window.SlowOffset);
        }

        if (showEmptyPoor && windows.EPoorWindow is { } emptyPoor)
        {
            fastOffset = Math.Min(fastOffset, -emptyPoor.FastOffset);
            slowOffset = Math.Max(slowOffset, emptyPoor.SlowOffset);
        }

        return (fastOffset, slowOffset);
    }

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
        judgementsContainer.RequestUpdate();
    }

    protected override void Dispose(bool isDisposing)
    {
        if (scoreProcessor != null)
            scoreProcessor.NonConsumingJudgementRegistered -= onNonConsumingJudgementRegistered;

        base.Dispose(isDisposing);
    }

    private sealed partial class JudgementsContainer(BmsHitErrorMeter meter) : Container
    {
        // A single line can extend beyond this container's bounds. Keep that path flattened;
        // larger groups retain a node so unchanged buffered child lists can skip traversal.
        protected override bool CanBeFlattened => AliveInternalChildren.Count < 2 && !Masking && !HasProxy && !ForceLocalVertexBatch;

        private long childDrawVersion;

        private double lastUpdate = double.NegativeInfinity;
        private bool updateRequested;
        private long maskingVersion;
        private long maskedVersion = -1;
        private long maskingInvalidationId = -1;
        private RectangleF maskingBounds;
        private bool maskingUpdated;

        public void AddJudgement(JudgementLine line)
        {
            Add(line);
            RequestUpdate();
        }

        public void RequestUpdate()
        {
            updateRequested = true;
            maskingVersion++;
            InvalidateChildDrawNodes();
        }

        public void InvalidateChildDrawNodes() => childDrawVersion++;

        protected override bool OnInvalidate(Invalidation invalidation, InvalidationSource source)
        {
            InvalidateChildDrawNodes();
            return base.OnInvalidate(invalidation, source);
        }

        protected override DrawNode CreateDrawNode() => new JudgementsDrawNode(this);

        public override bool UpdateSubTree()
        {
            var now = Time.Current;
            var updateInterval = Math.Min(8, meter.JudgementFadeDuration.Value * 1000 / 60);

            // Long fades need fewer samples than high-frequency gameplay updates, while
            // short user-configured fades retain enough steps to appear smooth.
            if (!updateRequested && now >= lastUpdate && now - lastUpdate < updateInterval)
                return false;

            updateRequested = false;
            lastUpdate = now;
            var updated = base.UpdateSubTree();
            // Fades and lifetimes may change child presence without invalidating the parent.
            // Between these updates, the leaf boxes have no independent animation or input.
            maskingVersion++;
            if (updated)
                InvalidateChildDrawNodes();
            return updated;
        }

        public override bool UpdateSubTreeMasking()
        {
            var bounds = ComputeMaskingBounds();
            if (maskedVersion == maskingVersion && maskingInvalidationId == InvalidationID && maskingBounds == bounds)
                return maskingUpdated;

            if (maskingBounds != bounds)
                InvalidateChildDrawNodes();
            maskingUpdated = base.UpdateSubTreeMasking();
            maskingInvalidationId = InvalidationID;
            maskingBounds = bounds;
            maskedVersion = maskingVersion;
            return maskingUpdated;
        }

        private sealed class JudgementsDrawNode(JudgementsContainer source) : CompositeDrawableDrawNode(source)
        {
            private long childVersion = -1;

            // The framework evaluates this on the update thread while preparing this buffer slot.
            // Reuse only its own child nodes; drawing still uses the immutable standard node state.
            public override bool AddChildDrawNodes
            {
                get
                {
                    var version = ((JudgementsContainer)Source).childDrawVersion;
                    if (childVersion == version)
                        return false;

                    childVersion = version;
                    return true;
                }
            }
        }
    }

    internal partial class JudgementLinePool : CompositeDrawable
    {
        // The framework pool requires a composite wrapper. Keep leaf boxes here to avoid
        // a second drawable and its colour invalidations for every fading judgement.
        private readonly Stack<JudgementLine> available = new();
        private bool disposed;

        public int CurrentPoolSize { get; private set; }

        public int CountInUse { get; private set; }

        public int CountAvailable => available.Count;

        [BackgroundDependencyLoader]
        private void load()
        {
            for (var i = 0; i < 50; i++)
                available.Push(create());

            LoadComponents(available.ToArray());
        }

        public JudgementLine Get()
        {
            if (!available.TryPop(out var line))
            {
                line = create();
                LoadComponent(line);
            }

            CountInUse++;
            line.Activate();
            return line;
        }

        public void Return(JudgementLine line)
        {
            CountInUse--;

            if (disposed)
                line.Dispose();
            else
                available.Push(line);
        }

        private JudgementLine create()
        {
            CurrentPoolSize++;
            return new JudgementLine(this);
        }

        protected override void Dispose(bool isDisposing)
        {
            disposed = true;

            foreach (var line in available)
                line.Dispose();

            available.Clear();
            base.Dispose(isDisposing);
        }
    }

    internal partial class JudgementLine : Box
    {
        public bool? PoorIsFast { get; set; }

        private readonly JudgementLinePool pool;
        private readonly BindableNumber<float> judgementLineThickness = new BindableFloat();
        private bool inUse;
        private bool waitingForPrepare;
        private double fadeStart;
        private double fadeEnd;

        public override bool DisposeOnDeathRemoval => false;

        public override bool IsPresent => inUse && (waitingForPrepare || base.IsPresent);

        [Resolved]
        private BmsHitErrorMeter hitErrorMeter { get; set; } = null!;

        public JudgementLine(JudgementLinePool pool)
        {
            this.pool = pool;
            RelativeSizeAxes = Axes.Y;
            Height = 1;
            RelativePositionAxes = Axes.X;
            Blending = BlendingParameters.Additive;
            Anchor = Anchor.CentreLeft;
            Origin = Anchor.Centre;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            judgementLineThickness.BindTo(hitErrorMeter.JudgementLineThickness);
            judgementLineThickness.BindValueChanged(thickness => Width = thickness.NewValue, true);
        }

        public void Activate()
        {
            inUse = true;
            waitingForPrepare = true;
            LifetimeStart = double.MinValue;
            LifetimeEnd = double.MaxValue;
        }

        public override bool UpdateSubTree()
        {
            // Update before the presence check so rewinding can reveal a nearly faded line.
            // Each line has one fixed linear fade, so it needs no transform queue.
            if (!waitingForPrepare)
                Alpha = Interpolation.ValueAt(Math.Clamp(Time.Current, fadeStart, fadeEnd), 0.4f, 0, fadeStart, fadeEnd);

            return base.UpdateSubTree();
        }

        protected override void Update()
        {
            if (waitingForPrepare)
            {
                waitingForPrepare = false;
                fadeStart = Time.Current;
                fadeEnd = fadeStart + hitErrorMeter.JudgementFadeDuration.Value * 1000;
                Alpha = 0.4f;
                // Match Expire()'s extra millisecond after the last transform finishes.
                LifetimeEnd = fadeEnd + 1;
            }

            base.Update();
        }

        protected override bool OnInvalidate(Invalidation invalidation, InvalidationSource source)
        {
            // Child colour invalidation does not reach the parent, but changes its buffered leaf state.
            (Parent as JudgementsContainer)?.InvalidateChildDrawNodes();
            if (source != InvalidationSource.Child && (invalidation & Invalidation.Parent) != 0 && inUse && Parent == null)
            {
                inUse = false;
                waitingForPrepare = false;
                pool.Return(this);
            }

            return base.OnInvalidate(invalidation, source);
        }
    }
}

internal readonly record struct BmsHitErrorTimingObservation(double TimeOffset, HitResult Result);

internal readonly record struct BmsHitErrorMeterDomain(double FastOffset, double SlowOffset)
{
    public float RelativePosition(double timeOffset)
        => Math.Clamp((float)((timeOffset - FastOffset) / (SlowOffset - FastOffset)), 0, 1);
}
