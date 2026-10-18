using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Screens;
using osu.Framework.Threading;
using osu.Game.Overlays.SkinEditor;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect;
using osu.Game.Screens.Select;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.UI.Skinning;

internal static class BmsSkinEditorPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.SkinEditor";
    private static readonly object install_lock = new();

    private static FieldInfo? contentField;
    private static FieldInfo? selectedTargetField;
    private static PropertyInfo? schedulerProperty;

    internal static bool IsInstalled { get; private set; }

    internal static void InstallOnce()
    {
        lock (install_lock)
        {
            if (IsInstalled)
                return;

            var harmony = new Harmony(harmony_id);

            try
            {
                var previewAction = typeof(SkinEditorOverlay).GetNestedTypes(BindingFlags.NonPublic)
                    .SelectMany(type => AccessTools.GetDeclaredMethods(type))
                    .SingleOrDefault(method => method.Name.StartsWith("<presentGameplay>b__", StringComparison.Ordinal)
                                               && method.ReturnType == typeof(void)
                                               && method.GetParameters() is [{ ParameterType: var parameterType }] && parameterType == typeof(IScreen));
                var updateTarget = AccessTools.Method(typeof(SkinEditor), nameof(SkinEditor.UpdateTargetScreen), [typeof(Drawable)]);
                contentField = AccessTools.Field(typeof(SkinEditor), "content");
                selectedTargetField = AccessTools.Field(typeof(SkinEditor), "selectedTarget");
                schedulerProperty = AccessTools.Property(typeof(Drawable), "Scheduler");

                if (previewAction == null || updateTarget == null || contentField?.FieldType != typeof(Container)
                    || selectedTargetField?.FieldType != typeof(Bindable<GlobalSkinnableContainerLookup?>)
                    || schedulerProperty?.PropertyType != typeof(Scheduler))
                    throw new InvalidOperationException("Incompatible skin editor preview or target members.");

                harmony.Patch(previewAction, transpiler: new HarmonyMethod(typeof(BmsSkinEditorPatcher), nameof(previewTranspiler)));
                harmony.Patch(updateTarget,
                    transpiler: new HarmonyMethod(typeof(BmsSkinEditorPatcher), nameof(updateTargetTranspiler)),
                    postfix: new HarmonyMethod(typeof(BmsSkinEditorPatcher), nameof(updateTargetPostfix)));
                IsInstalled = true;
            }
            catch (Exception exception)
            {
                harmony.UnpatchAll(harmony_id);
                BmsLogger.Error(exception, "Failed to install BMS skin editor compatibility patch.");
            }
        }
    }

    private static IEnumerable<CodeInstruction> previewTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var replacements = 0;

        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Isinst && Equals(instruction.operand, typeof(SoloSongSelect)))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(BmsSkinEditorPatcher), nameof(asSoloSongSelect));
                replacements++;
            }

            yield return instruction;
        }

        if (replacements != 1)
            throw new InvalidOperationException($"Expected one skin editor song-select check, found {replacements}.");
    }

    // Resetting the ruleset on our song select synchronously replaces the screen used to push the preview.
    private static IScreen? asSoloSongSelect(IScreen screen) => screen is SoloSongSelect or BmsSoloSongSelect ? screen : null;

    private static IEnumerable<CodeInstruction> updateTargetTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var childGetter = AccessTools.PropertyGetter(typeof(Container<Drawable>), nameof(Container<Drawable>.Child));
        var replacements = 0;

        foreach (var instruction in instructions)
        {
            if (instruction.Calls(childGetter))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(BmsSkinEditorPatcher), nameof(getChildOrDefault));
                replacements++;
            }

            yield return instruction;
        }

        if (replacements != 1)
            throw new InvalidOperationException($"Expected one skin editor content access, found {replacements}.");
    }

    // Target changes can arrive again before the scheduled blueprint rebuild has filled the container.
    private static Drawable? getChildOrDefault(Container<Drawable> container) => container.Children.Count == 0 ? null : container.Child;

    // ReSharper disable once InconsistentNaming
    private static void updateTargetPostfix(SkinEditor __instance)
    {
        if (contentField?.GetValue(__instance) is Container { Children.Count: 0 }
            && schedulerProperty?.GetValue(__instance) is Scheduler scheduler)
            scheduler.AddOnce(refreshEmptyTarget, __instance);
    }

    private static void refreshEmptyTarget(SkinEditor editor)
    {
        // Keeping the same layer lookup doesn't trigger the upstream binding after its blueprints are cleared.
        if (contentField?.GetValue(editor) is Container { Children.Count: 0 }
            && selectedTargetField?.GetValue(editor) is Bindable<GlobalSkinnableContainerLookup?> selectedTarget)
            selectedTarget.TriggerChange();
    }
}
