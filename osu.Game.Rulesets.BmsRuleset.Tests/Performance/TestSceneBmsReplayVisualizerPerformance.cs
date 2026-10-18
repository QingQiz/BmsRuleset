#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Tests.Visualize;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.UI;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

[HeadlessTest, NonParallelizable, Explicit("Opt-in replay HUD measurements; requires BMS_DIAGNOSTIC_CHART and benchmark approval."), Category("Performance")]
public partial class TestSceneBmsReplayVisualizerPerformance : BmsPlayerTestScene
{
    private const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int frames = 10000;
    private const int warmups = 4;
    private BmsReplayVisualizer visualizer = null!;
    private BmsBeatmap chart = null!;
    private double startTime;
    private int intervalNotes;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(beatmap => new BmsAutoGenerator(beatmap).Generate().Frames);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var path = Environment.GetEnvironmentVariable("BMS_DIAGNOSTIC_CHART");
        if (!File.Exists(path))
            Assert.Ignore("Set BMS_DIAGNOSTIC_CHART to an existing local BMS chart.");

        var decoded = BmsBeatmapDecoder.DecodeBytes(File.ReadAllBytes(path!), randomValueSelector: _ => 1);
        chart = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()) { BranchRandomValueSelector = _ => 1 }.Convert();
        BmsTestBeatmaps.SetupBeatmapInfo(chart, ruleset);
        var times = chart.HitObjects.Where(note => note is not BmsLandmine).Select(note => note.StartTime).Order().ToArray();
        var end = 0;
        for (var first = 0; first < times.Length; first++)
        {
            while (end < times.Length && times[end] < times[first] + frames)
                end++;
            if (end - first <= intervalNotes)
                continue;

            intervalNotes = end - first;
            startTime = times[first];
        }

        return chart;
    }

    [TestCase(4, "both")]
    [TestCase(10, "both")]
    [TestCase(4, "inputs")]
    [TestCase(4, "notes")]
    [TestCase(4, "paused")]
    [TestCase(4, "hidden")]
    public void DensestInterval(int range, string mode)
    {
        AddStep("load original chart", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        AddStep("add HUD", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.HUDOverlay.Add(visualizer = new BmsReplayVisualizer());
            visualizer.TimeRange.Value = range;
            visualizer.ShowNotes.Value = mode != "inputs";
            visualizer.ShowInputs.Value = mode != "notes";
            Player.GameplayClockContainer.Seek(startTime + frames + 1000);
        });
        AddUntilStep("replay caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - startTime - frames - 1000) < .001);
        AddUntilStep("HUD indexed", () => typeof(BmsReplayVisualizer).GetField("replayInputs", instance)!.GetValue(visualizer) != null);
        AddStep("verify dense interval replay", () => Assert.That(BmsGameplayDiagnosticScene.CheckJudgementCompleteness(
            chart.HitObjects.Where(note => note.StartTime >= startTime && note.StartTime < startTime + frames).ToHashSet(), Player.Results), Is.Null));
        AddStep("benchmark HUD independently of player and audio", () => measure(range, mode));
    }

    private void measure(int range, string mode)
    {
        // Keep the actual chart, replay index and score ledger. Advance only the HUD clock so
        // audio, replay catch-up and other gameplay drawables cannot dominate these measurements.
        var stability = (FrameStabilityContainer)Player.DrawableRuleset.FrameStableClock;
        var manual = (ManualClock)typeof(FrameStabilityContainer).GetField("manualClock", instance)!.GetValue(stability)!;
        var framed = (FramedClock)typeof(FrameStabilityContainer).GetField("framedClock", instance)!.GetValue(stability)!;
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", instance)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        var samples = new List<Sample>();
        var drawFrame = 0UL;
        using var geometryHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var peakRectangles = 0;
        var peakQuads = 0;
        var nodes = new BmsReplayVisualizer.PlotLayer.PlotDrawNode?[3];
        var originalTime = manual.CurrentTime;

        try
        {
            visualizer.UpdateSubTree();
            visualizer.Alpha = mode == "hidden" ? 0 : 1;
            for (var run = -warmups; run < 3; run++)
            {
                var updateTicks = 0L;
                var snapshotTicks = 0L;
                var copiesBefore = nodes.Sum(node => node?.TimelineCopiedRectangles ?? 0);
                var allocation = GC.GetAllocatedBytesForCurrentThread();
                for (var frame = 0; frame < frames; frame++)
                {
                    manual.CurrentTime = startTime + (mode == "paused" ? frames / 2 : frame);
                    framed.ProcessFrame();
                    var before = Stopwatch.GetTimestamp();
                    visualizer.UpdateSubTree();
                    updateTicks += Stopwatch.GetTimestamp() - before;
                    before = Stopwatch.GetTimestamp();
                    visualizer.Plot.UpdateSubTreeMasking();
                    var node = (BmsReplayVisualizer.PlotLayer.PlotDrawNode?)generate(visualizer.Plot, ++drawFrame, (int)(drawFrame % 3), false);
                    snapshotTicks += Stopwatch.GetTimestamp() - before;
                    nodes[(int)(drawFrame % 3)] = node;
                    if (run != -1 || node == null)
                        continue;

                    peakRectangles = Math.Max(peakRectangles, visualizer.Plot.Rectangles.Count);
                    peakQuads = Math.Max(peakQuads, node.QuadCount);
                    if (frame % 125 == 24)
                        geometryHash.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(visualizer.Plot.Rectangles.Select(rectangle => new
                        {
                            rectangle.Bounds.X, rectangle.Bounds.Y, rectangle.Bounds.Width, rectangle.Bounds.Height,
                            rectangle.Colour.R, rectangle.Colour.G, rectangle.Colour.B, rectangle.Colour.A,
                            rectangle.Outline, rectangle.Opacity, rectangle.Kind,
                        }))));
                }

                var allocated = GC.GetAllocatedBytesForCurrentThread() - allocation;
                if (run >= 0)
                    samples.Add(new Sample(updateTicks * 1000d / Stopwatch.Frequency, snapshotTicks * 1000d / Stopwatch.Frequency, allocated,
                        nodes.Sum(node => node?.TimelineCopiedRectangles ?? 0) - copiesBefore));
            }

            var report = JsonSerializer.Serialize(new
            {
                Chart = Path.GetFileName(Environment.GetEnvironmentVariable("BMS_DIAGNOSTIC_CHART")),
                Runtime = RuntimeInformation.FrameworkDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                JitOptimizationsDisabled = typeof(BmsReplayVisualizer).Assembly.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled,
                TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                Objects = chart.HitObjects.Count, StartMs = startTime, EndMs = startTime + frames, IntervalNotes = intervalNotes,
                RangeSeconds = range, Mode = mode, UpdatesPerSample = frames, SnapshotsPerSample = frames,
                WarmupSamples = warmups,
                Judgements = ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.Count, PeakRectangles = peakRectangles, PeakQuads = peakQuads,
                GeometryHash = Convert.ToHexString(geometryHash.GetHashAndReset()),
                MedianUpdateMs = samples.Select(sample => sample.UpdateMilliseconds).Order().ElementAt(1),
                MedianSnapshotMs = samples.Select(sample => sample.SnapshotMilliseconds).Order().ElementAt(1),
                MedianAllocatedBytes = samples.Select(sample => sample.AllocatedBytes).Order().ElementAt(1),
                Samples = samples,
            }, new JsonSerializerOptions { WriteIndented = true });
            TestContext.Progress.WriteLine(report);
            var directory = Environment.GetEnvironmentVariable("BMS_PERFORMANCE_RESULTS");
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, $"replay-hud-{range}-{mode}.json"), report);
            }

            if (mode != "hidden")
                Assert.That(peakRectangles, Is.GreaterThan(0));
        }
        finally
        {
            manual.CurrentTime = originalTime;
            framed.ProcessFrame();
        }
    }

    private sealed record Sample(double UpdateMilliseconds, double SnapshotMilliseconds, long AllocatedBytes, long CopiedRectangles);
}
