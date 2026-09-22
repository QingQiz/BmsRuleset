using NUnit.Framework;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal;

public class BmsRepackedAssemblyTest
{
    [TestCase("HarmonyLib.Harmony")]
    [TestCase("HarmonyLib.HarmonyMethod")]
    [TestCase("MonoMod.Utils.DynamicMethodDefinition")]
    public void RepackedRuntimeTypesKeepTheirNames(string typeName)
    {
        var type = typeof(BmsRuleset).Assembly.GetType(typeName);
        Assert.That(type, Is.Not.Null, "Runtime reflection and test probes must resolve the embedded patching library.");
    }
}
