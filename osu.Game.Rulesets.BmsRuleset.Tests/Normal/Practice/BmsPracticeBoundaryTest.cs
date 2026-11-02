using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Practice;

[TestFixture]
public class BmsPracticeBoundaryTest
{
    [TestCase(-1e-8)]
    [TestCase(0)]
    [TestCase(1e-8)]
    public void BoundaryNoiseKeepsCountsPlayableNotesAndInitialKeySoundsConsistent(double offset)
    {
        var beatmap = new BmsBeatmap
        {
            HitObjects =
            [
                new BmsNote { StartTime = 999.999, Column = 1, SampleKey = 1 },
                new BmsNote { StartTime = 1000, Column = 2, SampleKey = 2 },
                new BmsLongNote { StartTime = 1000, Column = 3, Duration = 1500 },
                new BmsNote { StartTime = 1000.001, Column = 4 },
                new BmsNote { StartTime = 1999.999, Column = 5 },
                new BmsNote { StartTime = 2000, Column = 6 },
                new BmsLongNote { StartTime = 2000, Column = 7, Duration = 1000 },
            ],
        };
        var settings = new BmsPracticeSettings { StartTime = 1000 + offset, EndTime = 2000 + offset };
        var practice = BmsPracticeBeatmap.Create(beatmap, settings);

        Assert.That(BmsPracticeBeatmap.SelectedCount(beatmap, settings), Is.EqualTo(4));
        Assert.That(practice.HitObjects.Select(note => note.Column), Is.EqualTo(new[] { 2, 3, 4, 5 }));
        Assert.That(practice.HitObjects.OfType<BmsLongNote>().Single().EndTime, Is.EqualTo(2500));
        Assert.That(practice.PracticeInitialKeySounds.Select(note => note.Column), Is.EqualTo(new[] { 1 }));
    }

    [TestCase(130)]
    [TestCase(173)]
    public void ShiftedGridBoundariesIncludeStartAndExcludeEnd(double bpm)
    {
        var lines = new[] { $"#BPM {bpm}", "#BPM01 157", "#00108:01" }.Concat(Enumerable.Range(0, 20).Select(measure => $"#{measure:000}11:01010101010101010101010101010101")).ToArray();
        var parsed = BmsChartParser.Parse(lines).ShiftedBy(2500);
        var grid = BmsPracticeGrid.Create(parsed.TimingMap);
        foreach (var note in parsed.HitObjects)
        {
            var boundary = grid.MinBy(line => Math.Abs(line.Time - note.StartTime)).Time;
            Assert.That(new BmsPracticeSettings { StartTime = boundary, EndTime = boundary + 1000 }.Contains(note.StartTime), Is.True,
                $"Start {boundary:R}, note {note.StartTime:R}, difference {boundary - note.StartTime:R} ms");
            Assert.That(new BmsPracticeSettings { StartTime = boundary - 1000, EndTime = boundary }.Contains(note.StartTime), Is.False,
                $"End {boundary:R}, note {note.StartTime:R}, difference {boundary - note.StartTime:R} ms");
        }
    }

    [TestCaseSource(nameof(chartPaths))]
    public void RealChartGridBoundariesIncludeStartAndExcludeEnd(string path)
    {
        var beatmap = (BmsBeatmap)new BmsBeatmapConverter(BmsBeatmapDecoder.DecodeBytes(File.ReadAllBytes(path), randomValueSelector: _ => 1), new BmsRuleset())
        {
            BranchRandomValueSelector = _ => 1,
        }.Convert();
        var grid = BmsPracticeGrid.Create(beatmap.TimingMap).Select(line => line.Time).Distinct().Order().ToArray();
        var checkedNotes = 0;
        foreach (var note in beatmap.HitObjects)
        {
            var index = Array.BinarySearch(grid, note.StartTime);
            index = index < 0 ? Math.Min(~index, grid.Length - 1) : index;
            if (index > 0 && Math.Abs(grid[index - 1] - note.StartTime) < Math.Abs(grid[index] - note.StartTime))
                index--;
            var boundary = grid[index];
            if (Math.Abs(boundary - note.StartTime) > 1e-6)
                continue;

            checkedNotes++;
            Assert.That(new BmsPracticeSettings { StartTime = boundary, EndTime = boundary + 1000 }.Contains(note.StartTime), Is.True,
                $"Start {boundary:R}, note {note.StartTime:R}, difference {boundary - note.StartTime:R} ms");
            Assert.That(new BmsPracticeSettings { StartTime = boundary - 1000, EndTime = boundary }.Contains(note.StartTime), Is.False,
                $"End {boundary:R}, note {note.StartTime:R}, difference {boundary - note.StartTime:R} ms");
        }
        Assert.That(checkedNotes, Is.GreaterThan(0));
    }

    private static string[] chartPaths() => Directory.GetFiles(BmsEmbeddedSongDecoderTest.TestSongsRoot, "*", SearchOption.AllDirectories)
        .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".bms" or ".bml" or ".bme" or ".pms").ToArray();
}
