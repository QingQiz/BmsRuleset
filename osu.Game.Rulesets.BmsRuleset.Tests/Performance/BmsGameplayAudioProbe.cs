#nullable enable

using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Native;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

// Observe the native callback as well as the update thread: a healthy frame rate alone cannot
// establish that the audio device is being fed within its buffer deadline.
internal sealed class BmsGameplayAudioProbe : IDisposable
{
    private static long callbacks;
    private static long overBudget;
    private static long renderedFrames;
    private static long maxTicks;
    private static long totalTicks;
    private static long cycleCallbacks;
    private static long totalCycles;
    private readonly ScopedMethodProbe probe;

    public BmsGameplayAudioProbe()
    {
        callbacks = overBudget = renderedFrames = maxTicks = totalTicks = 0;
        cycleCallbacks = totalCycles = 0;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        probe = new ScopedMethodProbe(typeof(BmsBassMixerBridge).GetMethod("render", BindingFlags.NonPublic | BindingFlags.Instance)!,
            typeof(BmsGameplayAudioProbe).GetMethod(nameof(before), flags),
            typeof(BmsGameplayAudioProbe).GetMethod(nameof(after), flags));
    }

    private readonly record struct CallbackStart(long Ticks, BmsThreadCpuCounter.CycleSnapshot? Cycles);

    private static void before(out CallbackStart __state)
    {
        var cycles = BmsThreadCpuCounter.ReadCycles();
        __state = new CallbackStart(Stopwatch.GetTimestamp(), cycles);
    }

    private static void after(int length, CallbackStart __state)
    {
        var ticks = Stopwatch.GetTimestamp() - __state.Ticks;
        var cycles = BmsThreadCpuCounter.ElapsedCycles(__state.Cycles, BmsThreadCpuCounter.ReadCycles());
        if (cycles is { } measured)
        {
            Interlocked.Add(ref totalCycles, checked((long)measured));
            Interlocked.Increment(ref cycleCallbacks);
        }
        var frames = length / (sizeof(float) * 2);
        Interlocked.Increment(ref callbacks);
        Interlocked.Add(ref renderedFrames, frames);
        Interlocked.Add(ref totalTicks, ticks);
        if (ticks / (double)Stopwatch.Frequency > frames / 44100d)
            Interlocked.Increment(ref overBudget);
        var previous = Volatile.Read(ref maxTicks);
        while (ticks > previous)
        {
            var actual = Interlocked.CompareExchange(ref maxTicks, ticks, previous);
            if (actual == previous)
                break;
            previous = actual;
        }
    }

    public object Snapshot() => new
    {
        Callbacks = Volatile.Read(ref callbacks),
        RenderedFrames = Volatile.Read(ref renderedFrames),
        CallbacksOverBufferDuration = Volatile.Read(ref overBudget),
        MaxCallbackMs = Volatile.Read(ref maxTicks) * 1000d / Stopwatch.Frequency,
        TotalCallbackMs = Volatile.Read(ref totalTicks) * 1000d / Stopwatch.Frequency,
        CycleCallbacks = Volatile.Read(ref cycleCallbacks),
        TotalCallbackCycles = Volatile.Read(ref totalCycles),
    };

    internal readonly record struct Totals(long Callbacks, long RenderedFrames, long OverBudget, long Ticks,
                                          long CycleCallbacks, long Cycles)
    {
        public object Since(Totals start) => new
        {
            Callbacks = Callbacks - start.Callbacks,
            RenderedFrames = RenderedFrames - start.RenderedFrames,
            CallbacksOverBufferDuration = OverBudget - start.OverBudget,
            TotalCallbackMs = (Ticks - start.Ticks) * 1000d / Stopwatch.Frequency,
            CycleCallbacks = CycleCallbacks - start.CycleCallbacks,
            TotalCallbackCycles = Cycles - start.Cycles,
        };
    }

    // Read cumulative counters at gameplay boundaries without resetting a live callback.
    // The counters are independent, so one concurrent callback may straddle the boundary.
    internal Totals ReadTotals() => new(Volatile.Read(ref callbacks), Volatile.Read(ref renderedFrames),
        Volatile.Read(ref overBudget), Volatile.Read(ref totalTicks), Volatile.Read(ref cycleCallbacks), Volatile.Read(ref totalCycles));

    public void Dispose() => probe.Dispose();
}
