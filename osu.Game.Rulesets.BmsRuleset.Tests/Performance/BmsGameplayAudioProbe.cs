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
    private readonly ScopedMethodProbe probe;

    public BmsGameplayAudioProbe()
    {
        callbacks = overBudget = renderedFrames = maxTicks = 0;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        probe = new ScopedMethodProbe(typeof(BmsBassMixerBridge).GetMethod("render", BindingFlags.NonPublic | BindingFlags.Instance)!,
            typeof(BmsGameplayAudioProbe).GetMethod(nameof(before), flags),
            typeof(BmsGameplayAudioProbe).GetMethod(nameof(after), flags));
    }

    private static void before(out long __state) => __state = Stopwatch.GetTimestamp();

    private static void after(int length, long __state)
    {
        var ticks = Stopwatch.GetTimestamp() - __state;
        var frames = length / (sizeof(float) * 2);
        Interlocked.Increment(ref callbacks);
        Interlocked.Add(ref renderedFrames, frames);
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
    };

    public void Dispose() => probe.Dispose();
}
