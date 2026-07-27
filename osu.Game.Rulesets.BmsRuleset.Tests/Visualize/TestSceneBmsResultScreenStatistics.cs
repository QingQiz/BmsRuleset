using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Models;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Result;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens;
using osu.Game.Screens.Ranking;
using osu.Game.Screens.Ranking.Statistics;
using osu.Game.Tests.Visual;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsResultScreenStatistics : OsuManualInputManagerTestScene
{
    [Test]
    public void TestScoreCardUsesBmsRulesetIcon()
    {
        TestBmsSoloResultsScreen screen = null!;

        AddStep("load results screen", () =>
        {
            var stack = new OsuScreenStack
            {
                RelativeSizeAxes = Axes.Both,
            };

            Child = stack;
            stack.Push(screen = new TestBmsSoloResultsScreen(createScore()));
        });

        AddUntilStep("results screen loaded", () => screen.IsLoaded);
        AddUntilStep("score card uses BMS ruleset icon", () =>
            this.ChildrenOfType<DifficultyIcon>().Any(icon => icon.ChildrenOfType<BmsRulesetIcon>().Any()));
    }

    [Test]
    public void TestAutoGaugeHistory()
    {
        TestBmsSoloResultsScreen screen = null!;

        AddStep("load results screen", () =>
        {
            var score = createScore();

            OsuScreenStack stack;

            Child = stack = new OsuScreenStack
            {
                RelativeSizeAxes = Axes.Both,
            };

            stack.Push(screen = new TestBmsSoloResultsScreen(score));
        });

        AddUntilStep("results screen loaded", () => screen.IsLoaded);

        AddStep("open statistics", () =>
        {
            var expandedPanel = this.ChildrenOfType<ScorePanel>().Single(p => p.State == PanelState.Expanded);

            InputManager.MoveMouseTo(expandedPanel);
            InputManager.Click(MouseButton.Left);
        });

        AddUntilStep("statistics shown", () => this.ChildrenOfType<StatisticsPanel>().Single().State.Value == Visibility.Visible);

        assertText("BMS Result Showcase");
        assertText("visual-test artist");
        assertText("[7K Hyper]");
        assertText("mapper");
        assertText("Gauge History");
        assertText("Hazard");
        assertText("ExHard");
        assertText("Hard");
        assertText("Normal");
        assertText("Easy");
        assertText("Assist Easy");
        assertText("Timeline");
        assertText("Hit Offset");
        assertNoHitOffsetText("Scratch");
        assertNoHitOffsetText("Key 1");
        assertHitOffsetText("-150");
        assertHitOffsetText("0");
        assertHitOffsetText("+150");
        assertText("Hit Scatter");
        assertText("E-POOR");
        assertHitScatterText("+150 ms");
    }

    [Test]
    public void TestGaugeFailureMarkerCentresAlignWithFailurePoints()
    {
        BmsGaugeHistoryGraph normalGraph = null!;
        BmsGaugeHistoryGraph hardGraph = null!;
        BmsBeatmap normalBeatmap = null!;
        BmsBeatmap hardBeatmap = null!;
        ScoreInfo normalScore = null!;
        ScoreInfo hardScore = null!;

        AddStep("load gauge failure graphs", () =>
        {
            normalScore = new ScoreInfo
            {
                HitEvents = [new HitEvent(0, 1, HitResult.Good, new BmsNote { StartTime = 1000 }, null, null)],
            };

            normalBeatmap = new BmsBeatmap
            {
                LayoutVariant = BmsLayoutVariant.Bme7K,
                TotalColumns = BmsLayout.GetTotalColumns(BmsLayoutVariant.Bme7K),
                Total = 200,
                HitObjects =
                {
                    new BmsNote { StartTime = 1000 },
                    new BmsNote { StartTime = 2000 },
                },
            };

            var hardEvents = Enumerable.Range(0, 20)
                                       .Select(i => new HitEvent(0, 1, HitResult.Meh, new BmsNote { StartTime = 1000 + i * 100 }, null, null))
                                       .ToList();

            hardScore = new ScoreInfo
            {
                Mods = [new BmsModHardGauge()],
                HitEvents = hardEvents,
            };

            hardBeatmap = new BmsBeatmap
            {
                LayoutVariant = BmsLayoutVariant.Bme7K,
                TotalColumns = BmsLayout.GetTotalColumns(BmsLayoutVariant.Bme7K),
                Total = 200,
            };

            foreach (var hitEvent in hardEvents)
                hardBeatmap.HitObjects.Add(new BmsNote { StartTime = hitEvent.HitObject.StartTime });

            Child = new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 720,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new osuTK.Vector2(0, 24),
                Children =
                [
                    normalGraph = new BmsGaugeHistoryGraph(normalScore, normalBeatmap),
                    hardGraph = new BmsGaugeHistoryGraph(hardScore, hardBeatmap),
                ],
            };
        });

        AddUntilStep("gauge graphs loaded", () => normalGraph.IsLoaded && normalGraph.DrawHeight > 0 && hardGraph.IsLoaded && hardGraph.DrawHeight > 0);
        AddUntilStep("gauge plots allow overflow", () => !gaugePlotFor(normalGraph).Masking && !gaugePlotFor(hardGraph).Masking);
        AddUntilStep("hard line has room below bottom", () => gaugePathExtendsBelowBottomEdge(hardGraph));
        AddUntilStep("hard line stops at failure point", () => gaugePathEndsAtFailurePoint(hardGraph, hardScore, hardBeatmap));
        AddUntilStep("normal failure marker centre aligns", () => failureMarkerCentreAlignsWithFailurePoint(normalGraph, normalScore, normalBeatmap));
        AddUntilStep("hard failure marker centre aligns", () => failureMarkerCentreAlignsWithFailurePoint(hardGraph, hardScore, hardBeatmap));
    }

    [Test]
    public void TestGaugePathUpdatesWhenParentWidthChanges()
    {
        Container pathContainer = null!;
        BmsGaugeHistoryGraph.GaugePath path = null!;

        AddStep("load gauge path", () =>
        {
            Child = pathContainer = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 720,
                Height = 180,
                Child = path = new BmsGaugeHistoryGraph.GaugePath(
                [
                    new BmsGaugeHistoryGraph.GaugePoint(0, 0.5f),
                    new BmsGaugeHistoryGraph.GaugePoint(1, 0.5f),
                ], 0)
                {
                    PathRadius = 1.2f,
                },
            };
        });

        AddUntilStep("path loaded", () => path.IsLoaded && path.Vertices.Count > 0);
        AddUntilStep("initial path matches parent width", () => Math.Abs(path.Vertices[^1].X - (pathContainer.DrawWidth + path.PathRadius)) < 0.5f);

        AddStep("shrink parent", () => pathContainer.Width = 360);

        AddUntilStep("path follows shrunken parent width", () => Math.Abs(path.Vertices[^1].X - (pathContainer.DrawWidth + path.PathRadius)) < 0.5f);
    }

    [Test]
    public void TestGaugeFailureMarkerRepositionsWhenParentWidthChanges()
    {
        Container markerContainer = null!;
        BmsGaugeHistoryGraph.GaugeFailureMarker marker = null!;

        AddStep("load failure marker", () =>
        {
            var failurePoint = new BmsGaugeHistoryGraph.GaugePoint(0.5f, 0f);

            Child = markerContainer = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 720,
                Height = 180,
                Child = marker = new BmsGaugeHistoryGraph.GaugeFailureMarker(
                    [new BmsGaugeHistoryGraph.GaugePoint(0, 0.5f), failurePoint],
                    failurePoint,
                    1.2f),
            };
        });

        AddUntilStep("marker loaded", () => marker.IsLoaded && marker.Position.X > 0);
        AddUntilStep("initial marker position matches parent width", () =>
            Math.Abs(marker.Position.X - 0.5f * markerContainer.DrawWidth) < 0.5f);

        AddStep("shrink parent", () => markerContainer.Width = 360);

        AddUntilStep("marker follows shrunken parent width", () =>
            Math.Abs(marker.Position.X - 0.5f * markerContainer.DrawWidth) < 0.5f);
    }

    [Test]
    public void TestHitOffsetStatisticTogglesKeyChartsOnClick()
    {
        BmsHitOffsetStatistic hitOffsetStatistic = null!;

        AddStep("load hit offset statistic", () =>
        {
            Child = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 720,
                AutoSizeAxes = Axes.Y,
                Child = hitOffsetStatistic = new BmsHitOffsetStatistic(createGaugeHitEvents(), new BmsBeatmap
                {
                    LayoutVariant = BmsLayoutVariant.Bms5K,
                    TotalColumns = BmsLayout.GetTotalColumns(BmsLayoutVariant.Bms5K),
                }),
            };
        });

        AddUntilStep("hit offset statistic loaded", () => hitOffsetStatistic.IsLoaded && hitOffsetStatistic.DrawHeight > 0);
        assertNoHitOffsetText("Scratch");
        assertNoHitOffsetText("Key 1");

        AddStep("expand from component body", () =>
        {
            InputManager.MoveMouseTo(hitOffsetStatistic);
            InputManager.Click(MouseButton.Left);
        });

        assertHitOffsetText("Scratch");
        assertHitOffsetText("Key 1");

        AddStep("collapse from component body", () =>
        {
            InputManager.MoveMouseTo(hitOffsetStatistic);
            InputManager.Click(MouseButton.Left);
        });

        assertNoHitOffsetText("Scratch");
        assertNoHitOffsetText("Key 1");
    }

    [Test]
    public void TestHitScatterStatisticTogglesKeyChartsOnClick()
    {
        BmsHitScatterStatistic hitScatterStatistic = null!;

        AddStep("load hit scatter statistic", () =>
        {
            Child = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 720,
                AutoSizeAxes = Axes.Y,
                Child = hitScatterStatistic = new BmsHitScatterStatistic(createGaugeHitEvents(), new BmsBeatmap
                {
                    LayoutVariant = BmsLayoutVariant.Bms5K,
                    TotalColumns = BmsLayout.GetTotalColumns(BmsLayoutVariant.Bms5K),
                }),
            };
        });

        AddUntilStep("hit scatter statistic loaded", () => hitScatterStatistic.IsLoaded && hitScatterStatistic.DrawHeight > 0);
        assertNoHitScatterText("Scratch");
        assertNoHitScatterText("Key 1");

        AddStep("expand from component body", () =>
        {
            InputManager.MoveMouseTo(hitScatterStatistic);
            InputManager.Click(MouseButton.Left);
        });

        assertHitScatterText("Scratch");
        assertHitScatterText("Key 1");

        AddStep("collapse from component body", () =>
        {
            InputManager.MoveMouseTo(hitScatterStatistic);
            InputManager.Click(MouseButton.Left);
        });

        assertNoHitScatterText("Scratch");
        assertNoHitScatterText("Key 1");
    }

    [Test]
    public void TestHitOffsetStatisticUsesManiaTimingDistributionStyle()
    {
        BmsHitOffsetStatistic hitOffsetStatistic = null!;

        AddStep("load hit offset statistic", () =>
        {
            var beatmap = new BmsBeatmap
            {
                LayoutVariant = BmsLayoutVariant.Bms5K,
                TotalColumns = BmsLayout.GetTotalColumns(BmsLayoutVariant.Bms5K),
            };

            Child = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 720,
                AutoSizeAxes = Axes.Y,
                Child = hitOffsetStatistic = new BmsHitOffsetStatistic(createGaugeHitEvents(), beatmap),
            };
        });

        AddUntilStep("hit offset statistic loaded", () => hitOffsetStatistic.IsLoaded);
        AddUntilStep("rounded timing bars shown", () => hitOffsetStatistic.ChildrenOfType<Circle>().Count() >= 101);
        AddUntilStep("old plot background removed", () => !hitOffsetStatistic.ChildrenOfType<Box>().Any(b => Math.Abs(b.Alpha - 0.18f) < 0.001f));
        AddUntilStep("standard deviation shown", () => hitOffsetStatistic.ChildrenOfType<SpriteText>().Any(t => t.Text.ToString().StartsWith("SD ")));
        assertHitOffsetText("-150");
        assertHitOffsetText("0");
        assertHitOffsetText("+150");
    }

    [Test]
    public void TestHitScatterAxisAndDirectionLabelsAvoidPlotContent()
    {
        BmsHitScatterStatistic hitScatterStatistic = null!;

        AddStep("load hit scatter statistic", () =>
        {
            Child = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 720,
                AutoSizeAxes = Axes.Y,
                Child = hitScatterStatistic = new BmsHitScatterStatistic(createGaugeHitEvents(), new BmsBeatmap
                {
                    LayoutVariant = BmsLayoutVariant.Bms5K,
                    TotalColumns = BmsLayout.GetTotalColumns(BmsLayoutVariant.Bms5K),
                }),
            };
        });

        AddUntilStep("hit scatter statistic loaded", () => hitScatterStatistic.IsLoaded && hitScatterStatistic.DrawHeight > 0);
        AddUntilStep("axis labels stay outside plot", () =>
        {
            var plot = plotBackgroundFor(hitScatterStatistic).ScreenSpaceDrawQuad.AABBFloat;

            return hitScatterStatistic.ChildrenOfType<SpriteText>()
                                      .Where(t => t.Text.ToString() is "-150 ms" or "-75 ms" or "0 ms" or "+75 ms" or "+150 ms")
                                      .All(t => t.ScreenSpaceDrawQuad.AABBFloat.Right < plot.Left);
        });

        AddUntilStep("scatter points stay inside plot", () =>
        {
            var plot = plotBackgroundFor(hitScatterStatistic).ScreenSpaceDrawQuad.AABBFloat;
            var points = hitScatterStatistic.ChildrenOfType<Circle>()
                                            .Where(c => Math.Abs(c.DrawWidth - 4.4f) < 0.01f || Math.Abs(c.DrawWidth - 5.2f) < 0.01f)
                                            .Select(c => c.ScreenSpaceDrawQuad.AABBFloat)
                                            .ToArray();

            return points.Length > 0
                   && points.All(point => point.Left >= plot.Left
                                          && point.Right <= plot.Right
                                          && point.Top >= plot.Top
                                          && point.Bottom <= plot.Bottom);
        });

        AddUntilStep("direction labels stay at plot edges", () =>
        {
            var plot = plotBackgroundFor(hitScatterStatistic).ScreenSpaceDrawQuad.AABBFloat;
            var slow = hitScatterStatistic.ChildrenOfType<SpriteText>().Single(t => t.Text.ToString() == "slow").ScreenSpaceDrawQuad.AABBFloat;
            var fast = hitScatterStatistic.ChildrenOfType<SpriteText>().Single(t => t.Text.ToString() == "fast").ScreenSpaceDrawQuad.AABBFloat;

            return fast.Top >= plot.Top
                   && fast.Bottom <= plot.Top + 28
                   && slow.Bottom <= plot.Bottom
                   && slow.Top >= plot.Bottom - 28;
        });

        AddUntilStep("axis marks touch plot", () =>
        {
            var plot = plotBackgroundFor(hitScatterStatistic).ScreenSpaceDrawQuad.AABBFloat;

            return hitScatterStatistic.ChildrenOfType<Box>()
                                      .Where(b => Math.Abs(b.Alpha - 0.45f) < 0.001f || Math.Abs(b.Alpha - 0.25f) < 0.001f)
                                      .All(b => Math.Abs(b.ScreenSpaceDrawQuad.AABBFloat.Right - plot.Left) < 0.5f);
        });
    }

    private ScoreInfo createScore()
    {
        var beatmap = Beatmap.Value.BeatmapInfo;
        var ruleset = new BmsRuleset().RulesetInfo;

        beatmap.Ruleset = ruleset;
        beatmap.DifficultyName = "[7K Hyper]";
        beatmap.StarRating = 7.42;
        beatmap.Hash = "bms-result-showcase";
        var metadata = new BeatmapMetadata(new RealmUser { Username = "mapper" })
        {
            Title = "BMS Result Showcase",
            TitleUnicode = "BMS Result Showcase",
            Artist = "visual-test artist",
            ArtistUnicode = "visual-test artist",
        };

        beatmap.Metadata = metadata;

        beatmap.BeatmapSet = null;

        var score = new ScoreInfo
        {
            User = new APIUser
            {
                Id = 2,
                Username = "bms-test",
            },
            BeatmapInfo = beatmap,
            BeatmapHash = beatmap.Hash,
            Ruleset = ruleset,
            Rank = ScoreRank.A,
            TotalScore = 813197,
            TotalScoreWithoutMods = 813197,
            Accuracy = 0.813197,
            Mods =
            [
                new BmsModAutoGauge(),
                new BmsModHardGauge(),
            ],
            HitEvents = createGaugeHitEvents(),
            Statistics = new Dictionary<HitResult, int>
            {
                [HitResult.Perfect] = 621,
                [HitResult.Great] = 300,
                [HitResult.Good] = 41,
                [HitResult.Ok] = 12,
                [HitResult.Meh] = 8,
                [HitResult.Miss] = 3,
            },
            MaximumStatistics =
            {
                [HitResult.Perfect] = 985,
            },
        };

        return score;
    }

    private List<HitEvent> createGaugeHitEvents()
    {
        // 1000 hit events with normally-distributed offsets (mean 0, std 50ms) — a natural bell
        // that fills the hit-offset histogram. Result severity tracks offset magnitude (Perfect
        // near the centre, Meh in the tails); Gauge History is unaffected because it ignores
        // TimeOffset. The RNG is seeded so the visual is reproducible across runs.
        var rng = new Random(42991);
        var offsets = new double[1000];

        for (var i = 0; i < 1000; i++)
            offsets[i] = nextGaussian(rng, 0, 50);

        offsets[0] = 200;

        return offsets.Select((offset, i) => new HitEvent(offset, 1, resultFor(offset), new BmsNote
        {
            Column = i % 2,
            StartTime = i * 100,
        }, null, null)).ToList();

        static double nextGaussian(Random rng, double mean, double std)
        {
            double u1;
            do u1 = rng.NextDouble();
            while (u1 == 0);

            var u2 = rng.NextDouble();
            return mean + std * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }

        static HitResult resultFor(double offset)
        {
            var magnitude = Math.Abs(offset);

            if (magnitude < 15) return HitResult.Perfect;
            if (magnitude < 35) return HitResult.Great;
            if (magnitude < 70) return HitResult.Good;
            if (magnitude > 150) return HitResult.Miss;
            if (magnitude < 115) return HitResult.Ok;

            return HitResult.Meh;
        }
    }

    private void assertText(string text) =>
        AddUntilStep($"{text} shown", () => this.ChildrenOfType<SpriteText>().Any(t => t.Text.ToString() == text));

    private void assertHitOffsetText(string text) =>
        AddUntilStep($"hit offset {text} shown", () => hitOffsetStatisticContainsText(text));

    private void assertNoHitOffsetText(string text) =>
        AddUntilStep($"hit offset {text} hidden", () => !hitOffsetStatisticContainsText(text));

    private bool hitOffsetStatisticContainsText(string text) =>
        this.ChildrenOfType<BmsHitOffsetStatistic>().Single().ChildrenOfType<SpriteText>().Any(t => t.Text.ToString() == text);

    private void assertHitScatterText(string text) =>
        AddUntilStep($"hit scatter {text} shown", () => hitScatterStatisticContainsText(text));

    private void assertNoHitScatterText(string text) =>
        AddUntilStep($"hit scatter {text} hidden", () => !hitScatterStatisticContainsText(text));

    private bool hitScatterStatisticContainsText(string text) =>
        this.ChildrenOfType<BmsHitScatterStatistic>().Single().ChildrenOfType<SpriteText>().Any(t => t.Text.ToString() == text);

    private static Box plotBackgroundFor(Drawable statistic) =>
        statistic.ChildrenOfType<Box>()
                 .Where(b => Math.Abs(b.Alpha - 0.18f) < 0.001f && b.DrawWidth > 100 && b.DrawHeight > 80)
                 .OrderByDescending(b => b.DrawWidth * b.DrawHeight)
                 .First();

    private static Box gaugePlotBackgroundFor(Drawable statistic) =>
        statistic.ChildrenOfType<Box>()
                 .Where(b => Math.Abs(b.Alpha - 0.22f) < 0.001f && b.DrawWidth > 100 && b.DrawHeight > 80)
                 .OrderByDescending(b => b.DrawWidth * b.DrawHeight)
                 .First();

    private static Container gaugePlotFor(Drawable statistic) =>
        (Container)gaugePlotBackgroundFor(statistic).Parent!;

    private static BmsGaugeHistoryGraph.GaugePath gaugePathFor(Drawable statistic) =>
        statistic.ChildrenOfType<BmsGaugeHistoryGraph.GaugePath>().Single(p => p.Name?.Contains("gauge history") == true);

    private static Container failureMarkerFor(Drawable statistic) =>
        statistic.ChildrenOfType<Container>()
                 .Single(c => Math.Abs(c.DrawWidth - BmsGaugeHistoryGraph.FAILURE_MARKER_SIZE) < 0.5f
                              && Math.Abs(c.DrawHeight - BmsGaugeHistoryGraph.FAILURE_MARKER_SIZE) < 0.5f
                              && c.ChildrenOfType<Box>().Count(b => Math.Abs(Math.Abs(b.Rotation) - 45) < 0.001f) == 4);

    private static bool gaugePathExtendsBelowBottomEdge(Drawable statistic)
    {
        var plot = gaugePlotBackgroundFor(statistic).ScreenSpaceDrawQuad.AABBFloat;
        var path = gaugePathFor(statistic);
        var pathBounds = path.ScreenSpaceDrawQuad.AABBFloat;

        return pathBounds.Bottom >= plot.Bottom + path.PathRadius - 0.5f;
    }

    private static bool gaugePathEndsAtFailurePoint(BmsGaugeHistoryGraph graph, ScoreInfo score, BmsBeatmap beatmap)
    {
        var failurePoint = BmsGaugeHistoryGraph.CreateSeries(score, beatmap).Single().FailurePoint;
        if (failurePoint == null)
            return false;

        var path = gaugePathFor(graph);
        var pathEnd = path.ToScreenSpace(path.Vertices[^1]);
        var plot = gaugePlotBackgroundFor(graph).ScreenSpaceDrawQuad.AABBFloat;
        var expectedX = plot.Left + failurePoint.Value.Time * plot.Width;
        var expectedY = plot.Top + (1 - failurePoint.Value.Health) * plot.Height;

        return Math.Abs(pathEnd.X - expectedX) < 0.5f
               && Math.Abs(pathEnd.Y - expectedY) < 0.5f;
    }

    private static bool failureMarkerCentreAlignsWithFailurePoint(BmsGaugeHistoryGraph graph, ScoreInfo score, BmsBeatmap beatmap)
    {
        var failurePoint = BmsGaugeHistoryGraph.CreateSeries(score, beatmap).Single().FailurePoint;
        if (failurePoint == null)
            return false;

        var plot = gaugePlotBackgroundFor(graph).ScreenSpaceDrawQuad.AABBFloat;
        var marker = failureMarkerFor(graph).ScreenSpaceDrawQuad.AABBFloat;
        var markerCentreX = (marker.Left + marker.Right) / 2;
        var markerCentreY = (marker.Top + marker.Bottom) / 2;
        var expectedX = plot.Left + failurePoint.Value.Time * plot.Width;
        var expectedY = plot.Top + (1 - failurePoint.Value.Health) * plot.Height;

        return Math.Abs(markerCentreX - expectedX) < 0.5f
               && Math.Abs(markerCentreY - expectedY) < 0.5f;
    }

    private partial class TestBmsSoloResultsScreen : SoloResultsScreen
    {
        public TestBmsSoloResultsScreen(ScoreInfo score)
            : base(score)
        {
            AllowWatchingReplay = false;
        }

        protected override Task<ScoreInfo[]> FetchScores() => Task.FromResult<ScoreInfo[]>([]);
    }
}
