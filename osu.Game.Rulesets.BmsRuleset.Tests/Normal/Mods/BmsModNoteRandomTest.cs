using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Mods;

[TestFixture]
public class BmsModNoteRandomTest
{
    private const int total_columns = 8;

    private BmsBeatmap applyMod(BmsModNoteRandom mod, BmsBeatmap beatmap)
    {
        mod.ApplyToBeatmap(beatmap);
        return beatmap;
    }

    private BmsBeatmap createBeatmap(params (double time, int column)[] notes)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = total_columns,
        };

        foreach (var (time, col) in notes)
        {
            beatmap.HitObjects.Add(new BmsHitObject
            {
                StartTime = time,
                Column = col,
            });
        }

        return beatmap;
    }

    /// <summary>
    ///     Creates a simple beatmap with a known pattern for threshold testing.
    /// </summary>
    private BmsBeatmap createPatternBeatmap()
    {
        const double t = 1000;
        // A single column repeated at short intervals → should produce jacks without random.
        // After random, identical-source columns get spread.
        return createBeatmap(
            (t, 1), (t + 30, 1), (t + 60, 1), (t + 90, 1),
            (t + 2000, 3), (t + 2030, 3), (t + 2060, 3), (t + 2090, 3)
        );
    }

    [Test]
    public void TestChordsGetUniqueColumns()
    {
        // Three notes at the same time — each must get a different column.
        var beatmap = createBeatmap((1000, 0), (1000, 1), (1000, 2));
        var mod = new BmsModNoteRandom { Seed = { Value = 42 } };

        applyMod(mod, beatmap);

        var columns = beatmap.HitObjects.Where(h => h.StartTime == 1000).Select(h => h.Column).ToArray();
        Assert.That(columns.Distinct().Count(), Is.EqualTo(3));
    }

    [Test]
    public void TestChordsGetUniqueColumnsMultiSeed([Random(1, 100, 10)] int seed)
    {
        var beatmap = createBeatmap(
            (1000, 0), (1000, 1), (1000, 2),
            (2000, 3), (2000, 4), (2000, 5)
        );
        var mod = new BmsModNoteRandom { Seed = { Value = seed } };

        applyMod(mod, beatmap);

        var group1 = beatmap.HitObjects.Where(h => h.StartTime == 1000).Select(h => h.Column).ToArray();
        var group2 = beatmap.HitObjects.Where(h => h.StartTime == 2000).Select(h => h.Column).ToArray();

        Assert.That(group1.Distinct().Count(), Is.EqualTo(3), "Group 1 columns not unique");
        Assert.That(group2.Distinct().Count(), Is.EqualTo(3), "Group 2 columns not unique");
    }

    [Test]
    public void TestColumnsAreAlwaysValid([Random(1, 100, 10)] int seed)
    {
        // Every note must always end up on a column within [0, totalColumns).
        var beatmap = createBeatmap(
            (1000, 0), (1000, 1), (1000, 5),
            (2000, 7), (2000, 3),
            (3000, 2), (3000, 4), (3000, 6)
        );
        var mod = new BmsModNoteRandom { Seed = { Value = seed } };

        applyMod(mod, beatmap);

        foreach (var note in beatmap.HitObjects)
            Assert.That(note.Column, Is.InRange(0, 7), $"Column out of range at time {note.StartTime}");
    }

    [Test]
    public void TestDifferentSeedDifferentResult()
    {
        var beatmap1 = createBeatmap((1000, 0), (1000, 1), (2000, 2), (2000, 3));
        var beatmap2 = createBeatmap((1000, 0), (1000, 1), (2000, 2), (2000, 3));

        var mod1 = new BmsModNoteRandom { Seed = { Value = 42 } };
        var mod2 = new BmsModNoteRandom { Seed = { Value = 99 } };

        applyMod(mod1, beatmap1);
        applyMod(mod2, beatmap2);

        Assert.That(beatmap1.HitObjects.Select(h => h.Column),
            Is.Not.EqualTo(beatmap2.HitObjects.Select(h => h.Column)));
    }

    [Test]
    public void TestEachTimelineHasDistinctColumns()
    {
        // Two separate time groups, each with 3 notes → each group must have unique columns.
        var beatmap = createBeatmap(
            (1000, 0), (1000, 1), (1000, 2),
            (2000, 3), (2000, 4), (2000, 5)
        );
        var mod = new BmsModNoteRandom { Seed = { Value = 42 } };

        applyMod(mod, beatmap);

        var group1 = beatmap.HitObjects.Where(h => h.StartTime == 1000).Select(h => h.Column).ToArray();
        var group2 = beatmap.HitObjects.Where(h => h.StartTime == 2000).Select(h => h.Column).ToArray();

        Assert.That(group1.Distinct().Count(), Is.EqualTo(3));
        Assert.That(group2.Distinct().Count(), Is.EqualTo(3));
    }

    [Test]
    public void TestHRandomMode()
    {
        var beatmap = createPatternBeatmap();
        var mod = new BmsModNoteRandom { Mode = { Value = BmsNoteRandomMode.H_Random }, Seed = { Value = 42 } };

        applyMod(mod, beatmap);

        // H_Random should also have shuffled columns.
        Assert.That(beatmap.HitObjects.Select(h => h.Column).Distinct().Count(), Is.GreaterThan(1));
    }

    [Test]
    public void TestLongNoteBlocksSubsequentNotesOnSameColumn()
    {
        // An LN at time 1000 on a non-scratch column blocks that column until 1500.
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
        };

        beatmap.HitObjects.Add(new BmsLongNote { StartTime = 1000, Column = 3, Duration = 500 });
        beatmap.HitObjects.Add(new BmsHitObject { StartTime = 1100, Column = 1 });

        var mod = new BmsModNoteRandom { IncludeScratch = { Value = true }, Seed = { Value = 42 } };
        applyMod(mod, beatmap);

        var lnColumn = beatmap.HitObjects.Single(h => h is BmsLongNote).Column;
        var regularColumn = beatmap.HitObjects.Single(h => h is not BmsLongNote).Column;

        // The regular note at time 1100 (during the LN body) must NOT share the LN's column.
        Assert.That(regularColumn, Is.Not.EqualTo(lnColumn));
    }

    [Test]
    public void TestLongNoteColumnFreedAfterDuration()
    {
        // An LN at time 1000 with duration 200 ends at 1200.
        // A regular note at time 1300 (after LN ends) CAN use the LN's column.
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
        };

        beatmap.HitObjects.Add(new BmsLongNote { StartTime = 1000, Column = 0, Duration = 200 });
        // After the LN ends, add enough notes to force using every column.
        for (var i = 0; i < 8; i++)
            beatmap.HitObjects.Add(new BmsHitObject { StartTime = 1300, Column = 1 });

        var mod = new BmsModNoteRandom { IncludeScratch = { Value = true }, Seed = { Value = 42 } };
        applyMod(mod, beatmap);

        var lnColumn = beatmap.HitObjects.Single(h => h is BmsLongNote).Column;
        var postLnColumns = beatmap.HitObjects.Where(h => h.StartTime == 1300).Select(h => h.Column).ToArray();

        // With 8 notes at 1300 and 8 columns, the LN's column must be available again.
        Assert.That(postLnColumns, Does.Contain(lnColumn));
    }

    [Test]
    public void TestModAcronym()
    {
        var mod = new BmsModNoteRandom();
        Assert.That(mod.Acronym, Is.EqualTo("NR"));
    }

    [Test]
    public void TestNonScratchNotesNeverLandOnScratchWhenExcluded([Random(1, 100, 10)] int seed)
    {
        // With IncludeScratch=false (default), non-scratch notes must never get column 0.
        var beatmap = createBeatmap(
            (1000, 1), (1000, 2), (1000, 3),
            (2000, 4), (2000, 5), (2000, 6)
        );
        var mod = new BmsModNoteRandom { Seed = { Value = seed } };

        applyMod(mod, beatmap);

        foreach (var note in beatmap.HitObjects)
            Assert.That(note.Column, Is.Not.EqualTo(0), $"Non-scratch note landed on scratch at time {note.StartTime}");
    }

    [Test]
    public void TestSRandomMode()
    {
        var beatmap = createPatternBeatmap();
        var mod = new BmsModNoteRandom { Mode = { Value = BmsNoteRandomMode.S_Random }, Seed = { Value = 42 } };

        applyMod(mod, beatmap);

        // S_Random should have shuffled columns (not all at column 1 anymore).
        Assert.That(beatmap.HitObjects.Select(h => h.Column).Distinct().Count(), Is.GreaterThan(1));
    }

    [Test]
    public void TestScratchColumnWhenIncludeScratchTrue()
    {
        // With IncludeScratch=true, a note on scratch (0) can move to any column.
        var beatmap = createBeatmap((1000, 0), (2000, 1));
        var mod = new BmsModNoteRandom { IncludeScratch = { Value = true }, Seed = { Value = 42 } };

        applyMod(mod, beatmap);

        // At time 1000, the note was at column 0 but can now be at any column.
        var noteAtTime1000 = beatmap.HitObjects.Single(h => h.StartTime == 1000);
        Assert.That(noteAtTime1000.Column, Is.Not.EqualTo(0));
    }

    [Test]
    public void TestScratchIncludedWhenIncludeScratchTrue([Random(1, 100, 10)] int seed)
    {
        // With IncludeScratch=true, column 0 (scratch) must be used by at least one note
        // after shuffling notes across all 8 columns.
        var beatmap = createBeatmap(
            (1000, 0), (1000, 1), (1000, 2), (1000, 3),
            (1000, 4), (1000, 5), (1000, 6), (1000, 7)
        );
        var mod = new BmsModNoteRandom { IncludeScratch = { Value = true }, Seed = { Value = seed } };

        applyMod(mod, beatmap);

        var columns = beatmap.HitObjects.Select(h => h.Column).ToArray();
        Assert.That(columns, Does.Contain(0), "Scratch column should be used when IncludeScratch=true");
    }

    [Test]
    public void TestScratchNotesStayPutWhenIncludeScratchFalse()
    {
        var beatmap = createBeatmap((1000, 0), (1000, 1), (2000, 0), (2000, 3));
        var mod = new BmsModNoteRandom { IncludeScratch = { Value = false }, Seed = { Value = 42 } };

        applyMod(mod, beatmap);

        // Scratch notes (column 0) must stay at column 0.
        var scratchNotes = beatmap.HitObjects.Where(h => h.Column == 0).ToList();
        Assert.That(scratchNotes.Count, Is.EqualTo(2));
    }

    [Test]
    public void TestSeedAutoGeneratedOnApply()
    {
        var mod = new BmsModNoteRandom();
        var beatmap = createBeatmap((1000, 0));

        Assert.That(mod.Seed.Value, Is.Null);
        applyMod(mod, beatmap);
        Assert.That(mod.Seed.Value, Is.Not.Null);
    }

    [Test]
    public void TestSeedDeterminism()
    {
        var beatmap1 = createBeatmap((1000, 0), (1000, 1), (2000, 2), (2000, 3));
        var beatmap2 = createBeatmap((1000, 0), (1000, 1), (2000, 2), (2000, 3));

        var mod1 = new BmsModNoteRandom { Seed = { Value = 42 } };
        var mod2 = new BmsModNoteRandom { Seed = { Value = 42 } };

        applyMod(mod1, beatmap1);
        applyMod(mod2, beatmap2);

        Assert.That(beatmap1.HitObjects.Select(h => (h.StartTime, h.Column)),
            Is.EqualTo(beatmap2.HitObjects.Select(h => (h.StartTime, h.Column))));
    }
}
