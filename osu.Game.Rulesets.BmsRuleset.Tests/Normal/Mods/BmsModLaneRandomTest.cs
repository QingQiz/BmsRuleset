using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Mods;

[TestFixture]
public class BmsModLaneRandomTest
{
    private const int total_columns = 8;

    /// <summary>
    /// Applies the mod's beatmap conversion and returns the result.
    /// </summary>
    private BmsBeatmap applyMod(BmsModLaneRandom mod, BmsBeatmap beatmap)
    {
        mod.ApplyToBeatmap(beatmap);
        return beatmap;
    }

    private BmsBeatmap createBeatmap(params int[] columns)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = total_columns,
        };

        for (var i = 0; i < columns.Length; i++)
        {
            beatmap.HitObjects.Add(new BmsHitObject
            {
                StartTime = 1000 + i * 125,
                Column = columns[i],
            });
        }

        return beatmap;
    }

    [Test]
    public void TestCustomLaneOrder()
    {
        var beatmap = createBeatmap(0, 1, 2, 3, 4, 5, 6, 7);
        var mod = new BmsModLaneRandom { IncludeScratch = { Value = true }, LaneOrder = { Value = "0,2,4,6,1,3,5,7" } };

        applyMod(mod, beatmap);

        Assert.That(beatmap.HitObjects.Select(h => h.Column), Is.EqualTo([0, 2, 4, 6, 1, 3, 5, 7]));
    }

    [Test]
    public void TestCustomLaneOrderIdentity()
    {
        var beatmap = createBeatmap(0, 2, 4, 6, 1, 3, 5, 7);
        var mod = new BmsModLaneRandom { IncludeScratch = { Value = true }, LaneOrder = { Value = "0,1,2,3,4,5,6,7" } };

        applyMod(mod, beatmap);

        Assert.That(beatmap.HitObjects.Select(h => h.Column), Is.EqualTo([0, 2, 4, 6, 1, 3, 5, 7]));
    }

    [Test]
    public void TestCustomLaneOrderClampsOutOfRange()
    {
        // Note at column 0 where the "99" entry sits, to exercise clamping.
        var beatmap = createBeatmap(0, 1, 3, 5);
        var mod = new BmsModLaneRandom { LaneOrder = { Value = "99,0,1,2,3,4,5,6" } };

        applyMod(mod, beatmap);

        // "99" maps column 0 → Math.Clamp(99, 0, 7) → 7.
        Assert.That(beatmap.HitObjects[0].Column, Is.EqualTo(7));
    }

    [Test]
    public void TestSeedDeterminism()
    {
        var beatmap1 = createBeatmap(0, 1, 2, 3, 4, 5, 6, 7);
        var beatmap2 = createBeatmap(0, 1, 2, 3, 4, 5, 6, 7);

        var mod1 = new BmsModLaneRandom { IncludeScratch = { Value = true }, Seed = { Value = 42 } };
        var mod2 = new BmsModLaneRandom { IncludeScratch = { Value = true }, Seed = { Value = 42 } };

        applyMod(mod1, beatmap1);
        applyMod(mod2, beatmap2);

        Assert.That(beatmap1.HitObjects.Select(h => h.Column),
            Is.EqualTo(beatmap2.HitObjects.Select(h => h.Column)));
    }

    [Test]
    public void TestDifferentSeedDifferentResult()
    {
        var beatmap1 = createBeatmap(0, 1, 2, 3, 4, 5, 6, 7);
        var beatmap2 = createBeatmap(0, 1, 2, 3, 4, 5, 6, 7);

        var mod1 = new BmsModLaneRandom { IncludeScratch = { Value = true }, Seed = { Value = 42 } };
        var mod2 = new BmsModLaneRandom { IncludeScratch = { Value = true }, Seed = { Value = 99 } };

        applyMod(mod1, beatmap1);
        applyMod(mod2, beatmap2);

        Assert.That(beatmap1.HitObjects.Select(h => h.Column),
            Is.Not.EqualTo(beatmap2.HitObjects.Select(h => h.Column)));
    }

    [Test]
    public void TestAllColumnsPreserved()
    {
        var inputColumns = new[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        var beatmap = createBeatmap(inputColumns);
        var mod = new BmsModLaneRandom { IncludeScratch = { Value = true }, LaneOrder = { Value = "0,3,1,4,2,5,7,6" } };

        applyMod(mod, beatmap);

        var outputColumns = beatmap.HitObjects.Select(h => h.Column).OrderBy(c => c).ToArray();
        Assert.That(outputColumns, Is.EqualTo(inputColumns));
    }

    [Test]
    public void TestScratchColumnsStayPutWhenIncludeScratchFalse()
    {
        // Column 0 is scratch for Bme7K.
        // Place notes on all columns so we can verify scratch wasn't moved.
        var beatmap = createBeatmap(0, 1, 2, 3, 4, 5, 6, 7);
        var mod = new BmsModLaneRandom { IncludeScratch = { Value = false }, Seed = { Value = 42 } };

        applyMod(mod, beatmap);

        // Scratch note (column 0) must still be at column 0.
        Assert.That(beatmap.HitObjects[0].Column, Is.EqualTo(0));
    }

    [Test]
    public void TestScratchColumnMovesWhenIncludeScratchTrue()
    {
        var beatmap = createBeatmap(0);
        var mod = new BmsModLaneRandom { IncludeScratch = { Value = true }, Seed = { Value = 42 } };

        applyMod(mod, beatmap);

        // With only 1 note at column 0 and scratch included, it should be shuffled somewhere.
        // Exact result depends on seed 42; just verify it's not stuck at 0.
        Assert.That(beatmap.HitObjects[0].Column, Is.Not.EqualTo(0));
    }

    [Test]
    public void TestModAcronym()
    {
        var mod = new BmsModLaneRandom();
        Assert.That(mod.Acronym, Is.EqualTo("LR"));
    }

    [Test]
    public void TestShuffleDefaultMode()
    {
        // Mode defaults to Shuffle.
        var beatmap = createBeatmap(0, 1, 2, 3, 4, 5, 6, 7);
        var mod = new BmsModLaneRandom { Seed = { Value = 42 } };

        applyMod(mod, beatmap);

        var output = beatmap.HitObjects.Select(h => h.Column).ToArray();
        Assert.That(output, Is.Not.EqualTo(Enumerable.Range(0, 8).ToArray()));
    }

    [Test]
    public void TestEveryColumnAppearsOnceWithLaneOrder()
    {
        // LaneOrder bypasses IncludeScratch and maps all columns directly.
        var beatmap = createBeatmap(0, 1, 2, 3, 4, 5, 6, 7);
        var mod = new BmsModLaneRandom { LaneOrder = { Value = "3,7,0,2,5,1,6,4" } };

        applyMod(mod, beatmap);

        var outputColumns = beatmap.HitObjects.Select(h => h.Column).OrderBy(c => c).ToArray();
        Assert.That(outputColumns, Is.EqualTo(Enumerable.Range(0, 8).ToArray()));
    }

    [Test]
    public void TestEveryColumnAppearsOnceWithRandomAndIncludeScratch([Random(1, 100, 10)] int seed)
    {
        var beatmap = createBeatmap(0, 1, 2, 3, 4, 5, 6, 7);
        var mod = new BmsModLaneRandom { IncludeScratch = { Value = true }, Seed = { Value = seed } };

        applyMod(mod, beatmap);

        var outputColumns = beatmap.HitObjects.Select(h => h.Column).OrderBy(c => c).ToArray();
        Assert.That(outputColumns, Is.EqualTo(Enumerable.Range(0, 8).ToArray()));
    }

    [Test]
    public void TestEveryNonScratchColumnAppearsOnceWithRandomExcludeScratch([Random(1, 100, 10)] int seed)
    {
        // IncludeScratch=false (default): scratch column 0 stays at 0,
        // non-scratch columns [1..7] are a bijection among themselves.
        var beatmap = createBeatmap(0, 1, 2, 3, 4, 5, 6, 7);
        var mod = new BmsModLaneRandom { Seed = { Value = seed } };

        applyMod(mod, beatmap);

        // Scratch note stays at 0.
        Assert.That(beatmap.HitObjects[0].Column, Is.EqualTo(0));

        // Non-scratch output must be exactly [1,2,3,4,5,6,7] in some order.
        var nonScratchColumns = beatmap.HitObjects.Skip(1).Select(h => h.Column).OrderBy(c => c).ToArray();
        Assert.That(nonScratchColumns, Is.EqualTo([1, 2, 3, 4, 5, 6, 7]));
    }
}
