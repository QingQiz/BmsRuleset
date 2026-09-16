#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using osu.Framework.Allocation;
using osu.Framework.Configuration;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Platform;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

internal partial class BmsGameplayDiagnosticGame(BmsGameplayDiagnosticOptions options) : OsuGameBase
{
    private readonly List<Frame> frames = new(300_000);
    private readonly Stopwatch timeout = Stopwatch.StartNew();
    private BmsGameplayDiagnosticScene scene = null!;
    private long previousFrame;
    private bool finished;
    private int[]? initialCollections;
    private readonly List<object> aliveSnapshots = [];
    private double nextSnapshot;
    private readonly List<object> memorySnapshots = [];
    private readonly List<object> framePacingChanges = [];
    private FramePacing? previousPacing;
    private FrameworkConfigManager frameworkConfig = null!;
    private Func<bool>? getVerticalSync;

    public int ResultCode { get; private set; } = 1;

    private readonly record struct Frame(double ChartMs, double SimulationMs, double UpdateMs, double IntervalMs, double GcPauseMs, long AllocatedBytes, int AliveObjects);

    private readonly record struct FramePacing(bool WindowActive, double UpdateClockHz, bool UpdateThrottling,
                                              double? DrawClockHz, bool? DrawThrottling, bool? RendererVerticalSync);

    protected override IDictionary<FrameworkSetting, object> GetFrameworkConfigDefaults() => new Dictionary<FrameworkSetting, object>
    {
        [FrameworkSetting.WindowMode] = WindowMode.Windowed,
        [FrameworkSetting.WindowedSize] = new System.Drawing.Size(1280, 720),
        [FrameworkSetting.FrameSync] = FrameSync.Unlimited,
        [FrameworkSetting.VolumeUniversal] = 0.0,
    };

    [BackgroundDependencyLoader]
    private void load(FrameworkConfigManager config)
    {
        frameworkConfig = config;
        config.SetValue(FrameworkSetting.FrameSync, FrameSync.Unlimited);
        config.SetValue(FrameworkSetting.ExecutionMode, ExecutionMode.MultiThreaded);
        // Keep real sample scheduling/mixing in the capture while making non-audio runs silent.
        // Explicitly override saved host settings as defaults alone do not mute an existing config.
        config.SetValue(FrameworkSetting.VolumeUniversal, options.AudioOutput ? 1.0 : 0.0);
    }

