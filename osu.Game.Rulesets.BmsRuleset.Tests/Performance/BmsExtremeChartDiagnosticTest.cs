using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Replays;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

[TestFixture, Explicit("Requires a local chart supplied through BMS_DIAGNOSTIC_CHART.")]
public class BmsExtremeChartDiagnosticTest
{
    [Test]
    public void DescribeDensity()
    {
        var path = Environment.GetEnvironmentVariable("BMS_DIAGNOSTIC_CHART");
        Assert.That(File.Exists(path), Is.True);
        var decoded = BmsBeatmapDecoder.DecodeBytes(File.ReadAllBytes(path!), randomValueSelector: _ => 1);
        var chart = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()) { BranchRandomValueSelector = _ => 1 }.Convert();
        var replay = new BmsAutoGenerator(chart).Generate();
        var report = new
        {
            Objects = chart.HitObjects.Count,
            Types = chart.HitObjects.GroupBy(n => n.GetType().Name).ToDictionary(g => g.Key, g => g.Count()),
            ReplayFrames = replay.Frames.Count,
            MaxTime = chart.HitObjects.Max(n => n.StartTime),
            Bpm = chart.TimingMap!.BpmEvents.OrderByDescending(b => Math.Abs(b.Bpm)).Take(10),
            DenseSeconds = chart.HitObjects.GroupBy(n => (int)(n.StartTime / 1000)).OrderByDescending(g => g.Count()).Take(10)
                .Select(g => new { Second = g.Key, Count = g.Count(), FirstMs = g.Min(n => n.StartTime), LastMs = g.Max(n => n.StartTime),
                    Times = g.Select(n => n.StartTime).Distinct().Count(), Samples = g.Select(n => n.SampleKey).Distinct().Count() }),
        };
        TestContext.Progress.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Assert.That(chart.HitObjects, Is.Not.Empty);
    }
}
