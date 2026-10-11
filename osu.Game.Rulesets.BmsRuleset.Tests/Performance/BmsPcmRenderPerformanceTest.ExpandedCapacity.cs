using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;
using osu.Game.Rulesets.BmsRuleset.Tests.Audio;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

public partial class BmsPcmRenderPerformanceTest
{
    [TestCase(512, 0, TestName = "ExpandedCapacitySparseVoices_Peak512_Voices0")]
    [TestCase(512, 1, TestName = "ExpandedCapacitySparseVoices_Peak512_Voices1")]
    [TestCase(512, 8, TestName = "ExpandedCapacitySparseVoices_Peak512_Voices8")]
    [TestCase(2048, 0, TestName = "ExpandedCapacitySparseVoices_Peak2048_Voices0")]
    [TestCase(2048, 1, TestName = "ExpandedCapacitySparseVoices_Peak2048_Voices1")]
    [TestCase(2048, 8, TestName = "ExpandedCapacitySparseVoices_Peak2048_Voices8")]
    [TestCase(8192, 0, TestName = "ExpandedCapacitySparseVoices_Peak8192_Voices0")]
    [TestCase(8192, 1, TestName = "ExpandedCapacitySparseVoices_Peak8192_Voices1")]
    [TestCase(8192, 8, TestName = "ExpandedCapacitySparseVoices_Peak8192_Voices8")]
    public void ExpandedCapacitySparseVoices(int peakVoices, int voices)
    {
        const int block_frames = 512;
        var frames = voices <= 1 ? 17640000 : 4410000;
        var asset = new BmsPcmAsset(44100, 2);
        var pcm = new float[4096 * 2];
        for (var frame = 0; frame < 4096; frame++)
        {
            pcm[frame * 2] = (float)Math.Sin(frame * .071) * .003f;
            pcm[frame * 2 + 1] = (float)Math.Cos(frame * .053) * -.002f;
        }
        for (var frame = 0; frame < frames + 4096; frame += 4096)
            asset.Publish(new BmsPcmChunk(frame, 4096, pcm));
        asset.Complete(asset.PublishedFrameCount);
        var output = new float[frames * 2];
        var warmup = new float[block_frames * 2];
        var samples = new List<Sample>();
        var capacities = new List<object>();
        using var process = Process.GetCurrentProcess();

        for (var run = -2; run < 5; run++)
        {
            var expansion = BmsPcmExpandedCapacityWorkload.Prepare(peakVoices);
            var mixer = expansion.Mixer;
            BmsPcmExpandedCapacityWorkload.SubmitSparseVoices(mixer, asset, voices);
            // Setup and attack settling stay outside the timed region; the measured
            // callback sees retained capacity and the same sparse source positions.
            mixer.Render(warmup);
            var capacity = BmsPcmExpandedCapacityWorkload.ReadCapacity(mixer);
            Assert.That(capacity.OccupiedVoices, Is.EqualTo(voices));
            Assert.That(capacity.OccupiedSegments, Is.EqualTo(voices == 0 ? 0 : 1));
            Assert.That(capacity.VoiceCapacity, Is.EqualTo(peakVoices));
            var cpuBefore = process.TotalProcessorTime;
            var threadCpuBefore = BmsThreadCpuCounter.Read();
            var threadCyclesBefore = BmsThreadCpuCounter.ReadCycles();
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            for (var frame = 0; frame < frames; frame += block_frames)
                mixer.Render(output.AsSpan(frame * 2, Math.Min(block_frames, frames - frame) * 2));
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var threadCycles = BmsThreadCpuCounter.ElapsedCycles(threadCyclesBefore, BmsThreadCpuCounter.ReadCycles());
            var threadCpu = BmsThreadCpuCounter.ElapsedMilliseconds(threadCpuBefore, BmsThreadCpuCounter.Read());
            var cpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
            Assert.That(allocated, Is.Zero, "Expanded capacity must not allocate in the rendering callback.");
            Assert.That(mixer.ActiveVoiceCount, Is.EqualTo(voices));
            Assert.That(mixer.RenderedFrames, Is.EqualTo(frames + 1024));
            Assert.That(BmsPcmExpandedCapacityWorkload.ReadCapacity(mixer), Is.EqualTo(capacity));
            Assert.That(output.Any(value => value != 0), Is.EqualTo(voices > 0));
            var hash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(output.AsSpan())));
            if (run >= 0)
            {
                samples.Add(new Sample(elapsed, cpu, allocated, hash, threadCycles, threadCpu));
                capacities.Add(new { expansion.Peak, expansion.Retired, Measured = capacity });
            }
        }

        Assert.That(samples.Select(sample => sample.PcmHash).Distinct().Count(), Is.EqualTo(1));
        var report = JsonSerializer.Serialize(new
        {
            PeakVoices = peakVoices, Voices = voices, FramesPerSample = frames, BlockFrames = block_frames,
            WarmupRuns = 2, SourceStorage = "shared-4096-frame-chunk", Runtime = runtimeInfo(process),
            MedianMilliseconds = samples.Select(sample => sample.Milliseconds).Order().ElementAt(2),
            MedianCpuMilliseconds = samples.Select(sample => sample.CpuMilliseconds).Order().ElementAt(2),
            MedianAllocatedBytes = samples.Select(sample => sample.AllocatedBytes).Order().ElementAt(2),
            Samples = samples, Capacities = capacities,
        });
        TestContext.Progress.WriteLine(report);
        var path = Environment.GetEnvironmentVariable("BMS_PCM_RENDER_PERFORMANCE_REPORT");
        if (!string.IsNullOrEmpty(path))
            File.WriteAllText(path, report);
    }
}
