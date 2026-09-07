// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.
// Adapted from osu.Game.Screens.Ranking.Expanded.Accuracy.AccuracyCircle.
// Based on osu! revision 3c1c96f742e7aae2ff67a7361e058fe91ca3b955.
// BMS owns these components so rank lettering does not depend on native UI patches.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Utils;
using osu.Game.Audio;
using osu.Game.Graphics;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Expanded.Accuracy;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal partial class BmsAccuracyCircle : CompositeDrawable
{
    public const double TOTAL_DURATION = APPEAR_DURATION + ACCURACY_TRANSFORM_DELAY + ACCURACY_TRANSFORM_DURATION;

    public const double APPEAR_DURATION = 200;

    public const double ACCURACY_TRANSFORM_DELAY = 450;

    public const double ACCURACY_TRANSFORM_DURATION = 3000;

    public const double TEXT_APPEAR_DELAY = ACCURACY_TRANSFORM_DURATION / 2;

    public const double RANK_CIRCLE_TRANSFORM_DELAY = 150;

    public const double RANK_CIRCLE_TRANSFORM_DURATION = 800;

    private const float accuracy_circle_radius = 0.2f;

    public const double VIRTUAL_SS_PERCENTAGE = 0.01;

    public const double GRADE_SPACING_PERCENTAGE = 2.0 / 360;

    public static readonly Easing ACCURACY_TRANSFORM_EASING = Easing.OutPow10;

    private readonly ScoreInfo score;

    [Resolved]
    private BmsResultsScreen? resultsScreen { get; set; }

    private CircularProgress accuracyCircle = null!;
    private GradedCircles gradedCircles = null!;
    private Container<BmsRankBadge> badges = null!;
    private BmsRankText rankText = null!;

    private PoolableSkinnableSample? scoreTickSound;
    private PoolableSkinnableSample? badgeTickSound;
    private PoolableSkinnableSample? badgeMaxSound;
    private PoolableSkinnableSample? swooshUpSound;
    private PoolableSkinnableSample? rankImpactSound;

    private readonly Bindable<double> tickPlaybackRate = new();

    private double lastTickPlaybackTime;
    private bool isTicking;

    private readonly double accuracyX;
    private readonly double accuracyS;
    private readonly double accuracyA;
    private readonly double accuracyB;
    private readonly double accuracyC;
    private readonly double accuracyD;
    private bool withFlair;

    internal BmsAccuracyCircle(ScoreInfo score)
    {
        this.score = score;

        accuracyX = BmsExScore.AccuracyCutoffFromRank(ScoreRank.X);
        accuracyS = BmsExScore.AccuracyCutoffFromRank(ScoreRank.S);
        accuracyA = BmsExScore.AccuracyCutoffFromRank(ScoreRank.A);
        accuracyB = BmsExScore.AccuracyCutoffFromRank(ScoreRank.B);
        accuracyC = BmsExScore.AccuracyCutoffFromRank(ScoreRank.C);
        accuracyD = BmsExScore.AccuracyCutoffFromRank(ScoreRank.D);
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        withFlair = resultsScreen?.ShouldPlayFlair == true;
        InternalChildren =
        [
            new CircularProgress
            {
                Name = "Background circle",
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Colour = OsuColour.Gray(47),
                Alpha = 0.5f,
                InnerRadius = accuracy_circle_radius + 0.01f, // Extends a little bit into the circle
                Progress = 1,
            },
            accuracyCircle = new CircularProgress
            {
                Name = "Accuracy circle",
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Colour = ColourInfo.GradientVertical(Color4Extensions.FromHex("#7CF6FF"), Color4Extensions.FromHex("#BAFFA9")),
                InnerRadius = accuracy_circle_radius,
            },
            new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(0.8f),
                Padding = new MarginPadding(2.5f),
                Child = gradedCircles = new GradedCircles(accuracyC, accuracyB, accuracyA, accuracyS, accuracyX)
                {
                    RelativeSizeAxes = Axes.Both
                }
            },
            badges = new Container<BmsRankBadge>
            {
                Name = "Rank badges",
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Vertical = -15, Horizontal = -20 },
                Children =
                [
                    new BmsRankBadge(accuracyD, Interpolation.Lerp(accuracyD, accuracyC, 0.5), getRank(ScoreRank.D)),
                    new BmsRankBadge(accuracyC, Interpolation.Lerp(accuracyC, accuracyB, 0.5), getRank(ScoreRank.C)),
                    new BmsRankBadge(accuracyB, Interpolation.Lerp(accuracyB, accuracyA, 0.5), getRank(ScoreRank.B)),
                    // The S and A badges are moved down slightly to prevent collision with the SS badge.
                    new BmsRankBadge(accuracyA, Interpolation.Lerp(accuracyA, accuracyS, 0.25), getRank(ScoreRank.A)),
                    new BmsRankBadge(accuracyS, Interpolation.Lerp(accuracyS, accuracyX - VIRTUAL_SS_PERCENTAGE, 0.25), getRank(ScoreRank.S)),
                    new BmsRankBadge(accuracyX, accuracyX, getRank(ScoreRank.X)),
                ],
            },
            rankText = new BmsRankText(score.Rank)
        ];

        if (withFlair)
        {
            AddRangeInternal(
            [
                rankImpactSound = new PoolableSkinnableSample(new SampleInfo(impactSampleName)),
                scoreTickSound = new PoolableSkinnableSample(new SampleInfo(@"Results/score-tick")),
                badgeTickSound = new PoolableSkinnableSample(new SampleInfo(@"Results/badge-dink")),
                badgeMaxSound = new PoolableSkinnableSample(new SampleInfo(@"Results/badge-dink-max")),
                swooshUpSound = new PoolableSkinnableSample(new SampleInfo(@"Results/swoosh-up")),
            ]);
        }
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        this.ScaleTo(0).Then().ScaleTo(1, APPEAR_DURATION, Easing.OutQuint);

        if (withFlair)
        {
            const double swoosh_pre_delay = 443f;
            const double swoosh_volume = 0.4f;

            this.Delay(swoosh_pre_delay).Schedule(() =>
            {
                swooshUpSound!.VolumeTo(swoosh_volume);
                swooshUpSound!.Play();
            });
        }

        using (BeginDelayedSequence(RANK_CIRCLE_TRANSFORM_DELAY))
            gradedCircles.TransformTo(nameof(GradedCircles.Progress), 1.0, RANK_CIRCLE_TRANSFORM_DURATION, ACCURACY_TRANSFORM_EASING);

        using (BeginDelayedSequence(ACCURACY_TRANSFORM_DELAY))
        {
            var targetAccuracy = score.Accuracy;
            double[] notchPercentages =
            [
                accuracyS,
                accuracyA,
                accuracyB,
                accuracyC,
            ];

            // Ensure the gauge overshoots or undershoots a bit so it doesn't land in the gaps of the inner graded circle (caused by `RankNotch`es),
            // to prevent ambiguity on what grade it's pointing at.
            foreach (var p in notchPercentages)
            {
                if (Precision.AlmostEquals(p, targetAccuracy, GRADE_SPACING_PERCENTAGE / 2))
                {
                    var tippingDirection = targetAccuracy - p >= 0 ? 1 : -1; // We "round up" here to match rank criteria
                    targetAccuracy = p + tippingDirection * (GRADE_SPACING_PERCENTAGE / 2);
                    break;
                }
            }

            // The final gap between 99.999...% (S) and 100% (SS) is exaggerated by `virtual_ss_percentage`. We don't want to land there either.
            if (score.Rank == ScoreRank.X || score.Rank == ScoreRank.XH)
                targetAccuracy = 1;
            else
                targetAccuracy = Math.Min(accuracyX - VIRTUAL_SS_PERCENTAGE - GRADE_SPACING_PERCENTAGE / 2, targetAccuracy);

            // The accuracy circle gauge visually fills up a bit too much.
            // This wouldn't normally matter but we want it to align properly with the inner graded circle in the above cases.
            const double visual_alignment_offset = 0.001;

            if (targetAccuracy < 1 && targetAccuracy >= visual_alignment_offset)
                targetAccuracy -= visual_alignment_offset;

            accuracyCircle.ProgressTo(targetAccuracy, ACCURACY_TRANSFORM_DURATION, ACCURACY_TRANSFORM_EASING);

            if (withFlair)
            {
                Schedule(() =>
                {
                    const double score_tick_debounce_rate_start = 18f;
                    const double score_tick_debounce_rate_end = 300f;
                    const double score_tick_volume_start = 0.6f;
                    const double score_tick_volume_end = 1.0f;

                    this.TransformBindableTo(tickPlaybackRate, score_tick_debounce_rate_start);
                    this.TransformBindableTo(tickPlaybackRate, score_tick_debounce_rate_end, ACCURACY_TRANSFORM_DURATION, Easing.OutSine);

                    scoreTickSound!.FrequencyTo(1 + targetAccuracy, ACCURACY_TRANSFORM_DURATION, Easing.OutSine);
                    scoreTickSound!.VolumeTo(score_tick_volume_start).Then().VolumeTo(score_tick_volume_end, ACCURACY_TRANSFORM_DURATION, Easing.OutSine);

                    isTicking = true;
                });
            }

            var badgeNum = 0;

            if (score.Rank != ScoreRank.F && targetAccuracy > 0)
            {
                foreach (var badge in badges)
                {
                    if (badge.Rank > score.Rank)
                        continue;

                    using (BeginDelayedSequence(
                               inverseEasing(ACCURACY_TRANSFORM_EASING, Math.Min(accuracyX - VIRTUAL_SS_PERCENTAGE, badge.Accuracy) / targetAccuracy) * ACCURACY_TRANSFORM_DURATION))
                    {
                        badge.Appear();

                        if (withFlair)
                        {
                            Schedule(() =>
                            {
                                var dink = badgeNum < badges.Count - 1 ? badgeTickSound : badgeMaxSound;

                                dink!.FrequencyTo(1 + badgeNum++ * 0.05);
                                dink!.Play();
                            });
                        }
                    }
                }
            }

            using (BeginDelayedSequence(TEXT_APPEAR_DELAY))
            {
                rankText.Appear();

                if (withFlair)
                {
                    Schedule(() =>
                    {
                        isTicking = false;
                        rankImpactSound!.Play();
                    });

                    const double applause_pre_delay = 545f;

                    using (BeginDelayedSequence(applause_pre_delay))
                        Schedule(() => resultsScreen?.PlayApplause(score.Rank));
                }
            }
        }
    }

    protected override void Update()
    {
        base.Update();

        if (isTicking && Clock.CurrentTime - lastTickPlaybackTime >= tickPlaybackRate.Value)
        {
            scoreTickSound?.Play();
            lastTickPlaybackTime = Clock.CurrentTime;
        }
    }

    private string impactSampleName
    {
        get
        {
            switch (score.Rank)
            {
                default:
                case ScoreRank.D:
                    return @"Results/rank-impact-fail-d";

                case ScoreRank.C:
                case ScoreRank.B:
                    return @"Results/rank-impact-fail";

                case ScoreRank.A:
                case ScoreRank.S:
                case ScoreRank.SH:
                    return @"Results/rank-impact-pass";

                case ScoreRank.X:
                case ScoreRank.XH:
                    return @"Results/rank-impact-pass-ss";
            }
        }
    }

    private ScoreRank getRank(ScoreRank rank)
    {
        foreach (var mod in score.Mods.OfType<IApplicableToScoreProcessor>())
            rank = mod.AdjustRank(rank, score.Accuracy);

        return rank;
    }

    private static double inverseEasing(Easing easing, double targetValue)
    {
        // Imported scores may retain a rank whose threshold exceeds the available accuracy.
        // Keep the inverse search finite even when those values disagree.
        targetValue = Math.Clamp(targetValue, 0, 1);
        var lower = 0d;
        var upper = 1d;
        var test = 0d;
        for (var iteration = 0; iteration < 32; iteration++)
        {
            test = (lower + upper) / 2;
            var result = Interpolation.ApplyEasing(easing, test);
            if (Math.Abs(result - targetValue) <= 0.005)
                break;

            if (result < targetValue)
                lower = test;
            else
                upper = test;
        }

        return test;
    }
}
