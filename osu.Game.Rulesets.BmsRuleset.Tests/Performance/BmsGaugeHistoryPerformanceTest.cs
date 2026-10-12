using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

[TestFixture, NonParallelizable, Explicit("Opt-in gauge history measurements; benchmark approval is required."), Category("Performance")]
public class BmsGaugeHistoryPerformanceTest
{
    [Test]
    public void RecordGaugeHistory([Values(1, 6)] int layers, [Values(false, true)] bool warmed)
    {
        const int count = 100000;
        BmsJudgementPerformanceTest.Measure($"Gauge-record-{layers}-{(warmed ? "warmed" : "fresh")}", count, () =>
        {
            var processor = createProcessor(layers);
            if (warmed)
            {
                record(processor, count);
                processor.Rewind(-1);
            }

            return new BmsJudgementPerformanceTest.Measurement(() => record(processor, count), () => verify(processor, count));
        });
    }

    [TestCase(2048)]
    [TestCase(4096)]
    public void PopulateAutoGaugeScore(int count)
    {
        BmsJudgementPerformanceTest.Measure($"Gauge-populate-{count}", count, () =>
        {
            var processor = new BmsHealthProcessor();
            var mod = new BmsModAutoGauge();
            mod.ApplyToHealthProcessor(processor);
            var score = new ScoreInfo { Mods = [mod] };
            return new BmsJudgementPerformanceTest.Measurement(() =>
            {
                for (var i = 0; i < count; i++)
                {
                    processor.ApplyHellChargeTick(i % 4 < 3, eventTime: i);
                    mod.ApplyToScore(score);
                }
            }, () =>
            {
                Assert.That(BmsScoreGaugeHistoryStore.TryGet(score, out var history), Is.True);
                Assert.That(history, Has.Count.EqualTo(count));
                Assert.That(history[^1].States, Is.EqualTo(processor.CurrentGaugeStates));
                return verify(processor, count);
            });
        });
    }

    [Test]
    public void RewindGaugeHistory([Values(1, 6)] int layers)
    {
        const int count = 100000;
        BmsJudgementPerformanceTest.Measure($"Gauge-rewind-{layers}", 100, () =>
        {
            var processor = createProcessor(layers);
            record(processor, count);
            var initial = processor.GaugeHistory[0].States;
            return new BmsJudgementPerformanceTest.Measurement(() =>
            {
                for (var i = 99; i >= 0; i--)
                    processor.Rewind(i * 1000 + .5);
            }, () =>
            {
                Assert.That(processor.CurrentGaugeStates, Is.EqualTo(initial));
                return verify(processor, 1);
            });
        });
    }

    [Test]
    public void ExportGaugeHistory([Values(1, 6)] int layers)
    {
        const int count = 20000;
        BmsJudgementPerformanceTest.Measure($"Gauge-export-{layers}", count, () =>
        {
            var processor = createProcessor(layers);
            record(processor, count);
            var score = new ScoreInfo();
            return new BmsJudgementPerformanceTest.Measurement(() => BmsScoreGaugeHistoryStore.Set(score, processor.GaugeHistory), () =>
            {
                Assert.That(BmsScoreGaugeHistoryStore.TryGet(score, out var history), Is.True);
                Assert.That(history, Has.Count.EqualTo(count));
                Assert.That(history[^1].States, Is.EqualTo(processor.CurrentGaugeStates));
                processor.Rewind(-1);
                Assert.That(history, Has.Count.EqualTo(count), "Exported scores must retain their history after a seek.");
                return new { HistoryEntries = history.Count, States = history[^1].States };
            });
        });
    }

    private static BmsHealthProcessor createProcessor(int layers)
    {
        var processor = new BmsHealthProcessor();
        processor.SetGaugeTypes(layers == 6 ? BmsModAutoGauge.AUTO_GAUGE_CHAIN : [BmsGaugeType.Normal]);
        return processor;
    }

    private static void record(BmsHealthProcessor processor, int count)
    {
        for (var i = 0; i < count; i++)
            processor.ApplyHellChargeTick(i % 4 < 3, eventTime: i);
    }

    private static object verify(BmsHealthProcessor processor, int count)
    {
        Assert.That(processor.GaugeHistory, Has.Count.EqualTo(count));
        Assert.That(processor.GaugeHistory[^1].States, Is.EqualTo(processor.CurrentGaugeStates));
        return new
        {
            HistoryEntries = processor.GaugeHistory.Count,
            States = processor.CurrentGaugeStates.ToArray(),
            processor.HasFailed,
            processor.HasEverFailed,
        };
    }
}
