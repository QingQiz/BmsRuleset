using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Screens;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics.Carousel;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;
using osu.Game.Screens.Select;
using Realms;

namespace osu.Game.Rulesets.BmsRuleset.Database;

internal static class BmsBeatmapNotificationPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.BeatmapNotifications";
    private static readonly object install_lock = new();
    private static readonly ConditionalWeakTable<RealmDetachedBeatmapStore, StoreState> stores = new();
    private static readonly ConditionalWeakTable<BeatmapCarousel, CarouselState> carousels = new();

    private static MethodInfo clearPendingOperations = null!;
    private static PropertyInfo storeRealm = null!;
    private static FieldInfo carouselItems = null!;
    private static MethodInfo filterCarousel = null!;
    private static PropertyInfo requestSelection = null!;
    private static PropertyInfo scheduler = null!;
    private static PropertyInfo isDisposed = null!;

    [ThreadStatic]
    private static bool publishingSnapshot;

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
                var storeType = typeof(RealmDetachedBeatmapStore);
                var carouselType = typeof(BeatmapCarousel);
                var changed = require(AccessTools.Method(storeType, "beatmapSetsChanged"));
                var update = require(AccessTools.Method(storeType, "Update"));
                var dispose = require(AccessTools.Method(storeType, "Dispose", [typeof(bool)]));
                var carouselChanged = require(AccessTools.Method(carouselType, "beatmapSetsChanged"));
                var itemsChanged = require(AccessTools.Method(carouselType, "HandleItemsChanged"));
                clearPendingOperations = require(AccessTools.Method(require(AccessTools.Field(storeType, "pendingOperations")).FieldType, "Clear"));
                storeRealm = require(AccessTools.Property(storeType, "realm"));
                require(AccessTools.Field(storeType, "loaded"));
                require(AccessTools.Field(storeType, "detachedBeatmapSets"));
                carouselItems = require(AccessTools.Field(typeof(Carousel<BeatmapInfo>), "Items"));
                filterCarousel = require(AccessTools.Method(carouselType, "FilterAsync"));
                requestSelection = require(AccessTools.Property(carouselType, nameof(BeatmapCarousel.RequestSelection)));
                scheduler = require(AccessTools.Property(typeof(Drawable), "Scheduler"));
                isDisposed = require(AccessTools.Property(typeof(Drawable), "IsDisposed"));

                patch(changed, nameof(storeChangedPrefix));
                patch(update, nameof(storeUpdatePrefix));
                patch(dispose, nameof(storeDisposePrefix));
                patch(carouselChanged, nameof(carouselChangedPrefix));
                patch(itemsChanged, nameof(itemsChangedPrefix));
                IsInstalled = true;

                void patch(MethodInfo target, string prefix) => harmony.Patch(target,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(BmsBeatmapNotificationPatcher), prefix)));
            }
            catch (Exception exception)
            {
                harmony.UnpatchAll(harmony_id);
                BmsLogger.Error(exception, "Failed to install BMS bulk beatmap notifications; keeping the original notification handling.");
            }
        }

        return;

        static T require<T>(T? member) where T : MemberInfo => member ?? throw new MissingMemberException("The osu! beatmap notification API has changed.");
    }

    // ReSharper disable InconsistentNaming
    private static bool storeChangedPrefix(RealmDetachedBeatmapStore __instance, ManualResetEventSlim ___loaded,
                                           IRealmCollection<BeatmapSetInfo> sender, ChangeSet? changes)
    {
        var state = stores.GetOrCreateValue(__instance);
        if (sender is RealmResetEmptySet<BeatmapSetInfo>)
        {
            state.Reset();
            return false;
        }

        var bulk = BmsBulkBeatmapUpdate.For((RealmAccess)storeRealm.GetValue(__instance)!).Read();
        if (changes == null)
        {
            state.Reset();
            state.MarkPublished(bulk.Generation);
            // Initial loading must unblock GetBeatmapSets even if a caller is waiting on the update thread.
            ___loaded.Reset();
            return true;
        }

        if (!state.Pending && !bulk.Active && state.Generation == bulk.Generation)
            return true;

        // The frozen collection preserves the exact version underlying the next ChangeSet's indices.
        // Once a callback is skipped, all later callbacks must join the snapshot until it is published.
        state.Capture(sender, bulk.Generation);
        return false;
    }

    private static bool storeUpdatePrefix(RealmDetachedBeatmapStore __instance,
                                          BindableList<BeatmapSetInfo> ___detachedBeatmapSets, ManualResetEventSlim ___loaded,
                                          object ___pendingOperations)
    {
        var realm = (RealmAccess)storeRealm.GetValue(__instance)!;
        var bulk = BmsBulkBeatmapUpdate.For(realm);
        if (bulk.Read().Active || !___loaded.IsSet)
            return false;

        if (!stores.TryGetValue(__instance, out var state) || !state.Pending)
            return true;

        // A new operation must not start between the final Refresh and publishing its predecessor's snapshot.
        bulk.RunWhenIdle(generation =>
        {
            realm.Realm.Refresh();
            if (!state.Pending || !state.TryTakeSnapshot(realm, out var snapshot))
                return;

            state.MarkPublished(generation);
            clearPendingOperations.Invoke(___pendingOperations, null);
            lock (___detachedBeatmapSets)
            {
                publishingSnapshot = true;
                try
                {
                    ___detachedBeatmapSets.ReplaceRange(0, ___detachedBeatmapSets.Count, snapshot);
                }
                finally
                {
                    publishingSnapshot = false;
                    ___loaded.Set();
                }
            }
        });

        return false;
    }

    private static void storeDisposePrefix(RealmDetachedBeatmapStore __instance)
    {
        if (stores.TryGetValue(__instance, out var state))
            state.Reset();
        stores.Remove(__instance);
    }

    private static bool carouselChangedPrefix(BeatmapCarousel __instance, object? beatmaps)
    {
        var state = carousels.GetOrCreateValue(__instance);
        if (!publishingSnapshot && state.Snapshot == null)
            return true;

        if (beatmaps is not IEnumerable<BeatmapSetInfo> sets)
            return true;

        var scheduled = state.Snapshot != null;
        // A suspended carousel may receive more changes before its scheduler runs; read its latest list just once on resume.
        state.Snapshot = sets;
        if (!scheduled)
            ((Scheduler)scheduler.GetValue(__instance)!).Add(() => applyCarouselSnapshot(__instance, state));
        return false;
    }

    private static bool itemsChangedPrefix(BeatmapCarousel __instance, ref bool __result)
    {
        if (!carousels.TryGetValue(__instance, out var state) || !state.Applying)
            return true;

        // The upstream Replace handler assumes equal-sized ranges and stops at the first unchanged chart.
        __result = true;
        return false;
    }
    // ReSharper restore InconsistentNaming

    private static void applyCarouselSnapshot(BeatmapCarousel carousel, CarouselState state)
    {
        var snapshot = state.Snapshot!;
        state.Snapshot = null;
        if ((bool)isDisposed.GetValue(carousel)!)
            return;

        var items = (BindableList<BeatmapInfo>)carouselItems.GetValue(carousel)!;
        var charts = snapshot.SelectMany(set => set.Beatmaps).ToArray();
        if (carousel is BmsBeatmapCarousel bmsCarousel)
            charts = bmsCarousel.IncludeUnavailableEntries(charts);
        var selected = carousel.CurrentGroupedBeatmap;
        var replacement = selected == null ? null : charts.FirstOrDefault(chart => chart.ID == selected.Beatmap.ID);

        state.Applying = true;
        try
        {
            items.ReplaceRange(0, items.Count, charts);
        }
        finally
        {
            state.Applying = false;
        }

        // Start filtering before resetting a removed selection so the screen cannot select from the old results.
        filterCarousel.Invoke(carousel, [false]);

        if (replacement != null && (selected!.Beatmap.DifficultyName != replacement.DifficultyName
                                    || selected.Beatmap.Hash != replacement.Hash || selected.Beatmap.Hidden != replacement.Hidden))
        {
            if (carousel is BmsBeatmapCarousel bms)
                bms.BeatmapMetadataUpdated?.Invoke(replacement);
            else
                ((Action<GroupedBeatmap>)requestSelection.GetValue(carousel)!).Invoke(new GroupedBeatmap(selected.Group, replacement));
        }
        else if (selected != null && replacement == null)
        {
            var next = findSelectionAfterRemoval(carousel, selected, charts);
            if (carousel is BmsBeatmapCarousel bms)
                bms.BeatmapSelectionRemoved?.Invoke(selected.Beatmap, next);
            else if (next != null)
                ((Action<GroupedBeatmap>)requestSelection.GetValue(carousel)!).Invoke(next);
            else if (carousel.FindClosestParent<SongSelect>() is { } screen && screen.IsCurrentScreen())
            {
                carousel.CurrentBeatmap = null;
                screen.Beatmap.SetDefault();
            }
        }
    }

    private static GroupedBeatmap? findSelectionAfterRemoval(BeatmapCarousel carousel, GroupedBeatmap selected, BeatmapInfo[] charts)
    {
        var previous = selected.Beatmap;
        var candidates = charts.Where(chart => !chart.Hidden && chart.Ruleset.Equals(previous.Ruleset)).ToArray();
        var sameSet = candidates.Where(chart =>
            chart.BeatmapSet != null
            && previous.BeatmapSet != null
            && (chart.BeatmapSet.ID == previous.BeatmapSet.ID
                || (previous.BeatmapSet.OnlineID > 0 && chart.BeatmapSet.OnlineID == previous.BeatmapSet.OnlineID))).ToArray();
        // Import-as-update may replace local GUIDs. Keep matching inside the original set before considering neighbours.
        var replacement = sameSet.FirstOrDefault(chart => previous.OnlineID > 0 && chart.OnlineID == previous.OnlineID)
                          ?? sameSet.FirstOrDefault(chart => !string.IsNullOrEmpty(previous.MD5Hash)
                                                             && string.Equals(chart.MD5Hash, previous.MD5Hash, StringComparison.OrdinalIgnoreCase))
                          ?? sameSet.FirstOrDefault(chart => chart.DifficultyName == previous.DifficultyName);
        if (replacement != null)
            return new GroupedBeatmap(selected.Group, replacement);

        // Preserve the previous display order, while excluding every removed or newly hidden chart in this batch.
        var available = charts.Where(chart => !chart.Hidden).ToDictionary(chart => chart.ID);
        var displayed = carousel.GetCarouselItems()?.Select(item => item.Model).OfType<GroupedBeatmap>().ToList();
        if (displayed == null)
            return null;

        var index = displayed.FindIndex(chart => chart.Equals(selected));
        for (var i = index + 1; i < displayed.Count; i++)
        {
            if (available.TryGetValue(displayed[i].Beatmap.ID, out var chart))
                return new GroupedBeatmap(displayed[i].Group, chart);
        }

        for (var i = index - 1; i >= 0; i--)
        {
            if (available.TryGetValue(displayed[i].Beatmap.ID, out var chart))
                return new GroupedBeatmap(displayed[i].Group, chart);
        }

        return null;
    }

    private sealed class CarouselState
    {
        internal IEnumerable<BeatmapSetInfo>? Snapshot;
        internal bool Applying;
    }

    private sealed class StoreState
    {
        internal bool Pending { get; private set; }

        internal long Generation { get; private set; }

        private long revision;
        private long taskRevision;
        private IRealmCollection<BeatmapSetInfo>? latest;
        private Task<BeatmapSetInfo[]?>? task;

        internal void Capture(IRealmCollection<BeatmapSetInfo> sender, long generation)
        {
            latest?.Realm.Dispose();
            latest = sender.Freeze();
            Generation = generation;
            revision++;
            Pending = true;
        }

        internal void Reset()
        {
            latest?.Realm.Dispose();
            latest = null;
            revision++;
            Pending = false;
            task = null;
        }

        internal void MarkPublished(long generation) => Generation = generation;

        internal bool TryTakeSnapshot(RealmAccess realm, out BeatmapSetInfo[] snapshot)
        {
            snapshot = [];
            if (task != null)
            {
                if (!task.IsCompleted)
                    return false;

                var result = task.GetAwaiter().GetResult();
                task = null;
                if (result != null && taskRevision == revision)
                {
                    snapshot = result;
                    Pending = false;
                    return true;
                }

                if (result == null && latest == null)
                    Capture((IRealmCollection<BeatmapSetInfo>)realm.Realm.All<BeatmapSetInfo>().Where(set => !set.DeletePending && !set.Protected), Generation);
            }

            if (latest == null)
                return false;

            var frozen = latest;
            latest = null;
            taskRevision = revision;
            task = Task.Run(() =>
            {
                try
                {
                    return realm.Run(_ => frozen.Detach().ToArray());
                }
                catch (Exception exception)
                {
                    BmsLogger.Error(exception, "Failed to detach the BMS beatmap library snapshot.");
                    return null;
                }
                finally
                {
                    frozen.Realm.Dispose();
                }
            });
            return false;
        }
    }
}
