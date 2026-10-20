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
                processor.ResetReplayState();
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
                var history = processor.GaugeHistory;
                Assert.That(history, Has.Count.EqualTo(count));
                Assert.That(history[^1].States, Is.EqualTo(processor.CurrentGaugeStates));
                return verify(processor, count);
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
