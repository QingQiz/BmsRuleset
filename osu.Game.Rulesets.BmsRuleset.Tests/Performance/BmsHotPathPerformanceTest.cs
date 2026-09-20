using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Processing;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Tests.Audio;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

[TestFixture, NonParallelizable, Explicit("Opt-in performance measurements; run with a FullyQualifiedName filter."), Category("Performance")]
public class BmsHotPathPerformanceTest
{
    [Test]
    public void DecodeTenSeconds() => measure(() =>
    {
        var processor = new BmsFixedRatePcmProcessor(new GeneratedSource(), 1);
        var frames = 0;
        return new Measurement(() =>
        {
            foreach (var chunk in processor.ProcessChunks())
                frames += chunk.FrameCount;
        }, () => Assert.That(frames, Is.EqualTo(441000)), processor.Dispose);
    });

    [Test]
    public void MaintainOneThousandSamples() => measure(() =>
    {
        var controller = BmsPcmMaintenanceTest.CreateController(1000);
        controller.Update(0);
        return new Measurement(() =>
        {
            for (var i = 0; i < 1000; i++)
                controller.Update(0);
        }, () => Assert.That(controller.PreparedSampleKeys.Count(), Is.EqualTo(1000)), controller.Dispose);
    });

    [Test]
    public void ScanPinnedOverBudgetCache() => measure(() =>
    {
        var (cache, leases) = BmsPcmMaintenanceTest.CreatePinnedCache(4096);
        cache.EvictUnused();
        return new Measurement(() =>
        {
            for (var i = 0; i < 1000; i++)
                cache.EvictUnused();
        }, () => Assert.That(leases.All(lease => lease.Asset.IsComplete), Is.True), cache.Dispose);
    });

    [Test]
    public void MixSixtyFourLongSamples() => measure(() =>
    {
        var asset = new BmsPcmAsset(44100, 2);
        var samples = Enumerable.Repeat(0.001f, 8192).ToArray();
        for (var i = 0; i < 2048; i++)
            asset.Publish(new BmsPcmChunk(i * 4096L, 4096, samples));
        asset.Complete(2048L * 4096);
        var mixer = new BmsPcmVoiceMixer();
        mixer.SubmitPlayBatch(Enumerable.Range(0, 64)
            .Select(i => new BmsVoicePlay(asset, new BmsTerminationDomain((ushort)i), 0, SourceOffsetFrame: i * 8192)).ToArray());
        var output = new float[88200];
        return new Measurement(() => mixer.Render(output), () =>
        {
            Assert.That(mixer.ActiveVoiceCount, Is.EqualTo(64));
            Assert.That(output[^1], Is.EqualTo(0.064f).Within(0.00001));
        });
    });

    [TestCase(4000)]
    [TestCase(20000)]
    public void RewindJudgements(int count) => measure(() =>
    {
        var processor = new BmsScoreProcessor();
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8 };
        for (var i = 0; i < count; i++)
            beatmap.HitObjects.Add(new BmsHitObject { StartTime = i * 10, Column = i % 8 });
        processor.ApplyBeatmap(beatmap);
        var results = beatmap.HitObjects.Select(note => new JudgementResult(note, note.CreateJudgement()) { Type = HitResult.Perfect }).ToArray();
        foreach (var result in results)
            processor.ApplyResult(result);
        var score = new ScoreInfo();
        processor.PopulateScore(score);
        return new Measurement(() =>
        {
            for (var i = results.Length - 1; i >= 0; i--)
                processor.RevertResult(results[i]);
        }, () =>
        {
            Assert.That(processor.JudgementEvents, Is.Empty);
            Assert.That(score.HitEvents, Is.Empty);
            Assert.That(processor.ScoringJudgementEventCount, Is.Zero);
        }, processor.Dispose);
    });

    private static void measure(Func<Measurement> setup)
    {
        var elapsed = new double[7];
        var allocations = new long[7];
        for (var run = -2; run < elapsed.Length; run++)
        {
            var measurement = setup();
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                var started = Stopwatch.GetTimestamp();
                measurement.Run();
                var milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                measurement.Verify();
                if (run < 0)
                    continue;

                elapsed[run] = milliseconds;
                allocations[run] = allocated;
            }
            finally
            {
                measurement.Dispose?.Invoke();
            }
        }

        var report = new
        {
            Test = TestContext.CurrentContext.Test.Name,
            Runtime = RuntimeInformation.FrameworkDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            Milliseconds = elapsed,
            AllocatedBytes = allocations,
            MedianMilliseconds = elapsed.Order().ElementAt(elapsed.Length / 2),
            MedianAllocatedBytes = allocations.Order().ElementAt(allocations.Length / 2),
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        TestContext.Progress.WriteLine(json);
        var directory = Environment.GetEnvironmentVariable("BMS_PERFORMANCE_RESULTS");
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, report.Test + ".json"), json);
        }
    }

    private sealed record Measurement(Action Run, Action Verify, Action Dispose = null);

    private sealed class GeneratedSource : IBmsPcmSource
    {
        private int remaining = 441000 * 2;

        public int SampleRate => 44100;

        public int Channels => 2;

        public double? OriginalDurationMilliseconds => 10000;

        public int Read(float[] buffer, int offset, int count)
        {
            var read = Math.Min(count, remaining);
            Array.Fill(buffer, 0.1f, offset, read);
            remaining -= read;
            return read;
        }

        public void Dispose()
        {
        }
    }
}
