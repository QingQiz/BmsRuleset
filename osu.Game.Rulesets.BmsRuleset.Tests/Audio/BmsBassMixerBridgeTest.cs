#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using ManagedBass;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Native;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[HeadlessTest, NonParallelizable]
public partial class BmsBassMixerBridgeTest : TestScene
{
    [Resolved]
    private AudioManager audioManager { get; set; } = null!;

    private AudioMixer mixer = null!;
    private BmsBassMixerBridge bridge = null!;
    private Func<int> getHandle = null!;
    private Func<Action, Task> enqueue = null!;

    [Test]
    public void AttachedPollingDoesNotAllocate()
    {
        addCreateSteps();
        AddStep("warm attachment checks", () => poll(10000));
        AddAssert("stable attachment checks allocate nothing", () =>
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            poll(10000);
            return GC.GetAllocatedBytesForCurrentThread() == before;
        });
        addCleanupSteps();
    }

    [Test]
    public void ReattachesToRecreatedMixerAndDisposes()
    {
        var oldHandle = 0;
        Task recreation = Task.CompletedTask;
        addCreateSteps();
        AddStep("recreate mixer on audio thread", () =>
        {
            oldHandle = getHandle();
            var updateDevice = mixer.GetType().GetMethod("UpdateDevice")!.CreateDelegate<Action<int>>(mixer);
            recreation = enqueue(() => updateDevice(Bass.CurrentDevice));
        });
        AddUntilStep("mixer recreation finished", () => recreation.IsCompleted);
        AddAssert("recreation succeeded", () => recreation.IsCompletedSuccessfully && getHandle() != 0 && getHandle() != oldHandle);
        AddUntilStep("bridge follows new handle", () =>
        {
            bridge.EnsureAttached();
            return attached();
        });
        addCleanupSteps();
    }

    [Test, Explicit("Opt-in attachment polling CPU measurement; benchmark approval is required."), Category("Performance")]
    public void MeasureAttachedPolling()
    {
        addCreateSteps();
        AddStep("measure stable attachment polling", () =>
        {
            const int iterations = 5000000;
            var samples = new List<Sample>();
            poll(100000);
            poll(100000);
            for (var sample = 0; sample < 5; sample++)
            {
                var cpuStart = BmsThreadCpuCounter.Read();
                var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                var started = Stopwatch.GetTimestamp();
                poll(iterations);
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                var cpu = BmsThreadCpuCounter.ElapsedMilliseconds(cpuStart, BmsThreadCpuCounter.Read());
                samples.Add(new Sample(elapsed, cpu, allocated));
                Assert.That(attached(), Is.True);
            }

            var json = JsonSerializer.Serialize(new
            {
                Iterations = iterations,
                MedianMilliseconds = samples.Select(s => s.Milliseconds).Order().ElementAt(2),
                MedianThreadCpuMilliseconds = samples.Select(s => s.ThreadCpuMilliseconds).Order().ElementAt(2),
                MedianAllocatedBytes = samples.Select(s => s.AllocatedBytes).Order().ElementAt(2),
                Samples = samples,
            });
            TestContext.Progress.WriteLine(json);
            var path = Environment.GetEnvironmentVariable("BMS_BRIDGE_PERFORMANCE_REPORT");
            if (!string.IsNullOrEmpty(path))
                File.WriteAllText(path, json);
        });
        addCleanupSteps();
    }

    private void addCreateSteps()
    {
        if (!BmsAudioPlatform.SupportsNativeBass)
            Assert.Ignore("The native BMS mixer bridge requires a supported BASS platform.");

        AddStep("create mixer and bridge", () =>
        {
            BmsPcmMixerPatcher.InstallOnce();
            mixer = audioManager.CreateAudioMixer(BmsPcmMixerPatcher.MIXER_IDENTIFIER);
            getHandle = mixer.GetType().GetProperty("Handle")!.GetMethod!.CreateDelegate<Func<int>>(mixer);
            enqueue = typeof(AudioComponent).GetMethod("EnqueueAction", BindingFlags.Instance | BindingFlags.NonPublic)!
                                            .CreateDelegate<Func<Action, Task>>(mixer);
            bridge = new BmsBassMixerBridge(mixer, new BmsPcmVoiceMixer());
        });
        AddUntilStep("bridge attached", () =>
        {
            bridge.EnsureAttached();
            return attached();
        });
    }

    private bool attached() => BmsPcmMaintenanceTest.Field<Task>(bridge, "pendingLifecycleAction").IsCompletedSuccessfully
                               && getHandle() != 0
                               && BmsPcmMaintenanceTest.Field<int>(bridge, "attachedMixerHandle") == getHandle()
                               && BmsPcmMaintenanceTest.Field<int>(bridge, "streamHandle") != 0;

    private void poll(int iterations)
    {
        for (var i = 0; i < iterations; i++)
            bridge.EnsureAttached();
    }

    private void addCleanupSteps()
    {
        AddStep("dispose bridge", () => bridge.Dispose());
        AddUntilStep("bridge stream released", () => BmsPcmMaintenanceTest.Field<Task>(bridge, "pendingLifecycleAction").IsCompletedSuccessfully
                                                    && BmsPcmMaintenanceTest.Field<int>(bridge, "streamHandle") == 0);
        AddAssert("disposed bridge stays detached", () =>
        {
            bridge.EnsureAttached();
            return BmsPcmMaintenanceTest.Field<int>(bridge, "streamHandle") == 0;
        });
        AddStep("dispose mixer", () => mixer.Dispose());
    }

    protected override void Dispose(bool isDisposing)
    {
        bridge?.Dispose();
        mixer?.Dispose();
        base.Dispose(isDisposing);
    }

    private sealed record Sample(double Milliseconds, double? ThreadCpuMilliseconds, long AllocatedBytes);
}
