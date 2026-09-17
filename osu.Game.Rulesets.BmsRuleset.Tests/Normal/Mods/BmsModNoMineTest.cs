using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Tests.Beatmaps;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Mods;

[TestFixture]
public class BmsModNoMineTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void TestPlayableBeatmapRemovesMinesOnlyWhenEnabled(bool enabled)
    {
        var ruleset = new BmsRuleset();
        var mod = ruleset.GetModsFor(ModType.DifficultyReduction).OfType<BmsModNoMine>().Single();
        var beatmap = new BmsBeatmap
        {
            TotalColumns = 16,
            LayoutVariant = BmsLayoutVariant.Bme7KDouble,
            HitObjects =
            [
                new BmsNote { StartTime = 1000, Column = 0, SampleKey = 1 },
                new BmsLongNote { StartTime = 2000, Column = 9, Duration = 500, SampleKey = 2, TailSampleKey = 3 },
            ],
            BackgroundSampleEvents = [new BmsSampleEvent(500, 0, 4, 80)],
        };

        for (var column = 0; column < beatmap.TotalColumns; column++)
            beatmap.HitObjects.Add(new BmsLandmine { StartTime = 1500, Column = column, SampleKey = 0, LandmineDamagePercent = 20 });

        var working = new TestWorkingBeatmap(beatmap);
        var playable = (BmsBeatmap)working.GetPlayableBeatmap(ruleset.RulesetInfo, enabled ? [ruleset.CreateModFromAcronym(mod.Acronym)] : []);
        var note = playable.HitObjects.OfType<BmsNote>().Single();
        var longNote = playable.HitObjects.OfType<BmsLongNote>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(mod.Type, Is.EqualTo(ModType.DifficultyReduction));
            Assert.That(mod.Acronym, Is.EqualTo("NM"));
            Assert.That(playable.HitObjects.OfType<BmsLandmine>().Count(), Is.EqualTo(enabled ? 0 : 16));
            Assert.That((note.StartTime, note.Column, note.SampleKey), Is.EqualTo((1000d, 0, (ushort?)1)));
            Assert.That((longNote.StartTime, longNote.Column, longNote.Duration, longNote.SampleKey, longNote.TailSampleKey),
                Is.EqualTo((2000d, 9, 500d, (ushort?)2, (ushort?)3)));
            Assert.That(playable.BackgroundSampleEvents, Is.EqualTo(beatmap.BackgroundSampleEvents));
            Assert.That(beatmap.HitObjects.OfType<BmsLandmine>().Count(), Is.EqualTo(16));
        });
    }
}
