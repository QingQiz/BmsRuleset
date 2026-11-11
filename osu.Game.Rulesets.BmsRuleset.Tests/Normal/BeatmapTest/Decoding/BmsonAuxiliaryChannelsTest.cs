using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Models;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.BeatmapTest.Decoding;

[TestFixture]
public class BmsonAuxiliaryChannelsTest
{
    [TestCase("beat-5k", BmsGaugeProfileFamily.FiveKeys)]
    [TestCase("beat-7k", BmsGaugeProfileFamily.SevenKeys)]
    [TestCase("popn-5k", BmsGaugeProfileFamily.Pms)]
    [TestCase("popn-9k", BmsGaugeProfileFamily.Pms)]
    [TestCase("keyboard-24k", BmsGaugeProfileFamily.Keyboard)]
    public void MinesDoNotChangeTheLayoutsGaugeRecovery(string mode, BmsGaugeProfileFamily family)
    {
        var notes = string.Join(',', Enumerable.Range(0, 300).Select(index => $$"""{"x":1,"y":{{index * 480}}}"""));
        var chart = BmsonParserTest.Chart($$"""[{"name":"key.wav","notes":[{{notes}}]}]""",
            """, "mine_channels":[{"name":"mine.wav","notes":[{"x":2,"y":240,"damage":10}]}]""",
            $$""", "mode_hint":"{{mode}}" """);
        Assert.That(BmsonParser.Parse(chart).Total, Is.EqualTo(BmsGaugeCalculator.CalculateDefaultTotal(300, family)));
    }

    [TestCase("[{\"x\":1,\"y\":0,\"damage\":10}]")]
    [TestCase("[{\"x\":2,\"y\":0,\"damage\":-1}]")]
    public void ConflictingOrNegativeMinesAreRejected(string notes)
    {
        var chart = BmsonParserTest.Chart("""[{"name":"key.wav","notes":[{"x":1,"y":0}]}]""",
            $$""", "mine_channels":[{"name":"mine.wav","notes":{{notes}}}]""");
        Assert.Throws<InvalidDataException>(() => BmsonParser.Parse(chart));
    }

    [Test]
    public void ScrollMinesAndInvisibleKeysoundsShareTheRuntimeModels()
    {
        var bytes = BmsonParserTest.Chart("""[{"name":"a.wav","notes":[{"x":1,"y":480}]}]""", """
                                                                                              ,"scroll_events":[{"y":0,"rate":2},{"y":240,"rate":0}],
                                                                                              "key_channels":[{"name":"empty.wav","notes":[{"x":2,"y":240}]}],
                                                                                              "mine_channels":[{"name":"boom.wav","notes":[{"x":3,"y":720,"damage":12.5}]}]
                                                                                              """);
        var beatmap = convert(bytes);
        var mine = beatmap.HitObjects.OfType<BmsLandmine>().Single();
        Assert.That(mine.LandmineDamagePercent, Is.EqualTo(12.5));
        Assert.That(beatmap.SampleDefinitions[mine.SampleKey!.Value].Path, Is.EqualTo("boom.wav"));
        Assert.That(BmsGameplayAudioController.GetSampleUsages(beatmap).Any(usage => usage.SampleKey == mine.SampleKey), Is.True);
        Assert.That(beatmap.InvisibleNotes.Single().Column, Is.EqualTo(2));
        Assert.That(beatmap.SampleDefinitions[beatmap.InvisibleNotes.Single().SampleKey!.Value].Path, Is.EqualTo("empty.wav"));
        Assert.That(beatmap.TimingMap!.GetScrollPositionAtTime(1000), Is.EqualTo(beatmap.TimingMap.GetScrollPositionAtTime(500)));
        Assert.That(BmsonParser.ParseImportSummary(bytes).TotalObjectCount, Is.EqualTo(2));
    }

    private static BeatmapInfo createInfo()
    {
        var info = new BeatmapInfo(new BmsRuleset().RulesetInfo) { Hash = "chart", BeatmapSet = new BeatmapSetInfo() };
        info.BeatmapSet.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = info.Hash }, "chart.bmson"));
        return info;
    }

    private static BmsBeatmap convert(byte[] bytes) =>
        (BmsBeatmap)new BmsBeatmapConverter(BmsBeatmapDecoder.DecodeBytes(bytes, createInfo()), new BmsRuleset()).Convert();
}
