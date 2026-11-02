using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.UI.Practice;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Practice;

[TestFixture]
public class BmsPracticeViewportTest
{
    [Test]
    public void ZoomKeepsPointerTimeStableWhenThereIsRoom()
    {
        var view = new BmsPracticeViewport();
        view.SetBounds(0, 120000, true);
        view.Set(30000, 60000);
        var pointerTime = view.Start + view.Duration * 0.25;
        view.Zoom(0.5, 0.25);
        Assert.Multiple(() =>
        {
            Assert.That(view.Start + view.Duration * 0.25, Is.EqualTo(pointerTime).Within(0.001));
            Assert.That(view.Duration, Is.EqualTo(30000));
        });
    }

    [Test]
    public void NavigationClampsAtBothChartEdges()
    {
        var view = new BmsPracticeViewport();
        view.SetBounds(-1000, 9000, true);
        view.Set(2000, 2000);
        view.Pan(-100000);
        Assert.That(view.Start, Is.EqualTo(-1000));
        view.Pan(100000);
        Assert.That(view.End, Is.EqualTo(9000));
        view.Zoom(1000);
        Assert.That((view.Start, view.End), Is.EqualTo((-1000d, 9000d)));
    }

    [Test]
    public void FitRangeIncludesContextAndRetainsWholeSelectedTail()
    {
        var view = new BmsPracticeViewport();
        view.SetBounds(0, 120000, true);
        view.Fit(10000, 28000);
        Assert.Multiple(() =>
        {
            Assert.That(view.Start, Is.LessThan(10000));
            Assert.That(view.End, Is.GreaterThan(28000));
            Assert.That(view.Duration, Is.LessThan(25000));
        });
    }

    [Test]
    public void ShortChartsStillHaveAValidZoomRange()
    {
        var view = new BmsPracticeViewport();
        view.SetBounds(0, 10, true);
        view.Zoom(0.0001);
        Assert.That((view.Start, view.End), Is.EqualTo((0d, 10d)));
    }
}
