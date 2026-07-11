using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.UI.Components;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

public partial class BmsGameplayVirtualisationTest
{
    [Test]
    public void TestMeasureLineContainerClipsLinesToStageBounds()
    {
        Assert.That(new BmsMeasureLineContainer().Masking, Is.True);
    }
}
