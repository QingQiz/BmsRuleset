using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Mods.LongNoteMode;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Mods;

public class BmsLongNoteModeModsTest
{
    [Test]
    public void TestChargeNoteModAppliesToUnlockedChart()
    {
        var beatmap = createBeatmap(BmsLongNoteMode.Undefined);

        new BmsModChargeNote().ApplyToBeatmap(beatmap);

        Assert.That(beatmap.HitObjects.OfType<BmsLongNote>().Single().Beatmap.LockedLongNoteMode, Is.EqualTo(BmsLongNoteMode.ChargeNote));
    }

    [Test]
    public void TestHellChargeNoteModAppliesToUnlockedChart()
    {
        var beatmap = createBeatmap(BmsLongNoteMode.Undefined);

        new BmsModHellChargeNote().ApplyToBeatmap(beatmap);

        Assert.That(beatmap.HitObjects.OfType<BmsLongNote>().Single().Beatmap.LockedLongNoteMode, Is.EqualTo(BmsLongNoteMode.HellChargeNote));
    }

    [Test]
    public void TestLongNoteModAppliesToUnlockedChart()
    {
        var beatmap = createBeatmap(BmsLongNoteMode.Undefined);

        new BmsModLongNote().ApplyToBeatmap(beatmap);

        Assert.That(beatmap.HitObjects.OfType<BmsLongNote>().Single().Beatmap.LockedLongNoteMode, Is.EqualTo(BmsLongNoteMode.LongNote));
    }

    [TestCase(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.LongNote, BmsLongNoteMode.HellChargeNote)]
    [TestCase(BmsLongNoteMode.ChargeNote, BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote, BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote, BmsLongNoteMode.ChargeNote)]
    public void TestModeModOverridesLockedMode(BmsLongNoteMode targetMode, BmsLongNoteMode lockedMode)
    {
        var beatmap = createBeatmap(lockedMode);

        BmsModLongNoteModeBase mod = targetMode switch
        {
            BmsLongNoteMode.LongNote => new BmsModLongNote(),
            BmsLongNoteMode.ChargeNote => new BmsModChargeNote(),
            BmsLongNoteMode.HellChargeNote => new BmsModHellChargeNote(),
            _ => throw new System.ArgumentOutOfRangeException(nameof(targetMode)),
        };

        mod.ApplyToBeatmap(beatmap);

        Assert.That(beatmap.HitObjects.OfType<BmsLongNote>().Single().Beatmap.LockedLongNoteMode, Is.EqualTo(targetMode));
    }

    [Test]
    public void TestModeModsAreMutuallyExclusive()
    {
        Assert.That(new BmsModLongNote().IncompatibleMods, Does.Contain(typeof(BmsModChargeNote)));
        Assert.That(new BmsModLongNote().IncompatibleMods, Does.Contain(typeof(BmsModHellChargeNote)));
        Assert.That(new BmsModChargeNote().IncompatibleMods, Does.Contain(typeof(BmsModLongNote)));
        Assert.That(new BmsModHellChargeNote().IncompatibleMods, Does.Contain(typeof(BmsModLongNote)));
    }

    private static BmsBeatmap createBeatmap(BmsLongNoteMode lockedMode)
    {
        var beatmap = new BmsBeatmap
        {
            LockedLongNoteMode = lockedMode,
            HitObjects =
            {
                new BmsLongNote
                {
                    StartTime = 1000,
                    Duration = 500,
                    Column = 1,
                },
            },
        };

        // Set beatmap reference so computed LongNoteMode delegates to beatmap's LockedLongNoteMode.
        foreach (var h in beatmap.HitObjects.OfType<BmsHitObject>())
            h.Beatmap = beatmap;

        return beatmap;
    }
}
