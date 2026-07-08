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
using osu.Game.Models;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Result;
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
        assertHitOffsetText("-150 ms");
        assertHitOffsetText("0 ms");
        assertHitOffsetText("+150 ms");
        assertText("Hit Scatter");
        assertText("E-POOR");
        assertText("+200 ms");
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
    public void TestHitScatterPlotAlignsWithHitOffsetPlot()
    {
        BmsHitOffsetStatistic hitOffsetStatistic = null!;
        BmsHitScatterStatistic hitScatterStatistic = null!;

        AddStep("load hit statistics", () =>
        {
            var beatmap = new BmsBeatmap
            {
                LayoutVariant = BmsLayoutVariant.Bms5K,
                TotalColumns = BmsLayout.GetTotalColumns(BmsLayoutVariant.Bms5K),
            };

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
                    hitScatterStatistic = new BmsHitScatterStatistic(createGaugeHitEvents(), beatmap),
                    hitOffsetStatistic = new BmsHitOffsetStatistic(createGaugeHitEvents(), beatmap),
                ],
            };
        });

        AddUntilStep("hit statistics loaded", () => hitOffsetStatistic.IsLoaded && hitScatterStatistic.IsLoaded);
        AddUntilStep("plot backgrounds align", () =>
        {
            var scatterPlot = plotBackgroundFor(hitScatterStatistic);
            var offsetPlot = plotBackgroundFor(hitOffsetStatistic);

            return Math.Abs(scatterPlot.ScreenSpaceDrawQuad.TopLeft.X - offsetPlot.ScreenSpaceDrawQuad.TopLeft.X) < 0.5f
                   && Math.Abs(scatterPlot.DrawWidth - offsetPlot.DrawWidth) < 0.5f;
        });
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
                                      .Where(t => t.Text.ToString() is "-200 ms" or "-100 ms" or "0 ms" or "+100 ms" or "+200 ms")
                                      .All(t => t.ScreenSpaceDrawQuad.AABBFloat.Right < plot.Left);
        });

        AddUntilStep("direction labels stay at plot edges", () =>
        {
            var plot = plotBackgroundFor(hitScatterStatistic).ScreenSpaceDrawQuad.AABBFloat;
            var late = hitScatterStatistic.ChildrenOfType<SpriteText>().Single(t => t.Text.ToString() == "late").ScreenSpaceDrawQuad.AABBFloat;
            var fast = hitScatterStatistic.ChildrenOfType<SpriteText>().Single(t => t.Text.ToString() == "fast").ScreenSpaceDrawQuad.AABBFloat;

            return fast.Top >= plot.Top
                   && fast.Bottom <= plot.Top + 28
                   && late.Bottom <= plot.Bottom
                   && late.Top >= plot.Bottom - 28;
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
