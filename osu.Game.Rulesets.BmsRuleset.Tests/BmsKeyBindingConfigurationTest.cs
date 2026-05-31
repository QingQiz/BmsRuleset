using System.Linq;
using NUnit.Framework;
using osu.Framework.Input.Bindings;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public class BmsKeyBindingConfigurationTest
{
    [Test]
    public void TestDefaultKeyBindingsIncludeDoublePlayActions()
    {
        var bindings = BmsKeyBindingConfiguration.GetDefaultKeyBindings((int)BmsLayoutVariant.Bme7KDouble).ToArray();

        Assert.That(bindings.Select(b => b.Action), Is.SupersetOf(new object[]
        {
            BmsAction.P2Scratch,
            BmsAction.P2Key1,
            BmsAction.P2Key7,
        }));
        Assert.That(bindings.Single(b => (BmsAction)b.Action == BmsAction.P2Scratch).KeyCombination.Keys, Contains.Item(InputKey.RShift));
        Assert.That(bindings.Single(b => (BmsAction)b.Action == BmsAction.P2Key1).KeyCombination.Keys, Contains.Item(InputKey.Keypad1));
    }

    [Test]
    public void TestDefaultKeyBindingsIncludeScrollSpeedActions()
    {
        var bindings = BmsKeyBindingConfiguration.GetDefaultKeyBindings((int)BmsLayoutVariant.Bme7K).ToArray();

        Assert.That(bindings.Select(b => b.Action).Cast<BmsAction>(), Is.SupersetOf(new object[]
        {
            BmsAction.IncreaseScrollSpeed,
            BmsAction.DecreaseScrollSpeed,
        }));
    }

    [Test]
    public void TestDoublePlayActionsMapToSecondColumnBank()
    {
        Assert.That(BmsKeyBindingConfiguration.ActionToColumn(BmsAction.Scratch, BmsLayoutVariant.Bme7KDouble), Is.EqualTo(0));
        Assert.That(BmsKeyBindingConfiguration.ActionToColumn(BmsAction.Key7, BmsLayoutVariant.Bme7KDouble), Is.EqualTo(7));
        Assert.That(BmsKeyBindingConfiguration.ActionToColumn(BmsAction.P2Key1, BmsLayoutVariant.Bme7KDouble), Is.EqualTo(8));
        Assert.That(BmsKeyBindingConfiguration.ActionToColumn(BmsAction.P2Key7, BmsLayoutVariant.Bme7KDouble), Is.EqualTo(14));
        Assert.That(BmsKeyBindingConfiguration.ActionToColumn(BmsAction.P2Scratch, BmsLayoutVariant.Bme7KDouble), Is.EqualTo(15));
    }

    [Test]
    public void TestLayoutSpecificActionsDoNotReuseScratchMappings()
    {
        Assert.That(BmsKeyBindingConfiguration.ActionToColumn(BmsAction.Key5, BmsLayoutVariant.Bms5K), Is.EqualTo(5));
        Assert.That(BmsKeyBindingConfiguration.ActionToColumn(BmsAction.Key6, BmsLayoutVariant.Bms5K), Is.Null);
        Assert.That(BmsKeyBindingConfiguration.ActionToColumn(BmsAction.P2Scratch, BmsLayoutVariant.Bms5KDouble), Is.EqualTo(11));
        Assert.That(BmsKeyBindingConfiguration.ActionToColumn(BmsAction.PmsKey1, BmsLayoutVariant.Pms9K), Is.EqualTo(0));
        Assert.That(BmsKeyBindingConfiguration.ActionToColumn(BmsAction.Scratch, BmsLayoutVariant.Pms9K), Is.Null);
        Assert.That(BmsKeyBindingConfiguration.ActionToColumn(BmsAction.P2PmsKey9, BmsLayoutVariant.Pms9KDouble), Is.EqualTo(17));
    }
}
