using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

public class BmsAliveDrawableIndexTest
{
    [Test]
    public void TestReturnsOnlyRequestedColumn()
    {
        var index = new BmsAliveDrawableIndex();
        var columnOne = new DrawableBmsHitObject();
        var columnTwo = new DrawableBmsHitObject();

        index.Add(new BmsHitObject { Column = 1 }, columnOne);
        index.Add(new BmsHitObject { Column = 2 }, columnTwo);

        Assert.That(index.GetColumn(1).ToArray(), Is.EqualTo(new[] { columnOne }));
    }

    [Test]
    public void TestRemoveOnlyRemovesMatchingDrawable()
    {
        var index = new BmsAliveDrawableIndex();
        var first = new DrawableBmsHitObject();
        var second = new DrawableBmsHitObject();
        var hitObject = new BmsHitObject { Column = 1 };

        index.Add(hitObject, first);
        index.Add(hitObject, second);
        index.Remove(first);

        Assert.That(index.GetColumn(1).ToArray(), Is.EqualTo(new[] { second }));
    }

    [Test]
    public void TestMovingPooledDrawableRemovesOldColumnEntry()
    {
        var index = new BmsAliveDrawableIndex();
        var drawable = new DrawableBmsHitObject();

        index.Add(new BmsHitObject { Column = 1 }, drawable);
        index.Remove(drawable);
        index.Add(new BmsHitObject { Column = 2 }, drawable);

        Assert.That(index.GetColumn(1), Is.Empty);
        Assert.That(index.GetColumn(2).ToArray(), Is.EqualTo(new[] { drawable }));
    }
}
