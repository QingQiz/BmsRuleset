using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.BmsRuleset.UI;

internal static class BmsFrameStatisticsPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.FrameStatistics";

    private static readonly object install_lock = new();

    [ThreadStatic]
    private static int remainingFrames;

    private static MethodInfo? originalDequeue;
    private static MethodInfo? limitedDequeue;
    private static bool installed;

    internal static void InstallOnce()
    {
        lock (install_lock)
        {
            if (installed)
                return;

            var frameworkAssembly = typeof(osu.Framework.Game).Assembly;
            var displayType = frameworkAssembly.GetType("osu.Framework.Graphics.Performance.FrameStatisticsDisplay");
            var monitorType = frameworkAssembly.GetType("osu.Framework.Statistics.PerformanceMonitor");
            var frameType = frameworkAssembly.GetType("osu.Framework.Statistics.FrameStatistics");
            var update = displayType == null ? null : AccessTools.DeclaredMethod(displayType, "Update");
            var pendingFramesField = monitorType == null ? null : AccessTools.Field(monitorType, "PendingFrames");
            var tryDequeue = pendingFramesField?.FieldType.GetMethod("TryDequeue", [frameType?.MakeByRefType() ?? typeof(object).MakeByRefType()]);
            var prefixMethod = AccessTools.Method(typeof(BmsFrameStatisticsPatcher), nameof(prefix));
            var transpilerMethod = AccessTools.Method(typeof(BmsFrameStatisticsPatcher), nameof(transpiler));

            var missingMembers = new (string Name, MemberInfo? Member)[]
            {
                ("FrameStatisticsDisplay.Update", update),
                ("PerformanceMonitor.PendingFrames", pendingFramesField),
                ("PendingFrames.TryDequeue", tryDequeue),
                ("BmsFrameStatisticsPatcher.prefix", prefixMethod),
                ("BmsFrameStatisticsPatcher.transpiler", transpilerMethod),
            }.Where(member => member.Member == null).Select(member => member.Name).ToArray();

            if (frameType == null || missingMembers.Length > 0)
            {
                BmsLogger.Log(
                    "BMS frame statistics patch: incompatible framework members: "
                    + string.Join(", ", missingMembers),
                    LogLevel.Error);
                return;
            }

            try
            {
                originalDequeue = tryDequeue;
                limitedDequeue = AccessTools.Method(typeof(BmsFrameStatisticsPatcher), nameof(tryDequeueOnce))!.MakeGenericMethod(frameType);
                new Harmony(harmony_id).Patch(
                    update!,
                    prefix: new HarmonyMethod(prefixMethod),
                    transpiler: new HarmonyMethod(transpilerMethod));
                installed = true;
            }
            catch (Exception exception)
            {
                BmsLogger.Error(exception, "BMS frame statistics patch: failed to limit graph work per update.");
            }
        }
    }

    private static void prefix() => remainingFrames = 1;

    private static IEnumerable<CodeInstruction> transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var replacements = 0;

        foreach (var instruction in instructions)
        {
            if (instruction.Calls(originalDequeue))
            {
                instruction.opcode = System.Reflection.Emit.OpCodes.Call;
                instruction.operand = limitedDequeue;
                replacements++;
            }

            yield return instruction;
        }

        if (replacements != 1)
            throw new InvalidOperationException($"Expected one frame statistics dequeue call, found {replacements}.");
    }

    private static bool tryDequeueOnce<T>(ConcurrentQueue<T> queue, out T frame)
        where T : class
    {
        if (remainingFrames <= 0)
        {
            frame = null!;
            return false;
        }

        remainingFrames--;
        return queue.TryDequeue(out frame!);
    }
}
