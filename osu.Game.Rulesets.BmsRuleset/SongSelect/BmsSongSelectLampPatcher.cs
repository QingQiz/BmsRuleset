using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Game.Screens.Select;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

public static class BmsSongSelectLampPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.SongSelectLamp";

    private static readonly ConditionalWeakTable<PanelLocalRankDisplay, BmsLampDisplay> displays = new();

    private static bool disabled;

    private static PropertyInfo? rulesetProperty;
    private static FieldInfo? iconContainerField;
    private static FieldInfo? backgroundContainerField;
    private static MethodInfo? addInternalMethod;
    private static MethodInfo? removeInternalMethod;

    public static bool IsInstalled { get; private set; }

    public static void InstallOnce()
    {
        if (IsInstalled || disabled)
            return;

        try
        {
            var target = AccessTools.Method(typeof(PanelLocalRankDisplay), "setRankFromScore", [typeof(ScoreInfo)]);
            var postfixMethod = AccessTools.Method(typeof(BmsSongSelectLampPatcher), nameof(postfix));

            rulesetProperty = AccessTools.Property(typeof(PanelLocalRankDisplay), "ruleset");
            iconContainerField = AccessTools.Field(typeof(Panel), "iconContainer");
            backgroundContainerField = AccessTools.Field(typeof(Panel), "backgroundContainer");
            addInternalMethod = AccessTools.Method(typeof(CompositeDrawable), "AddInternal", [typeof(Drawable)]);
            removeInternalMethod = AccessTools.Method(typeof(CompositeDrawable), "RemoveInternal", [typeof(Drawable), typeof(bool)]);

            var missingMembers = new[]
            {
                (name: "PanelLocalRankDisplay.setRankFromScore", member: target),
                (name: "BmsSongSelectLampPatcher.postfix", member: postfixMethod),
                (name: "PanelLocalRankDisplay.ruleset", member: rulesetProperty),
                (name: "Panel.iconContainer", member: iconContainerField),
                (name: "Panel.backgroundContainer", member: backgroundContainerField),
                (name: "CompositeDrawable.AddInternal", member: addInternalMethod),
                (name: "CompositeDrawable.RemoveInternal", member: (MemberInfo?)removeInternalMethod),
            }.Where(m => m.member == null).Select(m => m.name).ToArray();

            if (missingMembers.Length > 0)
            {
                disable($"osu! song select local-rank internals no longer match the BMS lamp patch expectations. Missing: {string.Join(", ", missingMembers)}.");
                return;
            }

            new Harmony(harmony_id).Patch(target, postfix: new HarmonyMethod(postfixMethod));
            IsInstalled = true;
        }
        catch (Exception e)
        {
            disable("Failed to install the BMS song select lamp patch.", e);
        }
    }

    // ReSharper disable once InconsistentNaming
    private static void postfix(PanelLocalRankDisplay __instance, ScoreInfo? topScore)

    {
        try
        {
            if (disabled || !isBmsRuleset(__instance))
            {
                hideLamp(__instance, restoreRulesetMark: true);
                return;
            }

            var lamp = BmsLampCalculator.Calculate(topScore);

            var panel = findParentPanel(__instance);
            var iconContainer = panel != null ? getIconContainer(panel) : null;
            var lampHost = panel != null ? getLampHost(panel) : null;

            if (panel == null || iconContainer == null || lampHost == null)
            {
                hideLamp(__instance, restoreRulesetMark: false);
                return;
            }

            // The lamp IS the panel background colour. TopLevelContent's outer rounded corner and
            // Content's inner rounded corner mask the full-size lamp into the lens-shaped gap between
            // them, so the lamp never renders as a plain rectangle and never bleeds under the rank
            // icon or title. The ruleset mark icon is hidden so the slot shows the lamp; osu!'s rank
            // icon stays visible beside it.
            iconContainer.Alpha = 0;

            if (!displays.TryGetValue(__instance, out var display))
            {
                display = new BmsLampDisplay(lamp, showLabel: false, cornerRadius: 0, masking: false)
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    RelativeSizeAxes = Axes.Both,
                    Width = 1,
                    Height = 1,
                };

                addInternalMethod!.Invoke(lampHost, [display]);
                displays.Add(__instance, display);
            }
            else
            {
                display.Lamp = lamp;
            }

            display.Alpha = 1;
        }
        catch (Exception e)
        {
            Logger.Error(e, "BMS song select lamp patch failed during a rank update.");
            hideLamp(__instance, restoreRulesetMark: true);
        }
    }

    private static bool isBmsRuleset(PanelLocalRankDisplay display)
    {
        if (rulesetProperty?.GetValue(display) is not IBindable<RulesetInfo> ruleset)
            return false;

        return ruleset.Value.ShortName == "bms";
    }

    private static Drawable? getIconContainer(Panel panel)
        => iconContainerField?.GetValue(panel) as Drawable;

    private static CompositeDrawable? getLampHost(Panel panel)
        => backgroundContainerField?.GetValue(panel) as CompositeDrawable;

    private static Panel? findParentPanel(Drawable drawable)
    {
        for (Drawable? current = drawable; current != null; current = current.Parent)
        {
            if (current is Panel panel)
                return panel;
        }

        return null;
    }

    private static void hideLamp(PanelLocalRankDisplay display, bool restoreRulesetMark)
    {
        if (displays.TryGetValue(display, out var lampDisplay))
        {
            lampDisplay.Alpha = 0;

            if (lampDisplay.Parent is CompositeDrawable parent)
                removeInternalMethod?.Invoke(parent, [lampDisplay, true]);

            displays.Remove(display);
        }

        if (restoreRulesetMark && findParentPanel(display) is { } panel && getIconContainer(panel) is { } iconContainer)
            iconContainer.Alpha = 1;
    }

    private static void disable(string message, Exception? exception = null)
    {
        disabled = true;

        if (exception == null)
            Logger.Log(message, LoggingTarget.Runtime, LogLevel.Important);
        else
            Logger.Error(exception, message);
    }
}
