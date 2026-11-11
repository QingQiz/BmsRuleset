using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.BeatmapTest.Decoding;

[TestFixture]
public class BmsonBgaTest
{
    [TestCase("bga_events")]
    [TestCase("layer_events")]
    [TestCase("poor_events")]
    public void EmptyExtensionDefaultsKeepOrdinaryImageEvents(string channel)
    {
        var result = BmsonParser.Parse(BmsonParserTest.Chart(events: $$"""
            ,"bga":{"bga_header":[{"id":1,"name":"bgi.bmp"}],
            "{{channel}}":[{"y":0,"id":1,"id_set":[],"condition":"","interval":0}]}
            """));
        var source = result.Bga.Events.Single();
        Assert.That(result.Bga.BitmapDefinitions[source.DefinitionKey], Is.EqualTo("bgi.bmp"));
        Assert.That(source.Animation, Is.Null);
    }

    [Test]
    public void BgaDuplicateHeadersSequencesConditionsAndImageRolesAreRetained()
    {
        var result = BmsonParser.Parse(BmsonParserTest.Chart("""[{"name":"a.wav","notes":[{"x":1,"y":240}]}]""", """
                                                                                                                 ,"bga":{"bga_header":[{"id":1,"name":"old.png"},{"id":1,"name":"new.png"},{"id":2,"name":"second.png"}],
                                                                                                                 "bga_sequence":[{"id":100,"sequence":[{"time":0,"id":1},{"time":50,"id":1},{"time":100,"id":2},{"time":200}]}],
                                                                                                                 "layer_events":[{"y":0,"id_set":[100,2],"condition":"play","interval":2}]}
                                                                                                                 """, """, "back_image":"back.png","title_image":"title.png" """));
        Assert.That(result.Bga.BitmapDefinitions.Values, Does.Not.Contain("old.png"));
        Assert.That(result.Bga.Events.Single(evt => evt.Layer == BmsBgaLayer.Title).EndTime, Is.EqualTo(500));
        Assert.That(result.Bga.Events.Any(evt => evt.Layer == BmsBgaLayer.Background), Is.True);
        var source = result.Bga.Events.Single(evt => evt.Layer == BmsBgaLayer.Layer1);
        var playback = new BmsBgaAnimationPlayback(source);
        Assert.That(playback.GetFrame(0).Key, Is.Null);
        playback.Trigger(BmsBgaTrigger.Miss, 10);
        Assert.That(playback.GetFrame(10).Key, Is.Null);
        playback.Trigger(BmsBgaTrigger.Play, 10);
        Assert.That(result.Bga.BitmapDefinitions[playback.GetFrame(109).Key!.Value], Is.EqualTo("new.png"));
        Assert.That(result.Bga.BitmapDefinitions[playback.GetFrame(110).Key!.Value], Is.EqualTo("second.png"));
        Assert.That(playback.GetFrame(60).StartTime, Is.EqualTo(60));
        Assert.That(playback.GetFrame(210).Key, Is.Null);
        playback.Trigger(BmsBgaTrigger.Play, 220);
        Assert.That(playback.GetFrame(220).Key, Is.Null);
        playback.Trigger(BmsBgaTrigger.Play, 230);
        Assert.That(result.Bga.BitmapDefinitions[playback.GetFrame(230).Key!.Value], Is.EqualTo("second.png"));
    }

    [Test]
    public void BgaResourceCountIsNotLimitedToUshort()
    {
        var headers = string.Join(',', Enumerable.Range(0, 65536).Select(id => $$"""{"id":{{id}},"name":"a.png"}"""));
        var result = BmsonParser.Parse(BmsonParserTest.Chart(events: $$""", "bga":{"bga_header":[{{headers}}],"bga_events":[{"y":0,"id":65535}]}"""));
        Assert.That(result.Bga.BitmapDefinitions.Count, Is.EqualTo(65536));
        Assert.That(result.Bga.BitmapDefinitions[result.Bga.Events.Single().DefinitionKey], Is.EqualTo("a.png"));
    }
}