    public override void SetHost(GameHost host)
    {
        // "Unlimited" otherwise retains the framework's 1000 Hz safety cap.
        host.AllowBenchmarkUnlimitedFrames = true;
        Storage = host.GetStorage(Path.Combine(options.Output, "storage"));
        base.SetHost(host);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        // The renderer exposes its actual VSync state internally. Bind the read-only diagnostic
        // accessor once so recording state changes does not box a reflection result each frame.
        if (!options.Headless)
            getVerticalSync = typeof(IRenderer).GetProperty("VerticalSync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                                              .GetGetMethod(true)?.CreateDelegate<Func<bool>>(Host.Renderer);
        configureFramePacing();
        Add(scene = new BmsGameplayDiagnosticScene(options));
    }

    private void configureFramePacing()
    {
        // Display-mode changes may reapply the framework limiter. Also set each thread's
        // inactive rate: a shared 1000 Hz inactive cap would silently increase a 240 Hz draw cap.
        if (Host.MaximumUpdateHz != options.UpdateHz)
            Host.MaximumUpdateHz = options.UpdateHz;
        if (Host.MaximumInactiveHz != options.UpdateHz)
            Host.MaximumInactiveHz = options.UpdateHz;
        if (Host.MaximumDrawHz != options.DrawHz)
            Host.MaximumDrawHz = options.DrawHz;
        if (Host.DrawThread != null && Host.DrawThread.InactiveHz != options.DrawHz)
            Host.DrawThread.InactiveHz = options.DrawHz;
    }

    public override bool UpdateSubTree()
    {
        configureFramePacing();
        var start = Stopwatch.GetTimestamp();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var gcPause = GC.GetTotalPauseDuration();
        var updated = base.UpdateSubTree();
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        gcPause = GC.GetTotalPauseDuration() - gcPause;

        if (finished || scene == null)
            return updated;

        if (timeout.Elapsed.TotalSeconds > options.Duration + 120)
        {
            Finish("Timed out waiting for gameplay to complete.");
            return updated;
        }

        if (!scene.Ready || scene.ChartTime < options.Start * 1000)
            return updated;

        var pacing = new FramePacing(Host.IsActive.Value, Host.UpdateThread.Clock.MaximumUpdateHz, Host.UpdateThread.Clock.Throttling,
            Host.DrawThread?.Clock.MaximumUpdateHz, Host.DrawThread?.Clock.Throttling, getVerticalSync?.Invoke());
        if (previousPacing != pacing)
        {
            framePacingChanges.Add(new { scene.ChartTime, Pacing = pacing });
            previousPacing = pacing;
        }

        initialCollections ??= [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
        if (previousFrame != 0)
            frames.Add(new Frame(scene.ChartTime, scene.SimulationTime, elapsed, Stopwatch.GetElapsedTime(previousFrame, start).TotalMilliseconds,
                gcPause.TotalMilliseconds, allocated, scene.AliveObjects()));
        previousFrame = start;

        if (scene.ChartTime >= nextSnapshot)
        {
            using var process = Process.GetCurrentProcess();
            memorySnapshots.Add(new
            {
                scene.ChartTime,
                ManagedBytes = GC.GetTotalMemory(false),
                process.WorkingSet64,
                process.PrivateMemorySize64,
                process.PeakWorkingSet64,
            });
            aliveSnapshots.Add(scene.DescribeAlive());
            Console.WriteLine(FormattableString.Invariant($"BMS_GAMEPLAY chart_ms={scene.ChartTime:F0} simulation_ms={scene.SimulationTime:F0} update_ms={elapsed:F3} alive={scene.AliveObjects()}"));
            nextSnapshot = scene.ChartTime + 10000;
        }

        if (scene.ChartTime >= scene.EndTime)
            Finish(scene.CompletionError());
        return updated;
    }

    public void Finish(string? error = null)
    {
        if (finished)
            return;

        finished = true;
        try
        {
            Directory.CreateDirectory(options.Output);
            using (var writer = new StreamWriter(Path.Combine(options.Output, "frames.csv")))
            {
                writer.WriteLine("chart_ms,simulation_ms,update_ms,interval_ms,gc_pause_ms,allocated_bytes,alive_objects");
                foreach (var frame in frames)
                    writer.WriteLine(FormattableString.Invariant($"{frame.ChartMs:F3},{frame.SimulationMs:F3},{frame.UpdateMs:F6},{frame.IntervalMs:F6},{frame.GcPauseMs:F6},{frame.AllocatedBytes},{frame.AliveObjects}"));
            }

            var report = new
            {
                Success = error == null && frames.Count > 0,
                Error = error,
                Options = options,
                ChartSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(options.Chart))),
                Assembly = typeof(BmsRuleset).Assembly.ManifestModule.ModuleVersionId,
                Runtime = RuntimeInformation.FrameworkDescription,
                OS = RuntimeInformation.OSDescription,
                Environment.ProcessorCount,
                Renderer = Host.Renderer.GetType().FullName,
                AudioOutput = options.Headless ? "no-sound-device" : options.AudioOutput ? "audible" : "muted-master",
                MasterVolume = Audio?.Volume.Value,
                FramePacing = new
                {
                    RequestedUpdateHz = options.UpdateHz,
                    RequestedDrawHz = options.DrawHz,
                    Host.AllowBenchmarkUnlimitedFrames,
                    FrameSync = frameworkConfig?.Get<FrameSync>(FrameworkSetting.FrameSync).ToString(),
                    ExecutionMode = frameworkConfig?.Get<ExecutionMode>(FrameworkSetting.ExecutionMode).ToString(),
                    Changes = framePacingChanges,
                },
                Measurement = "Whole game UpdateSubTree CPU wall time; excludes draw-node generation, GPU work and throttle. Interval includes pacing. Headless skips GPU rendering. Seek simulation completes before two seconds of playback warmup.",
                LoadMs = scene?.LoadMilliseconds,
                SeekMs = scene?.SeekMilliseconds,
                Chart = scene?.Chart == null ? null : scene.DescribeChart(),
                Summary = summarise(frames),
                AliveSnapshots = aliveSnapshots,
                MemorySnapshots = memorySnapshots,
                Seconds = frames.GroupBy(f => (int)(f.ChartMs / 1000)).Select(g => new { Second = g.Key, Statistics = summarise(g.ToArray()) }),
                GarbageCollections = initialCollections == null ? null : Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - initialCollections[i]).ToArray(),
            };
            File.WriteAllText(Path.Combine(options.Output, "summary.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            ResultCode = report.Success ? 0 : 1;
        }
        finally
        {
            Exit();
        }
    }

    private static object summarise(IReadOnlyCollection<Frame> source)
    {
        var updates = source.Select(f => f.UpdateMs).Order().ToArray();
        var intervals = source.Select(f => f.IntervalMs).Order().ToArray();
        var wallSeconds = intervals.Sum() / 1000;
        var allocatedBytes = source.Sum(f => f.AllocatedBytes);
        return new
        {
            Frames = source.Count,
            WallSeconds = wallSeconds,
            ObservedUpdateHz = wallSeconds > 0 ? source.Count / wallSeconds : 0,
            FirstChartMs = source.Count == 0 ? 0 : source.First().ChartMs,
            LastChartMs = source.Count == 0 ? 0 : source.Last().ChartMs,
            UpdateP50Ms = percentile(updates, 0.5),
            UpdateP95Ms = percentile(updates, 0.95),
            UpdateP99Ms = percentile(updates, 0.99),
            UpdateMaxMs = updates.LastOrDefault(),
            IntervalP99Ms = percentile(intervals, 0.99),
            IntervalMaxMs = intervals.LastOrDefault(),
            UpdatesOver8Ms = updates.Count(v => v > 8),
            UpdatesOver16Ms = updates.Count(v => v > 16.667),
            AllocatedBytes = allocatedBytes,
            AllocatedBytesPerUpdate = source.Count > 0 ? (double)allocatedBytes / source.Count : 0,
            AllocatedBytesPerSecond = wallSeconds > 0 ? allocatedBytes / wallSeconds : 0,
            GcPauseDuringUpdatesMs = source.Sum(f => f.GcPauseMs),
            PeakAliveObjects = source.Count == 0 ? 0 : source.Max(f => f.AliveObjects),
            MaxSimulationLagMs = source.Count == 0 ? 0 : source.Max(f => f.ChartMs - f.SimulationMs),
        };
    }

    private static double percentile(double[] values, double percentile)
        => values.Length == 0 ? 0 : values[Math.Clamp((int)Math.Ceiling(values.Length * percentile) - 1, 0, values.Length - 1)];
}
