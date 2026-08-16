using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Game.Screens.Footer;
using osu.Game.Screens.Select;
using OsuSongSelect = osu.Game.Screens.Select.SongSelect;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

public static class BmsCourseSongSelectPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.CourseSongSelect";

    private static readonly ConditionalWeakTable<SoloSongSelect, BmsCourseSongSelectController> controllers = new();

    private static MethodInfo? addInternalMethod;
    private static FieldInfo? wedgesContainerField;
    private static FieldInfo? titleWedgeField;
    private static FieldInfo? detailsAreaField;
    private static FieldInfo? carouselField;
    private static FieldInfo? noResultsPlaceholderField;
    private static MethodInfo? filterControlGetter;
    private static bool disabled;

    public static bool IsInstalled { get; private set; }

    public static void InstallOnce()
    {
        if (IsInstalled || disabled)
            return;

        try
        {
            var target = AccessTools.Method(typeof(OsuSongSelect), nameof(OsuSongSelect.CreateFooterButtons));
            var postfixMethod = AccessTools.Method(typeof(BmsCourseSongSelectPatcher), nameof(postfix));
            addInternalMethod = AccessTools.Method(typeof(CompositeDrawable), "AddInternal", [typeof(Drawable)]);
            wedgesContainerField = AccessTools.Field(typeof(OsuSongSelect), "wedgesContainer");
            titleWedgeField = AccessTools.Field(typeof(OsuSongSelect), "titleWedge");
            detailsAreaField = AccessTools.Field(typeof(OsuSongSelect), "detailsArea");
            carouselField = AccessTools.Field(typeof(OsuSongSelect), "carousel");
            noResultsPlaceholderField = AccessTools.Field(typeof(OsuSongSelect), "noResultsPlaceholder");
            filterControlGetter = AccessTools.PropertyGetter(typeof(OsuSongSelect), "FilterControl");

            if (target == null || postfixMethod == null || addInternalMethod == null || wedgesContainerField == null
                || titleWedgeField == null || detailsAreaField == null || carouselField == null
                || noResultsPlaceholderField == null || filterControlGetter == null)
            {
                disable("osu! song select internals no longer match the BMS course-mode patch expectations.");
                return;
            }

            new Harmony(harmony_id).Patch(target, postfix: new HarmonyMethod(postfixMethod));
            IsInstalled = true;
        }
        catch (Exception e)
        {
            disable("Failed to install the BMS course-mode song select patch.", e);
        }
    }

    // ReSharper disable InconsistentNaming
    private static void postfix(OsuSongSelect __instance, ref IReadOnlyList<ScreenFooterButton> __result)
    {
        if (disabled || __instance is not SoloSongSelect soloSongSelect)
            return;

        try
        {
            var controller = controllers.GetValue(soloSongSelect, createController);
            var randomButton = __result.OfType<FooterButtonRandom>().SingleOrDefault();
            controller.AttachRandomButton(randomButton);
            __result = [.. __result, new BmsCourseFooterButton(controller)];
        }
        catch (Exception e)
        {
            disable("Failed to attach the BMS course selector to song select.", e);
        }
    }
    // ReSharper restore InconsistentNaming

    private static BmsCourseSongSelectController createController(SoloSongSelect songSelect)
    {
        var controller = new BmsCourseSongSelectController(
            BmsRulesetRuntime.CourseCatalog,
            (FillFlowContainer)wedgesContainerField!.GetValue(songSelect)!,
            (BeatmapTitleWedge)titleWedgeField!.GetValue(songSelect)!,
            (BeatmapDetailsArea)detailsAreaField!.GetValue(songSelect)!,
            (FilterControl)filterControlGetter!.Invoke(songSelect, null)!,
            (BeatmapCarousel)carouselField!.GetValue(songSelect)!,
            (NoResultsPlaceholder)noResultsPlaceholderField!.GetValue(songSelect)!,
            songSelect.TopPadding)
        {
            Name = "BMS course selector controller",
        };

        addInternalMethod!.Invoke(songSelect, [controller]);
        return controller;
    }

    private static void disable(string message, Exception? exception = null)
    {
        disabled = true;

        if (exception == null)
            BmsLogger.Log(message, LogLevel.Important);
        else
            BmsLogger.Error(exception, message);
    }
}
