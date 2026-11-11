using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Beatmaps.Formats;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Difficulty;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Tests.Beatmaps;
using LineBufferedReader = osu.Game.IO.LineBufferedReader;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Difficulty;

[TestFixture]
public class BmsNegativeTimeDifficultyTest
{
    [Test]
    public async Task MiseryTorporCalculatesConsistentlyWithAndWithoutHideScratch()
    {
        using var stream = typeof(BmsNegativeTimeDifficultyTest).Assembly.GetManifestResourceStream("mania_test_songs/misery-torpor.osu")!;
        using var reader = new LineBufferedReader(stream);
        var source = new LegacyBeatmapDecoder().Decode(reader);
        // Official rulesets are not registered in the isolated ruleset test runner.
        source.BeatmapInfo.Ruleset = new RulesetInfo { OnlineID = 3, ShortName = "mania" };
        Assert.That(source.HitObjects, Has.Count.EqualTo(3022));
        Assert.That(source.HitObjects.Count(note => note.StartTime < 0), Is.EqualTo(2824));
        Assert.That(source.HitObjects[0].StartTime, Is.EqualTo(-2146999358));

        var working = new TestWorkingBeatmap(source);
        var ruleset = new BmsRuleset().RulesetInfo;
        double calculate(bool hs) => new BmsDifficultyCalculator(ruleset, working)
            .Calculate(hs ? [new BmsModHideScratch()] : Array.Empty<Mod>()).StarRating;

        const double expected = 3.9649212423411786;
        Assert.That(calculate(false), Is.EqualTo(expected).Within(1e-12));
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Task.Run(() => calculate(i % 2 == 0))));
        Assert.That(results, Is.All.EqualTo(expected).Within(1e-12));
        Assert.That(source.HitObjects[0].StartTime, Is.EqualTo(-2146999358));
    }

    [TestCase(-1, false)]
    [TestCase(-100, false)]
    [TestCase(-100, true)]
    [TestCase(-2146999358, true)]
    public async Task ConvertedManiaWithNegativeFirstNoteCalculatesConsistently(int startTime, bool hold)
    {
        var text = $"""
                    osu file format v14

                    [General]
                    Mode:3

                    [Metadata]
                    Title:Negative start
                    Artist:Test
                    Creator:Test
                    Version:7K

                    [Difficulty]
                    CircleSize:7
                    OverallDifficulty:8

                    [TimingPoints]
                    0,500,4,1,0,100,1,0

                    [HitObjects]
                    36,192,{startTime},{(hold ? "128,0,240:0:0:0:0:" : "1,0,0:0:0:0:")}
                    109,192,150,1,0,0:0:0:0:
                    182,192,400,1,0,0:0:0:0:
                    256,192,600,1,0,0:0:0:0:
                    329,192,800,1,0,0:0:0:0:
                    402,192,1000,1,0,0:0:0:0:
                    """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var reader = new LineBufferedReader(stream);
        var source = new LegacyBeatmapDecoder().Decode(reader);
        // Official rulesets are not registered in the isolated ruleset test runner.
        source.BeatmapInfo.Ruleset = new RulesetInfo { OnlineID = 3, ShortName = "mania" };
        Assert.That(source.HitObjects[0].StartTime, Is.EqualTo(startTime));
        var working = new TestWorkingBeatmap(source);
        var ruleset = new BmsRuleset().RulesetInfo;

        double calculate(bool hs) => new BmsDifficultyCalculator(ruleset, working)
            .Calculate(hs ? [new BmsModHideScratch()] : Array.Empty<Mod>()).StarRating;

        var expected = calculate(false);
        Assert.That(double.IsFinite(expected), Is.True);
        Assert.That(expected, Is.GreaterThan(0));
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => calculate(i % 2 == 0))));
        Assert.That(results, Is.All.EqualTo(expected).Within(1e-12));
        Assert.That(source.HitObjects[0].StartTime, Is.EqualTo(startTime));
    }
}
