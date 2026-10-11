using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

[TestFixture, NonParallelizable, Explicit("Opt-in sustained PCM rendering measurements; benchmark approval is required."), Category("Performance")]
public partial class BmsPcmRenderPerformanceTest
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(8)]
    [TestCase(64)]
    [TestCase(256)]
    public void SustainedVoices(int voices) => measureSustainedVoices(voices, false);

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(8)]
    public void FutureCommand(int voices) => measureSustainedVoices(voices, true);

    [Test]
    public void LimiterOverload() => measureSustainedVoices(1, false, 2);

    [Test]
    public void LimiterRecovery() => measureSustainedVoices(1, false, 2, 512);

    private static void measureSustainedVoices(int voices, bool futureCommand, float amplitude = 0.001f, int overloadFrames = 0)
    {
        var frames = voices <= 1 ? 17640000 : voices <= 8 ? 4410000 : 441000;
        var asset = new BmsPcmAsset(44100, 2);
        var pcm = new float[4096 * 2];
        for (var i = 0; i < pcm.Length; i++)
            pcm[i] = (float)Math.Sin(i * 0.07) * (overloadFrames == 0 || i / 2 < overloadFrames ? amplitude : 0.05f);
        for (var frame = 0; frame < frames + 4096; frame += 4096)
            asset.Publish(new BmsPcmChunk(frame, 4096, pcm));
        asset.Complete(asset.PublishedFrameCount);
        var plays = Enumerable.Range(0, voices).Select(i =>
            new BmsVoicePlay(asset, new BmsTerminationDomain((ushort)i), 0, SourceOffsetFrame: i * 7)).ToArray();
        var output = new float[frames * 2];
        var samples = new List<Sample>();
        using var process = Process.GetCurrentProcess();

        for (var run = -2; run < 5; run++)
        {
            var mixer = new BmsPcmVoiceMixer();
            mixer.SubmitPlayBatch(plays);
            if (futureCommand)
                mixer.SubmitControl(BmsVoiceCommandType.SetMasterGain, frames + 1, 0, 0);
            var cpuBefore = process.TotalProcessorTime;
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var threadCpuBefore = BmsThreadCpuCounter.Read();
            var threadCyclesBefore = BmsThreadCpuCounter.ReadCycles();
            var started = Stopwatch.GetTimestamp();
            // Device-sized blocks retain callback boundaries while timing only rendering.
            for (var frame = 0; frame < frames; frame += 512)
                mixer.Render(output.AsSpan(frame * 2, Math.Min(512, frames - frame) * 2));
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var threadCycles = BmsThreadCpuCounter.ElapsedCycles(threadCyclesBefore, BmsThreadCpuCounter.ReadCycles());
            var threadCpu = BmsThreadCpuCounter.ElapsedMilliseconds(threadCpuBefore, BmsThreadCpuCounter.Read());
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var cpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
            Assert.That(mixer.ActiveVoiceCount, Is.EqualTo(voices));
            Assert.That(mixer.RenderedFrames, Is.EqualTo(frames));
            Assert.That(output.Any(value => value != 0), Is.EqualTo(voices > 0));
            var hash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(output.AsSpan())));
            if (run >= 0)
                samples.Add(new Sample(elapsed, cpu, allocated, hash, threadCycles, threadCpu));
        }

        Assert.That(samples.Select(sample => sample.PcmHash).Distinct().Count(), Is.EqualTo(1));
        var report = JsonSerializer.Serialize(new
        {
            Voices = voices, FramesPerSample = frames, FutureCommand = futureCommand,
            Amplitude = amplitude, OverloadFrames = overloadFrames, Runtime = runtimeInfo(process),
            MedianMilliseconds = samples.Select(sample => sample.Milliseconds).Order().ElementAt(2),
            MedianCpuMilliseconds = samples.Select(sample => sample.CpuMilliseconds).Order().ElementAt(2),
            MedianAllocatedBytes = samples.Select(sample => sample.AllocatedBytes).Order().ElementAt(2),
            Samples = samples,
        });
        TestContext.Progress.WriteLine(report);
        var path = Environment.GetEnvironmentVariable("BMS_PCM_RENDER_PERFORMANCE_REPORT");
        if (!string.IsNullOrEmpty(path))
            File.WriteAllText(path, report);
    }

    private sealed record Sample(double Milliseconds, double CpuMilliseconds, long AllocatedBytes, string PcmHash,
                                 ulong? ThreadCycles, double? ThreadCpuMilliseconds);

    private static object runtimeInfo(Process process) => new
    {
        ProductionMvid = typeof(BmsPcmVoiceMixer).Module.ModuleVersionId,
        TestMvid = typeof(BmsPcmRenderPerformanceTest).Module.ModuleVersionId,
        ProcessorAffinity = OperatingSystem.IsWindows() ? process.ProcessorAffinity.ToInt64() : (long?)null,
        TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
        JitNoInline = Environment.GetEnvironmentVariable("COMPlus_JitNoInline"),
    };

    [TestCase(1)]
    [TestCase(8)]
    [TestCase(64)]
    [TestCase(256)]
    public void DistinctAssets(int voices)
    {
        const int asset_frames = 44100;
        var repetitions = voices <= 8 ? 100 : 10;
        var frames = asset_frames * repetitions;
        var plays = new BmsVoicePlay[voices];
        for (var voice = 0; voice < voices; voice++)
        {
            var asset = new BmsPcmAsset(44100, 2);
            // Separate storage exceeds the shared-chunk benchmark's small cache footprint.
            // Different chunk boundaries also exercise cursor refreshes at different frames.
            for (var start = 0; start < asset_frames;)
            {
                var count = Math.Min(997 + voice % 128, asset_frames - start);
                var pcm = new float[count * 2];
                for (var i = 0; i < pcm.Length; i++)
                    pcm[i] = (float)Math.Sin((start * 2 + i + voice * 7) * 0.07) * 0.001f;
                asset.Publish(new BmsPcmChunk(start, count, pcm));
                start += count;
            }
            asset.Complete(asset_frames);
            plays[voice] = new BmsVoicePlay(asset, new BmsTerminationDomain((ushort)voice), 0);
        }
        var output = new float[frames * 2];
        var samples = new List<Sample>();
        using var process = Process.GetCurrentProcess();

        for (var run = -2; run < 5; run++)
        {
            var mixer = new BmsPcmVoiceMixer();
            var cpuBefore = process.TotalProcessorTime;
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var threadCpuBefore = BmsThreadCpuCounter.Read();
            var threadCyclesBefore = BmsThreadCpuCounter.ReadCycles();
            var started = Stopwatch.GetTimestamp();
            for (var repetition = 0; repetition < repetitions; repetition++)
            {
                var start = repetition * asset_frames;
                for (var voice = 0; voice < plays.Length; voice++)
                    plays[voice] = plays[voice] with { TargetFrame = start };
                mixer.SubmitPlayBatch(plays);
                for (var frame = 0; frame < asset_frames; frame += 512)
                    mixer.Render(output.AsSpan((start + frame) * 2, Math.Min(512, asset_frames - frame) * 2));
            }
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var threadCycles = BmsThreadCpuCounter.ElapsedCycles(threadCyclesBefore, BmsThreadCpuCounter.ReadCycles());
            var threadCpu = BmsThreadCpuCounter.ElapsedMilliseconds(threadCpuBefore, BmsThreadCpuCounter.Read());
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var cpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
            Assert.That(mixer.ActiveVoiceCount, Is.Zero);
            Assert.That(mixer.RenderedFrames, Is.EqualTo(frames));
            Assert.That(output.Any(value => value != 0), Is.True);
            var hash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(output.AsSpan())));
            if (run >= 0)
                samples.Add(new Sample(elapsed, cpu, allocated, hash, threadCycles, threadCpu));
        }

        Assert.That(samples.Select(sample => sample.PcmHash).Distinct().Count(), Is.EqualTo(1));
        var report = JsonSerializer.Serialize(new
        {
            Voices = voices, FramesPerSample = frames, DistinctAssets = true,
            Runtime = runtimeInfo(process),
            PcmBytes = (long)voices * asset_frames * 2 * sizeof(float),
            MedianMilliseconds = samples.Select(sample => sample.Milliseconds).Order().ElementAt(2),
            MedianCpuMilliseconds = samples.Select(sample => sample.CpuMilliseconds).Order().ElementAt(2),
            MedianAllocatedBytes = samples.Select(sample => sample.AllocatedBytes).Order().ElementAt(2),
            Samples = samples,
        });
        TestContext.Progress.WriteLine(report);
        var path = Environment.GetEnvironmentVariable("BMS_PCM_RENDER_PERFORMANCE_REPORT");
        if (!string.IsNullOrEmpty(path))
            File.WriteAllText(path, report);
    }

    [TestCase(1)]
    [TestCase(8)]
    [TestCase(64)]
    public void BoundaryVoices(int voices)
    {
        // At 176 frames, attack and tail touch: almost every sample takes the full envelope path.
        const int slice_frames = 176;
        var batches = voices == 1 ? 100000 : voices == 8 ? 10000 : 1000;
        var frames = batches * slice_frames;
        var asset = new BmsPcmAsset(44100, 2);
        var pcm = new float[slice_frames * 2];
        for (var i = 0; i < pcm.Length; i++)
            pcm[i] = (float)Math.Sin(i * 0.07) * 0.001f;
        asset.Publish(new BmsPcmChunk(0, slice_frames, pcm));
        asset.Complete(slice_frames);
        var plays = Enumerable.Range(0, voices).Select(i =>
            new BmsVoicePlay(asset, new BmsTerminationDomain((ushort)i), 0)).ToArray();
        var output = new float[frames * 2];
        var samples = new List<Sample>();
        using var process = Process.GetCurrentProcess();

        for (var run = -2; run < 5; run++)
        {
            var mixer = new BmsPcmVoiceMixer();
            var cpuBefore = process.TotalProcessorTime;
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var threadCpuBefore = BmsThreadCpuCounter.Read();
            var threadCyclesBefore = BmsThreadCpuCounter.ReadCycles();
            var started = Stopwatch.GetTimestamp();
            for (var frame = 0; frame < frames; frame += slice_frames)
            {
                for (var i = 0; i < plays.Length; i++)
                    plays[i] = plays[i] with { TargetFrame = frame };
                mixer.SubmitPlayBatch(plays);
                mixer.Render(output.AsSpan(frame * 2, slice_frames * 2));
            }
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var threadCycles = BmsThreadCpuCounter.ElapsedCycles(threadCyclesBefore, BmsThreadCpuCounter.ReadCycles());
            var threadCpu = BmsThreadCpuCounter.ElapsedMilliseconds(threadCpuBefore, BmsThreadCpuCounter.Read());
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var cpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
            Assert.That(mixer.ActiveVoiceCount, Is.Zero);
            Assert.That(mixer.RenderedFrames, Is.EqualTo(frames));
            Assert.That(output.Any(value => value != 0), Is.True);
            var hash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(output.AsSpan())));
            if (run >= 0)
                samples.Add(new Sample(elapsed, cpu, allocated, hash, threadCycles, threadCpu));
        }

        Assert.That(samples.Select(sample => sample.PcmHash).Distinct().Count(), Is.EqualTo(1));
        var report = JsonSerializer.Serialize(new
        {
            Voices = voices, FramesPerSample = frames, SliceFrames = slice_frames, BatchesPerSample = batches,
            Runtime = runtimeInfo(process),
            MedianMilliseconds = samples.Select(sample => sample.Milliseconds).Order().ElementAt(2),
            MedianCpuMilliseconds = samples.Select(sample => sample.CpuMilliseconds).Order().ElementAt(2),
            MedianAllocatedBytes = samples.Select(sample => sample.AllocatedBytes).Order().ElementAt(2),
            Samples = samples,
        });
        TestContext.Progress.WriteLine(report);
        var path = Environment.GetEnvironmentVariable("BMS_PCM_RENDER_PERFORMANCE_REPORT");
        if (!string.IsNullOrEmpty(path))
            File.WriteAllText(path, report);
    }
}
