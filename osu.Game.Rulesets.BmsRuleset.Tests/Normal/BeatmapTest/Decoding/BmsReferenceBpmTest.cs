using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Models;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.BeatmapTest.Decoding;

[TestFixture, NonParallelizable]
public class BmsReferenceBpmTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void CachedChartsUseCurrentSettingsWithoutChangingTheDecodedTimeline(bool bmson)
    {
        var previous = BmsRulesetRuntime.ConfigManager;
        using var config = new BmsRulesetConfigManager(null, new BmsRuleset().RulesetInfo);
        try
        {
            BmsRulesetRuntime.ConfigManager = config;
            config.SetValue(BmsRulesetSetting.ReferenceBpmMode, BmsReferenceBpmMode.StartBpm);
            var bytes = bmson
                ? BmsonParserTest.Chart("""[{"name":"a.wav","notes":[{"x":1,"y":960}]}]""",
                    ""","bpm_events":[{"y":960,"bpm":180}],"lines":[]""")
                : Encoding.UTF8.GetBytes("#BPM 120\n#BPM01 180\n#00108:01\n#00111:01");
            var decoded = decode(bytes, bmson);
            var originalMap = ((IBmsBeatmap)decoded).TimingMap!;
            foreach (var (mode, expected) in new[]
                     {
                         (BmsReferenceBpmMode.MaxBpm, 180), (BmsReferenceBpmMode.MinBpm, 120),
                         (BmsReferenceBpmMode.MainBpm, 180), (BmsReferenceBpmMode.StartBpm, 120),
                     })
            {
                config.SetValue(BmsRulesetSetting.ReferenceBpmMode, mode);
                var converted = (BmsBeatmap)new BmsRuleset().CreateBeatmapConverter(decoded).Convert();
                var map = converted.TimingMap!;
                Assert.That(map.ScrollReferenceBpm, Is.EqualTo(expected));
                Assert.That(converted.HitObjects.Single().StartTime, Is.EqualTo(2000));
                Assert.That(converted.HitObjects.Single().ScrollPositionAtStartTime, Is.EqualTo(240000d / expected).Within(1e-9));
                Assert.That(map, Is.Not.SameAs(originalMap));
                Assert.That(map.BarLines, Is.EqualTo(originalMap.BarLines));
                Assert.That(originalMap.ScrollReferenceBpm, Is.EqualTo(120));
                Assert.That(originalMap.GetScrollPositionAtTime(2000), Is.EqualTo(2000));
            }
        }
        finally
        {
            BmsRulesetRuntime.ConfigManager = previous;
        }
    }

    [TestCase("#00151:0101")]
    [TestCase("#LNOBJ 02\n#00111:0102")]
    public void BmsMainBpmRetainsSourceLongNoteEndpoints(string longNote)
    {
        var decoded = decode(Encoding.UTF8.GetBytes(
            "#BPM 120\n#BPM01 240\n#00108:01\n#00011:01\n#000D1:01010101\n#00031:01010101\n" + longNote));
        var converted = convert(decoded, BmsReferenceBpmMode.MainBpm);
        Assert.That(converted.HitObjects.OfType<BmsLongNote>(), Has.Exactly(1).Items);
        Assert.That(converted.TimingMap!.ScrollReferenceBpm, Is.EqualTo(240));
        Assert.That(converted.HitObjects.OfType<BmsLongNote>().Single().ScrollPositionAtEndTime, Is.EqualTo(1500).Within(1e-9));
    }

    [Test]
    public void BmsonLayersCountOnceAndNotesUseTheLastBpmAtTheirPulse()
    {
        var decoded = decode(BmsonParserTest.Chart("""
            [{"name":"a.wav","notes":[{"x":1,"y":0},{"x":1,"y":240},{"x":2,"y":240}]},
             {"name":"b.wav","notes":[{"x":1,"y":0}]},
             {"name":"c.wav","notes":[{"x":1,"y":0}]}]
            """, ""","bpm_events":[{"y":240,"bpm":240},{"y":240,"bpm":180}],"stop_events":[{"y":240,"duration":240}]"""), true);
        var converted = convert(decoded, BmsReferenceBpmMode.MainBpm);
        Assert.That(converted.HitObjects, Has.Count.EqualTo(3));
        Assert.That(converted.TimingMap!.ScrollReferenceBpm, Is.EqualTo(180));
        Assert.That(converted.HitObjects.Skip(1).Select(note => note.StartTime), Is.EqualTo(new[] { 500, 500 }));
    }

    [Test]
    public void MainBpmFollowsTheSelectedAndReplayedBranch()
    {
        var decoded = decode(Encoding.UTF8.GetBytes("""
            #BPM 120
            #RANDOM 2
            #IF 1
            #BPM01 180
            #ENDIF
            #IF 2
            #BPM01 240
            #ENDIF
            #ENDRANDOM
            #00108:01
            #00111:0101
            """));
        var first = convert(decoded, BmsReferenceBpmMode.MainBpm);
        var second = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset())
        {
            BranchRandomValueSelector = _ => 2,
            ReferenceBpmMode = BmsReferenceBpmMode.MainBpm,
        }.Convert();
        var replayed = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset())
        {
            BranchRandomValueSelector = _ => 1,
            BranchReplayDecisions = "2:2",
            ReferenceBpmMode = BmsReferenceBpmMode.MainBpm,
        }.Convert();
        Assert.That(first.TimingMap!.ScrollReferenceBpm, Is.EqualTo(180));
        Assert.That(second.TimingMap!.ScrollReferenceBpm, Is.EqualTo(240));
        Assert.That(replayed.TimingMap!.ScrollReferenceBpm, Is.EqualTo(240));
        Assert.That(replayed.HitObjects.Select(note => note.StartTime), Is.EqualTo(second.HitObjects.Select(note => note.StartTime)));
        Assert.That(replayed.BranchDecisions, Is.EqualTo(second.BranchDecisions));
    }

    [TestCase(BmsReferenceBpmMode.StartBpm, 120)]
    [TestCase(BmsReferenceBpmMode.MaxBpm, 240)]
    [TestCase(BmsReferenceBpmMode.MinBpm, 60)]
    [TestCase(BmsReferenceBpmMode.MainBpm, 240)]
    public void ReferenceChangesPreserveStopsReverseScrollAndBacktracking(BmsReferenceBpmMode mode, double reference)
    {
        var decoded = decode(Encoding.UTF8.GetBytes("""
            #BPM 120
            #BPM01 240
            #BPM02 -60
            #STOP01 48
            #SCROLL01 2
            #SCROLL02 0.5
            #SPEED01 0.75
            #00108:01
            #00109:01
            #001SC:01
            #001SP:01
            #00208:02
            #002SC:02
            #00011:01
            #00151:0101
            #00311:01
            """));
        var map = convert(decoded, mode).TimingMap!;
        Assert.That(map.ScrollReferenceBpm, Is.EqualTo(reference));
        (double Time, double Position)[] positions =
        [
            (1999, 1999), (2000, 2000), (2249, 2000), (2250, 2000),
            (2500, 3000), (3250, 6000), (4250, 5750), (7250, 5000),
        ];
        foreach (var (time, position) in positions.Concat(positions.Reverse()))
            Assert.That(map.GetScrollPositionAtTime(time), Is.EqualTo(position * 120 / reference).Within(1e-8), $"time {time}");
        Assert.That(map.ProjectTickToTime(192), Is.EqualTo(2000));
        Assert.That(map.ProjectTickToTime(384), Is.EqualTo(3250));
        Assert.That(map.GetSpeedFactorAtTime(2249), Is.EqualTo(1));
        Assert.That(map.GetSpeedFactorAtTime(2250), Is.EqualTo(.75));
    }

    private static Beatmap decode(byte[] bytes, bool bmson = false)
    {
        var info = new BeatmapInfo(new BmsRuleset().RulesetInfo) { Hash = "chart", BeatmapSet = new BeatmapSetInfo() };
        info.BeatmapSet.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = info.Hash }, bmson ? "chart.bmson" : "chart.bms"));
        return BmsBeatmapDecoder.DecodeBytes(bytes, info, _ => 1);
    }

    private static BmsBeatmap convert(Beatmap decoded, BmsReferenceBpmMode mode) =>
        (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()) { ReferenceBpmMode = mode, BranchRandomValueSelector = _ => 1 }.Convert();
}
