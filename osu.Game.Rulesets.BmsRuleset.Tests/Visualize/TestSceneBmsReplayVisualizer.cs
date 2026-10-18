using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.LongNoteMode;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsReplayVisualizer : BmsPlayerTestScene
{
    private BmsReplayVisualizer visualizer = null!;
    private BmsLayoutVariant layout = BmsLayoutVariant.Bme7K;
    private bool useReplay = true;

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(useReplay ? _ =>
    {
        var key1 = BmsKeyBindingConfiguration.ActionForColumn(layout, 1)!.Value;
        var key2 = BmsKeyBindingConfiguration.ActionForColumn(layout, 2)!.Value;
        var key3 = BmsKeyBindingConfiguration.ActionForColumn(layout, 3)!.Value;
        return
        [
            new BmsReplayFrame(0),
            new BmsReplayFrame(1025, key1),
            new BmsReplayFrame(1030),
            new BmsReplayFrame(1950, key2),
            new BmsReplayFrame(2980),
            new BmsReplayFrame(3500, key3),
            new BmsReplayFrame(3510),
            new BmsReplayFrame(3600, key3),
            new BmsReplayFrame(3610),
            new BmsReplayFrame(6000),
        ];
    } : null);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = layout,
            TotalColumns = BmsLayout.GetTotalColumns(layout),
            HitObjects =
            {
                new BmsNote { StartTime = 1000, Column = 1 },
                new BmsLongNote { StartTime = 2000, Duration = 1000, Column = 2 },
                new BmsNote { StartTime = 4000, Column = 3 },
                new BmsNote { StartTime = 60000, Column = 1 },
            },
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(BmsLayoutVariant.Bme7K)]
    [TestCase(BmsLayoutVariant.Bme7K2P)]
    [TestCase(BmsLayoutVariant.Bme7KDouble)]
    [TestCase(BmsLayoutVariant.Pms9KDouble)]
    public void TestReplaySeekingPauseAndSettings(BmsLayoutVariant variant)
    {
        AddStep("set layout and load player", () =>
        {
            layout = variant;
            useReplay = true;
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("add visualizer", () => Player.HUDOverlay.Add(visualizer = new BmsReplayVisualizer
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(360, 480),
        }));
        AddUntilStep("visualizer loaded", () => visualizer.IsLoaded);

        seek(3200);
        BmsReplayVisualizer.PlotRectangle[] snapshot = [];
        RectangleF[] positions = [];
        var geometryVersion = 0L;
        AddUntilStep("replay index ready", () => primitives().Any(drawable => drawable.Kind == BmsReplayVisualizer.PlotElementKind.Input));
        AddStep("capture replay geometry", () =>
        {
            snapshot = primitives();
            positions = snapshot.Select(drawable => drawable.Bounds).ToArray();
            geometryVersion = visualizer.Plot.GeometryVersion;
            Assert.That(snapshot, Is.Not.Empty);
            Assert.That(snapshot.All(drawable => float.IsFinite(drawable.Bounds.Y) && drawable.Bounds.Height > 0), Is.True);
            var input = snapshot.Where(drawable => drawable.Kind == BmsReplayVisualizer.PlotElementKind.Input).ToArray();
            Assert.That(input, Has.Length.EqualTo(2));
            Assert.That(input.All(drawable => !drawable.Outline && Math.Abs(drawable.Bounds.Width - input[0].Bounds.Width) < 0.001f), Is.True);
            Assert.That(input.Max(drawable => drawable.Bounds.Height), Is.GreaterThan(100));
            var line = snapshot.Single(drawable => drawable.Kind == BmsReplayVisualizer.PlotElementKind.Judgement);
            Assert.That(line.Bounds.X, Is.Zero);
            Assert.That(line.Bounds.Width, Is.EqualTo(visualizer.DrawWidth));
            Assert.That(line.Bounds.Centre.Y, Is.EqualTo(visualizer.DrawHeight * 0.25f));
            assertConnection(1, 1000, 1025, regularNote: true);
            assertConnection(2, 2000, 1950);
            assertConnection(2, 3000, 2980);
        });
        AddWaitStep("remain paused", 3);
        AddAssert("paused timeline stays fixed", () => primitives().Select(drawable => drawable.Bounds).SequenceEqual(positions));
        AddAssert("paused geometry is reused", () => visualizer.Plot.GeometryVersion == geometryVersion);
        AddStep("hide notes while paused", () => visualizer.ShowNotes.Value = false);
        AddAssert("notes and their connections hidden", () => primitives().All(drawable =>
            drawable.Kind is BmsReplayVisualizer.PlotElementKind.Input or BmsReplayVisualizer.PlotElementKind.Judgement));
        AddAssert("input positions and colours preserved", () => primitives().Where(drawable => drawable.Kind == BmsReplayVisualizer.PlotElementKind.Input)
            .SequenceEqual(snapshot.Where(drawable => drawable.Kind == BmsReplayVisualizer.PlotElementKind.Input)));
        AddStep("restore notes while paused", () => visualizer.ShowNotes.Value = true);
        AddAssert("restoring notes restores geometry", () => primitives().SequenceEqual(snapshot));
        seek(500);
        AddAssert("future input hidden after rewind", () => primitives().All(drawable =>
            drawable.Kind is BmsReplayVisualizer.PlotElementKind.Note or BmsReplayVisualizer.PlotElementKind.Judgement));
        seek(3200);
        AddAssert("forward seek restores geometry", () => primitives().Select(drawable => drawable.Bounds).SequenceEqual(positions));
        AddAssert("geometry matches previous snapshot", () => primitives().SequenceEqual(snapshot));

        AddStep("hide visualizer", () => visualizer.Alpha = 0);
        seek(500);
        AddAssert("hidden geometry is not rebuilt", () => primitives().SequenceEqual(snapshot));
        AddStep("show visualizer", () => visualizer.Alpha = 1);
        AddAssert("showing catches up after rewind", () => primitives().All(drawable =>
            drawable.Kind is BmsReplayVisualizer.PlotElementKind.Note or BmsReplayVisualizer.PlotElementKind.Judgement));
        seek(3200);

        AddStep("hide inputs", () => visualizer.ShowInputs.Value = false);
        AddAssert("inputs and note connections hidden", () => primitives().All(drawable =>
            drawable.Kind is BmsReplayVisualizer.PlotElementKind.Note or BmsReplayVisualizer.PlotElementKind.Judgement));
        AddAssert("judgement line remains visible", () => primitives().Count(drawable => drawable.Kind == BmsReplayVisualizer.PlotElementKind.Judgement) == 1);
        AddStep("hide notes and inputs", () => visualizer.ShowNotes.Value = false);
        AddAssert("only judgement line remains", () => primitives().Length == 1 && primitives()[0].Kind == BmsReplayVisualizer.PlotElementKind.Judgement);
        AddStep("restore notes", () => visualizer.ShowNotes.Value = true);
        AddStep("resize and change time range", () =>
        {
            visualizer.Size = new Vector2(180, 240);
            visualizer.TimeRange.Value = 2;
        });
        AddAssert("geometry stays inside resized timeline", () => primitives().All(drawable =>
            drawable.Bounds.X >= 0 && drawable.Bounds.Y >= 0 && drawable.Bounds.Right <= visualizer.DrawWidth + 0.01f &&
            drawable.Bounds.Bottom <= visualizer.DrawHeight + 0.01f));

        AddStep("show judgement colours", () =>
        {
            visualizer.ShowInputs.Value = true;
            visualizer.TimeRange.Value = 6;
        });
        seek(4500);
        AddStep("missed note red and empty press grey", () =>
        {
            var displayColumn = BmsLayout.Is2P(layout) ? 2 : 3;
            var columnWidth = visualizer.DrawWidth / BmsLayout.GetTotalColumns(layout);
            var lane = primitives().Where(drawable => drawable.Bounds.X >= displayColumn * columnWidth &&
                drawable.Bounds.Right <= (displayColumn + 1) * columnWidth).ToArray();
            var details = string.Join(", ", ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.Select(judgement =>
                $"{judgement.Source.Column}:{judgement.Source.StartTime}:{judgement.Result}:{judgement.SuppressPenalty}"));
            Assert.That(lane.Any(drawable => drawable.Kind == BmsReplayVisualizer.PlotElementKind.Note && drawable.Colour == (Colour4)BmsHitResultColours.ForHitResult(HitResult.Meh)),
                Is.True, $"missed note: {details}; {string.Join(", ", lane.Select(drawable => $"{drawable.Outline}:{drawable.Colour}"))}");
            Assert.That(lane.Any(drawable => drawable.Kind == BmsReplayVisualizer.PlotElementKind.Input && drawable.Colour == (Colour4)BmsHitResultColours.ForHitResult(HitResult.Miss)),
                Is.True, $"empty press: {details}");
            Assert.That(lane.Any(drawable => drawable.Kind == BmsReplayVisualizer.PlotElementKind.Input && drawable.Colour == Colour4.White), Is.True, $"unjudged press: {details}");
            Assert.That(lane.Any(drawable => drawable.Kind == BmsReplayVisualizer.PlotElementKind.Connection), Is.False);
        });

        void assertConnection(int column, double expectedTime, double actualTime, bool regularNote = false)
        {
            var columnWidth = visualizer.DrawWidth / BmsLayout.GetTotalColumns(layout);
            var displayColumn = BmsLayout.Is2P(layout) ? column - 1 : column;
            var centre = (displayColumn + 0.5f) * columnWidth;
            var expected = (float)(0.25 + (3200 - expectedTime) / 4000) * visualizer.DrawHeight + (regularNote ? 2.5f : 0);
            var actual = (float)(0.25 + (3200 - actualTime) / 4000) * visualizer.DrawHeight;
            var laneConnections = primitives().Where(drawable => drawable.Kind == BmsReplayVisualizer.PlotElementKind.Connection &&
                drawable.Bounds.Left >= displayColumn * columnWidth && drawable.Bounds.Right <= (displayColumn + 1) * columnWidth).ToArray();
            Assert.That(laneConnections, Is.Not.Empty);
            Assert.That(laneConnections.All(drawable => Math.Abs(drawable.Bounds.Centre.X - centre) < 0.001f),
                Is.True, "connections align with note and input centres");
            var connections = laneConnections.Where(drawable => drawable.Bounds.Top >= Math.Min(expected, actual) - 0.001f &&
                drawable.Bounds.Bottom <= Math.Max(expected, actual) + 0.001f).ToArray();
            Assert.That(connections, Is.Not.Empty, "connection spans the matching timing offset");
            Assert.That(connections.Any(drawable => Math.Abs((expected < actual ? drawable.Bounds.Top : drawable.Bounds.Bottom) - expected) < 0.001f),
                Is.True, "connection starts at the matching note");
        }
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void TestLiveInputAndImmediateLongNoteHead(BmsLongNoteMode mode)
    {
        AddStep("load live player", () =>
        {
            layout = BmsLayoutVariant.Bme7K;
            useReplay = false;
            LoadPlayer([mode switch
            {
                BmsLongNoteMode.ChargeNote => new BmsModChargeNote(),
                BmsLongNoteMode.HellChargeNote => new BmsModHellChargeNote(),
                _ => new BmsModLongNote(),
            }]);
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("freeze live time source", () =>
            typeof(GameplayClockContainer).GetMethod("StopGameplayClock", BindingFlags.Instance | BindingFlags.NonPublic)!
                                          .Invoke(Player.GameplayClockContainer, null));
        AddStep("add live visualizer", () => Player.HUDOverlay.Add(visualizer = new BmsReplayVisualizer()));
        AddUntilStep("visualizer loaded", () => visualizer.IsLoaded);
        seek(1000);
        AddStep("press and release between HUD frames", () =>
        {
            var input = ((BmsInputManager)((BmsDrawableRuleset)Player.DrawableRuleset).KeyBindingInputManager).KeyBindingContainer;
            input.TriggerPressed(BmsAction.Key1);
            input.TriggerReleased(BmsAction.Key1);
        });
        AddAssert("short live press displayed and coloured", () => primitives().Count(drawable =>
            drawable.Kind == BmsReplayVisualizer.PlotElementKind.Input && !drawable.Outline &&
            drawable.Colour == (Colour4)BmsHitResultColours.ForHitResult(HitResult.Perfect)) == 1);
        seek(2000);
        AddStep("hide notes before long note input", () => visualizer.ShowNotes.Value = false);
        AddStep("hide during long note input", () => visualizer.Alpha = 0);
        AddStep("hold long note", () =>
            ((BmsInputManager)((BmsDrawableRuleset)Player.DrawableRuleset).KeyBindingInputManager).KeyBindingContainer.TriggerPressed(BmsAction.Key2));
        AddStep("show long note input", () => visualizer.Alpha = 1);
        AddAssert("hidden note still colours held input immediately", () => primitives().Any(drawable =>
            drawable.Kind == BmsReplayVisualizer.PlotElementKind.Input &&
            Math.Abs(drawable.Bounds.Y + 1.5f - visualizer.DrawHeight * 0.25f) < 0.001f &&
            drawable.Colour == (Colour4)BmsHitResultColours.ForHitResult(HitResult.Perfect)) &&
            primitives().All(drawable => drawable.Kind is BmsReplayVisualizer.PlotElementKind.Input or BmsReplayVisualizer.PlotElementKind.Judgement));
        AddStep("show notes", () => visualizer.ShowNotes.Value = true);
        AddAssert("head coloured before tail", () => primitives().Any(drawable =>
            drawable.Outline && drawable.Bounds.Height > 20 && drawable.Colour == (Colour4)BmsHitResultColours.ForHitResult(HitResult.Perfect)));
        seek(2100);
        AddAssert("held input is a filled rectangle reaching current time", () => primitives().Any(drawable =>
            drawable.Kind == BmsReplayVisualizer.PlotElementKind.Input && !drawable.Outline && drawable.Bounds.Height > 10 &&
            Math.Abs(drawable.Bounds.Y + 1.5f - visualizer.DrawHeight * 0.25f) < 0.001f &&
            drawable.Colour == (Colour4)BmsHitResultColours.ForHitResult(HitResult.Perfect)));
        seek(15000);
        AddAssert("offscreen press stays a single clipped rectangle", () =>
        {
            var input = primitives().Where(drawable => drawable.Kind == BmsReplayVisualizer.PlotElementKind.Input).ToArray();
            var result = mode == BmsLongNoteMode.LongNote ? HitResult.Perfect : HitResult.Meh;
            return input.Length == 1 && input.All(drawable =>
                !drawable.Outline && drawable.Bounds.Height > 200 && drawable.Bounds.Y >= 0 &&
                drawable.Bounds.Bottom <= visualizer.DrawHeight && drawable.Colour == (Colour4)BmsHitResultColours.ForHitResult(result));
        });
        AddStep("release long note", () =>
            ((BmsInputManager)((BmsDrawableRuleset)Player.DrawableRuleset).KeyBindingInputManager).KeyBindingContainer.TriggerReleased(BmsAction.Key2));
    }

    private BmsReplayVisualizer.PlotRectangle[] primitives() => visualizer.Plot.Rectangles.ToArray();

    private void seek(double time)
    {
        AddStep($"seek to {time}", () =>
        {
            if (useReplay)
                Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(time);
        });
        AddUntilStep("simulation caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < 0.001);
        AddWaitStep("HUD catches up", 2);
    }
}
