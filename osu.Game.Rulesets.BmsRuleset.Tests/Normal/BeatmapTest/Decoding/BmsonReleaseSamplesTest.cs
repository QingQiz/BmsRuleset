using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Models;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.BeatmapTest.Decoding;

[TestFixture]
public class BmsonReleaseSamplesTest
{
    [Test]
    public void LayeredHeadAndReleaseSoundsSurviveConversionPreviewAndCloning()
    {
        var beatmap = createBeatmap();
        var hold = beatmap.HitObjects.OfType<BmsLongNote>().Single();
        hold.SampleVolume = 70;
        hold.TailSampleVolume = 30;
        Assert.That(hold.GetSamples().Select(sample => beatmap.SampleDefinitions[sample.SampleKey].Path),
            Is.EqualTo(new[] { "head-a.wav", "head-b.wav" }));
        Assert.That(hold.GetTailSamples().Select(sample => beatmap.SampleDefinitions[sample.SampleKey].Path),
            Is.EqualTo(new[] { "tail-a.wav", "tail-b.wav" }));
        Assert.That(hold.GetTailSamples().Select(sample => sample.Time), Is.All.EqualTo(hold.EndTime));
        Assert.That(BmsGameplayAudioController.GetSampleUsages(beatmap, new BmsEngineChart(beatmap)).Count(usage => usage.Time == hold.EndTime), Is.EqualTo(2));

        var preview = BmsEventPreviewTimeline.Create(beatmap, beatmap, default);
        Assert.That(preview.Entries.Count(entry => entry.Volume == 70), Is.EqualTo(2));
        Assert.That(preview.Entries.Count(entry => entry.Volume == 30), Is.EqualTo(2));
        Assert.That(preview.Entries, Has.Count.EqualTo(6));

        var clone = (BmsLongNote)hold.CloneForConversion();
        Assert.That(clone.GetSamples(), Is.EqualTo(hold.GetSamples()));
        Assert.That(clone.GetTailSamples(), Is.EqualTo(hold.GetTailSamples()));
        clone.TailSampleKey = null;
        clone.AdditionalTailSampleKeys = [];
        Assert.That(hold.GetTailSamples(), Has.Exactly(2).Items);
    }

    [TestCase("background")]
    [TestCase("hide-scratch")]
    [TestCase("practice")]
    [TestCase("invert")]
    public void ConversionsPreserveBothReleaseSounds(string conversion)
    {
        var beatmap = createBeatmap();
        var hold = beatmap.HitObjects.OfType<BmsLongNote>().Single();
        hold.TailSampleVolume = 40;
        switch (conversion)
        {
            case "background":
                new BmsModBackgroundKeysound().ApplyToBeatmap(beatmap);
                Assert.That(hold.GetSamples(), Is.Empty);
                Assert.That(hold.GetTailSamples(), Is.Empty);
                break;
            case "hide-scratch":
                new BmsModHideScratch().ApplyToBeatmap(beatmap);
                break;
            case "practice":
                beatmap = BmsPracticeBeatmap.Create(beatmap, new BmsPracticeSettings { StartTime = 1600, EndTime = 2500 });
                break;
            case "invert":
                new BmsModInvert().ApplyToBeatmap(beatmap);
                break;
        }

        var tails = beatmap.BackgroundSampleEvents.Where(sample => sample.Time == 1500).ToArray();
        Assert.That(tails.Select(sample => beatmap.SampleDefinitions[sample.SampleKey].Path),
            Is.EqualTo(new[] { "tail-a.wav", "tail-b.wav" }));
        Assert.That(tails.Select(sample => sample.Volume), Is.All.EqualTo(40));
        if (conversion != "invert")
            Assert.That(beatmap.BackgroundSampleEvents.Count(sample => sample.Time == 1000), Is.EqualTo(2));
    }

    [Test]
    public void ReleaseSoundRequiresAMatchingLongNoteEndpoint()
    {
        Assert.Throws<InvalidDataException>(() => BmsonParser.Parse(BmsonParserTest.Chart(
            """[{"name":"tail.wav","notes":[{"x":1,"y":240,"up":true}]}]""")));
    }

    private static BmsBeatmap createBeatmap()
    {
        var bytes = BmsonParserTest.Chart("""
            [{"name":"tail-a.wav","notes":[{"x":8,"y":720,"up":true}]},
             {"name":"head-a.wav","notes":[{"x":8,"y":480,"l":240},{"x":1,"y":960}]},
             {"name":"tail-b.wav","notes":[{"x":8,"y":720,"up":true}]},
             {"name":"head-b.wav","notes":[{"x":8,"y":480,"l":240},{"x":1,"y":960}]}]
            """);
        var info = new BeatmapInfo(new BmsRuleset().RulesetInfo) { Hash = "chart", BeatmapSet = new BeatmapSetInfo() };
        info.BeatmapSet.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = info.Hash }, "chart.bmson"));
        return (BmsBeatmap)new BmsBeatmapConverter(BmsBeatmapDecoder.DecodeBytes(bytes, info), new BmsRuleset()).Convert();
    }
}
