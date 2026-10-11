using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;
using osu.Game.Rulesets.BmsRuleset.Tests.Audio;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

[TestFixture, NonParallelizable, Explicit("Opt-in PCM submission CPU measurements; benchmark approval is required."), Category("Performance")]
public class BmsPcmLiveBatchPerformanceTest
{
    [Test]
    public void MixerSingles()
    {
        var asset = BmsPcmTestHelpers.CreateAsset([new BmsPcmChunk(0, 1, [0.1f, 0.1f])]);
        var mixer = new BmsPcmVoiceMixer();
        BmsVoicePlay[] plays = [new(asset, new BmsTerminationDomain(1), 0)];
        var output = new float[2];
        var samples = new List<Sample>();
        using var process = Process.GetCurrentProcess();
        playBatches(1000);

        for (var sample = 0; sample < 5; sample++)
        {
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var cpuBefore = process.TotalProcessorTime;
            var started = Stopwatch.GetTimestamp();
            var checksum = playBatches(3000000);
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var cpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Assert.That(checksum, Is.GreaterThan(0));
            Assert.That(mixer.RenderedFrames, Is.EqualTo(1000 + (sample + 1) * 3000000));
            samples.Add(new Sample(elapsed, cpu, allocated, checksum));
        }

        Assert.That(samples.Select(sample => sample.Checksum).Distinct().Count(), Is.EqualTo(1));
        var report = JsonSerializer.Serialize(new
        {
            Voices = 1, BatchesPerSample = 3000000,
            MedianMilliseconds = samples.Select(sample => sample.Milliseconds).Order().ElementAt(2),
            MedianCpuMilliseconds = samples.Select(sample => sample.CpuMilliseconds).Order().ElementAt(2),
            MedianAllocatedBytes = samples.Select(sample => sample.AllocatedBytes).Order().ElementAt(2),
            Samples = samples,
        });
        TestContext.Progress.WriteLine(report);
        var path = Environment.GetEnvironmentVariable("BMS_LIVE_BATCH_PERFORMANCE_REPORT");
        if (!string.IsNullOrEmpty(path))
            File.WriteAllText(path, report);

        double playBatches(int count)
        {
            var checksum = 0d;
            for (var i = 0; i < count; i++)
            {
                mixer.SubmitPlayBatch(plays);
                mixer.Render(output);
                checksum += output[0] + output[1];
            }
            return checksum;
        }
    }

    [TestCase(1)]
    [TestCase(8)]
    [TestCase(16)]
    [TestCase(32)]
    [TestCase(64)]
    [TestCase(256)]
    public void LiveChords(int voices)
    {
        using var controller = BmsPcmMaintenanceTest.CreateController(voices);
        var resources = BmsPcmMaintenanceTest.Field<Dictionary<ushort, string>>(controller, "resolvedResources");
        var mixer = BmsPcmMaintenanceTest.Field<BmsPcmVoiceMixer>(controller, "mixer");
        for (ushort key = 0; key < voices; key++)
            resources[key] = key.ToString(CultureInfo.InvariantCulture);
        var output = new float[2];
        var samples = new List<Sample>();
        using var process = Process.GetCurrentProcess();
        var batchesPerSample = voices == 1 ? 1000000 : Math.Min(100000, 6400000 / voices);
        playBatches(1000);

        for (var sample = 0; sample < 5; sample++)
        {
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var cpuBefore = process.TotalProcessorTime;
            var started = Stopwatch.GetTimestamp();
            var checksum = playBatches(batchesPerSample);
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var cpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Assert.That(checksum, Is.GreaterThan(0));
            Assert.That(mixer.RenderedFrames, Is.EqualTo(1000 + (sample + 1) * batchesPerSample));
            samples.Add(new Sample(elapsed, cpu, allocated, checksum));
        }

        Assert.That(samples.Select(sample => sample.Checksum).Distinct().Count(), Is.EqualTo(1));
        var report = JsonSerializer.Serialize(new
        {
            Voices = voices, BatchesPerSample = batchesPerSample,
            MedianMilliseconds = samples.Select(sample => sample.Milliseconds).Order().ElementAt(2),
            MedianCpuMilliseconds = samples.Select(sample => sample.CpuMilliseconds).Order().ElementAt(2),
            MedianAllocatedBytes = samples.Select(sample => sample.AllocatedBytes).Order().ElementAt(2),
            Samples = samples,
        });
        TestContext.Progress.WriteLine(report);
        var path = Environment.GetEnvironmentVariable("BMS_LIVE_BATCH_PERFORMANCE_REPORT");
        if (!string.IsNullOrEmpty(path))
            File.WriteAllText(path, report);

        double playBatches(int count)
        {
            var checksum = 0d;
            for (var i = 0; i < count; i++)
            {
                for (ushort key = 0; key < voices; key++)
                    controller.QueueLivePlay(key, 20 + (i + key) % 80);
                controller.SubmitLivePlayBatch();
                // Consume each batch so the benchmark measures submission and mixing,
                // without artificial queue growth caused by a stalled audio consumer.
                mixer.Render(output);
                checksum += output[0] + output[1];
            }
            return checksum;
        }
    }

    private sealed record Sample(double Milliseconds, double CpuMilliseconds, long AllocatedBytes, double Checksum);
}
