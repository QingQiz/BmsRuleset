using System;
using System.Reflection;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

internal sealed class ScopedMethodProbe : IDisposable
{
    private readonly string id = "bms.tests.probe." + Guid.NewGuid();
    private readonly Type harmonyType;
    private readonly object harmony;

    public ScopedMethodProbe(MethodInfo target, MethodInfo prefix = null, MethodInfo postfix = null, MethodInfo finalizer = null)
    {
        // The release assembly embeds Harmony; use that same copy so test probes share its patch registry.
        harmonyType = typeof(BmsRuleset).Assembly.GetType("HarmonyLib.Harmony") ?? Type.GetType("HarmonyLib.Harmony, 0Harmony", true)!;
        var methodType = harmonyType.Assembly.GetType("HarmonyLib.HarmonyMethod")!;
        harmony = Activator.CreateInstance(harmonyType, id)!;
        object wrap(MethodInfo method) => method == null ? null : Activator.CreateInstance(methodType, method);
        harmonyType.GetMethod("Patch")!.Invoke(harmony, [target, wrap(prefix), wrap(postfix), null, wrap(finalizer)]);
    }

    public void Dispose() => harmonyType.GetMethod("UnpatchAll")!.Invoke(harmony, [id]);
}
