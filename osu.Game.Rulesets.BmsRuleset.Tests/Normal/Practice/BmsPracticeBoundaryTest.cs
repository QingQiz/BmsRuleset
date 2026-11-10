using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.UI.Practice;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Practice;

[TestFixture]
public class BmsPracticeBoundaryTest
{
    [TestCase(130)]
    [TestCase(173)]
    [TestCase(180)]
    public void LegacyManiaGridBoundariesIncludeStartAndExcludeEnd(double bpm)
    {
        var source = new Beatmap
        {
            BeatmapInfo = new BeatmapInfo(new RulesetInfo { OnlineID = 3, ShortName = "mania" }, new BeatmapDifficulty { CircleSize = 7 }),
            BeatmapVersion = 14,
        };
        source.ControlPointInfo.Add(1000, new TimingControlPoint { BeatLength = 60000 / bpm });
        source.HitObjects = Enumerable.Range(0, 24).Select(index => (HitObject)new ManiaNote
        {
            StartTime = Math.Floor(1000 + index * (60000 / bpm) / 4),
        }).ToList();
        var beatmap = (BmsBeatmap)new BmsBeatmapConverter(source, new BmsRuleset()).Convert();
        var grid = BmsPracticeGrid.Create(beatmap.TimingMap).Lines;
        var checkedNotes = 0;
        foreach (var note in beatmap.HitObjects.Skip(1).SkipLast(8))
        {
            var boundary = grid.MinBy(line => Math.Abs(line.Time - note.StartTime)).Time;
            if (Math.Abs(boundary - note.StartTime) < 1e-7)
                continue;

            checkedNotes++;
            var fromNote = new BmsPracticeSettings { StartTime = boundary, EndTime = boundary + 1000 };
            var untilNote = new BmsPracticeSettings { StartTime = boundary - 1000, EndTime = boundary };
            Assert.Multiple(() =>
            {
                Assert.That(BmsPracticeBeatmap.Create(beatmap, fromNote).HitObjects.Select(selected => selected.StartTime), Does.Contain(note.StartTime),
                    $"Start {boundary:R}, note {note.StartTime:R}, difference {boundary - note.StartTime:R} ms");
                Assert.That(untilNote.Contains(note.StartTime, millisecondPrecision: true), Is.False,
                    $"End {boundary:R}, note {note.StartTime:R}, difference {boundary - note.StartTime:R} ms");
                Assert.That(BmsPracticeBeatmap.Create(beatmap, untilNote).HitObjects.Select(selected => selected.StartTime), Does.Not.Contain(note.StartTime));
                Assert.That(BmsPracticeBeatmap.SelectedCount(beatmap, fromNote), Is.EqualTo(BmsPracticeBeatmap.Create(beatmap, fromNote).HitObjects.Count));
            });
        }
        Assert.That(checkedNotes, Is.GreaterThan(0));
    }

    [Test]
    public void ManiaBoundarySelectionKeepsLongNoteCountsAndChartInSync()
    {
        var source = new Beatmap
        {
            BeatmapInfo = new BeatmapInfo(new RulesetInfo { OnlineID = 3, ShortName = "mania" }, new BeatmapDifficulty { CircleSize = 7 }),
            HitObjects = [new ManiaNote { StartTime = 1000 }, new ManiaHold { StartTime = 1083, Duration = 3000 }, new ManiaNote { StartTime = 1166 }],
        };
        source.ControlPointInfo.Add(1000, new TimingControlPoint { BeatLength = 60000d / 180 });
        var beatmap = (BmsBeatmap)new BmsBeatmapConverter(source, new BmsRuleset()).Convert();
        var grid = BmsPracticeGrid.Create(beatmap.TimingMap);
        var settings = new BmsPracticeSettings { StartTime = grid.Snap(1083), EndTime = grid.Snap(1166) };
        var practice = BmsPracticeBeatmap.Create(beatmap, settings);
        using var chart = new BmsPracticeChart();
        chart.SetBeatmap(beatmap, settings, true);

        Assert.Multiple(() =>
        {
            Assert.That(BmsPracticeBeatmap.SelectedCount(beatmap, settings), Is.EqualTo(1));
            Assert.That(practice.HitObjects, Has.Count.EqualTo(1));
            Assert.That(practice.HitObjects.Single().StartTime, Is.EqualTo(1083));
            Assert.That(((BmsLongNote)practice.HitObjects.Single()).EndTime, Is.EqualTo(4083));
            Assert.That(chart.SelectedEnd, Is.EqualTo(4083));
            Assert.That(beatmap.HitObjects.Select(note => note.StartTime), Is.EqualTo(source.HitObjects.Select(note => note.StartTime)));
        });
    }

    [Test]
    public void ManiaMillisecondCellsPreserveExactBoundariesAndFractionalNoteTimes()
    {
        var integerRange = new BmsPracticeSettings { StartTime = 1000, EndTime = 2000 };
        var fractionalRange = new BmsPracticeSettings { StartTime = 1000.5, EndTime = 2000.5 };
        Assert.Multiple(() =>
        {
            Assert.That(integerRange.Contains(999, millisecondPrecision: true), Is.False);
            Assert.That(integerRange.Contains(1000, millisecondPrecision: true), Is.True);
            Assert.That(integerRange.Contains(1999, millisecondPrecision: true), Is.True);
            Assert.That(integerRange.Contains(2000, millisecondPrecision: true), Is.False);
            Assert.That(fractionalRange.Contains(1000.25, millisecondPrecision: true), Is.False);
            Assert.That(fractionalRange.Contains(1000.5, millisecondPrecision: true), Is.True);
            Assert.That(fractionalRange.Contains(2000.25, millisecondPrecision: true), Is.True);
            Assert.That(fractionalRange.Contains(2000.5, millisecondPrecision: true), Is.False);
        });
    }

    private class ManiaNote : HitObject, IHasColumn
    {
        public int Column { get; set; } = 1;
    }

    private sealed class ManiaHold : ManiaNote, IHasDuration
    {
        public double Duration { get; set; }
        public double EndTime => StartTime + Duration;
    }

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
        foreach (var note in beatmap.HitObjects)
            note.Beatmap = beatmap;
        var settings = new BmsPracticeSettings { StartTime = 1000 + offset, EndTime = 2000 + offset };
        var practice = BmsPracticeBeatmap.Create(beatmap, settings);

        Assert.That(BmsPracticeBeatmap.SelectedCount(beatmap, settings), Is.EqualTo(4));
        Assert.That(practice.HitObjects.Select(note => note.Column), Is.EqualTo(new[] { 2, 3, 4, 5 }));
        Assert.That(practice.HitObjects.OfType<BmsLongNote>().Single().EndTime, Is.EqualTo(2500));
        Assert.That(practice.PracticeInitialKeySounds.Select(note => note.Column), Is.EqualTo(new[] { 1 }));
    }

    [TestCase(130)]
    [TestCase(173)]
    public void GridBoundariesIncludeStartAndExcludeEnd(double bpm)
    {
        var lines = new[] { $"#BPM {bpm}", "#BPM01 157", "#00108:01" }.Concat(Enumerable.Range(0, 20).Select(measure => $"#{measure:000}11:01010101010101010101010101010101")).ToArray();
        var parsed = BmsChartParser.Parse(lines);
        var grid = BmsPracticeGrid.Create(parsed.TimingMap).Lines;
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
        var grid = BmsPracticeGrid.Create(beatmap.TimingMap).Lines.Select(line => line.Time).Distinct().Order().ToArray();
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
