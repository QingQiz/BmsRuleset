using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Practice;

[TestFixture]
public class BmsPracticeDensityTest
{
    [Test]
    public void CountsHeadsInFixedRealTimeWindowsAndLeavesSilenceEmpty()
    {
        BmsHitObject[] notes =
        [
            new BmsNote { StartTime = -501 },
            new BmsNote { StartTime = -500 },
            new BmsNote { StartTime = 499 },
            new BmsNote { StartTime = 500 },
            new BmsLongNote { StartTime = 800, Duration = 3000 },
            new BmsLandmine { StartTime = 900 },
            new BmsInvisibleNote { StartTime = 900 },
            new BmsNote { StartTime = 2500 },
            new BmsNote { StartTime = 4500 },
        ];
        var density = new BmsPracticeDensity(notes, -500, 4500);
        Assert.Multiple(() =>
        {
            Assert.That(density.Windows, Has.Count.EqualTo(3));
            Assert.That(density.GetWindow(-500), Is.EqualTo(new BmsPracticeDensityWindow(-500, 500, 2)));
            Assert.That(density.GetWindow(499).NotesPerSecond, Is.EqualTo(2));
            Assert.That(density.GetWindow(500).NotesPerSecond, Is.EqualTo(2));
            Assert.That(density.GetWindow(1500).NotesPerSecond, Is.Zero);
            Assert.That(density.GetWindow(2500).NotesPerSecond, Is.EqualTo(1));
            Assert.That(density.GetWindow(3800).NotesPerSecond, Is.Zero);
            Assert.That(density.MaximumNotesPerSecond, Is.EqualTo(2));
        });
    }

    [Test]
    public void EmptyChartAndShortFinalWindowHaveFiniteDensity()
    {
        var empty = new BmsPracticeDensity([new BmsLandmine { StartTime = 0 }], 0, 1);
        var shortChart = new BmsPracticeDensity([new BmsNote { StartTime = 0 }], 0, 1);
        Assert.Multiple(() =>
        {
            Assert.That(empty.Windows, Is.Empty);
            Assert.That(empty.MaximumNotesPerSecond, Is.Zero);
            Assert.That(empty.GetWindow(1).NotesPerSecond, Is.Zero);
            Assert.That(shortChart.MaximumNotesPerSecond, Is.EqualTo(1));
            Assert.That(shortChart.GetWindow(1), Is.EqualTo(new BmsPracticeDensityWindow(0, 1000, 1)));
        });
    }

    [Test]
    public void LongSilentSectionsDoNotAllocateEmptyWindowsAndLookupClampsAtEdges()
    {
        var density = new BmsPracticeDensity([new BmsNote { StartTime = 0 }, new BmsNote { StartTime = 1_000_000_000 }], 0, 1_000_001_000);
        Assert.Multiple(() =>
        {
            Assert.That(density.Windows, Has.Count.EqualTo(2));
            Assert.That(density.GetWindow(-100).StartTime, Is.Zero);
            Assert.That(density.GetWindow(500_000_000).NotesPerSecond, Is.Zero);
            Assert.That(density.GetWindow(double.PositiveInfinity).NotesPerSecond, Is.EqualTo(1));
            Assert.That(density.GetWindow(2_000_000_000).StartTime, Is.EqualTo(1_000_000_000));
        });
    }
}
