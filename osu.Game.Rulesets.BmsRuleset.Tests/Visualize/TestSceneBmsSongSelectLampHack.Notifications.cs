#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Screens;
using osu.Framework.Statistics;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Graphics.Carousel;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.IO.Import;
using osu.Game.Rulesets.BmsRuleset.Database;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsSongSelectLampHack
{
    [TestCase(false)]
    [TestCase(true)]
    public void TestResumeAppliesPlayedMetadataWithoutUpdateThreadQueries(bool sortByLastPlayed)
    {
        ControlledSongSelect controlled = null!;
        BeatmapInfo selected = null!;
        var playedAt = DateTimeOffset.UtcNow;
        BindableList<BeatmapInfo> items = null!;
        var applied = false;
        var readsAtResume = 0;
        var readsAtReplacement = 0;

        importLampBeatmapSet();
        if (sortByLastPlayed)
        {
            AddStep("group and sort by last played", () =>
            {
                // Grouping by sets orders their difficulties separately from the set's last-played time.
                config.SetValue(OsuSetting.SongSelectSortingMode, SortMode.LastPlayed);
                config.SetValue(OsuSetting.SongSelectGroupMode, GroupMode.LastPlayed);
            });
        }

        loadSongSelect(() => controlled = new ControlledSongSelect());
        AddStep("suspend before recording gameplay", () =>
        {
            selected = controlled.Beatmap.Value.BeatmapInfo;
            items = (BindableList<BeatmapInfo>)carousel_items_field.GetValue(carousel)!;
            items.CollectionChanged += (_, change) =>
            {
                if (change.NewItems?.OfType<BeatmapInfo>().Any(beatmap => beatmap.ID == selected.ID && beatmap.LastPlayed == playedAt) != true)
                    return;

                applied = true;
                readsAtReplacement = GlobalStatistics.Get<int>("Realm", "Reads (Update)").Value;
            };
            controlled.Push(new ResumeTestScreen());
        });
        AddUntilStep("child loaded", () => Stack.CurrentScreen is ResumeTestScreen { IsLoaded: true });
        AddStep("record last played", () => Realm.Write(r => r.Find<BeatmapInfo>(selected.ID)!.LastPlayed = playedAt));
        AddUntilStep("detached store has playback history", () => beatmapStore.GetBeatmapSets(null)
            .SelectMany(set => set.Beatmaps).Any(beatmap => beatmap.ID == selected.ID && beatmap.LastPlayed == playedAt));
        AddStep("resume with validation held pending", () =>
        {
            controlled.DelayLoads = true;
            Stack.Exit();
            readsAtResume = GlobalStatistics.Get<int>("Realm", "Reads (Update)").Value;
        });
        AddUntilStep("carousel receives playback history", () => applied);
        AddAssert("notification does not query Realm on update thread", () => readsAtReplacement, () => Is.EqualTo(readsAtResume));
        AddAssert("resume validation is still pending", () => controlled.Requests.Count == 1 && !controlled.Requests[0].Token.IsCancellationRequested);
        AddStep("finish resume validation", () => controlled.Requests[0].Completion.SetResult(beatmaps.GetWorkingBeatmap(selected, true)));
        AddUntilStep("refreshed selection receives playback history", () => controlled.Beatmap.Value.BeatmapInfo.LastPlayed == playedAt);
        AddUntilStep("carousel has settled", () => !controlled.IsFiltering);
        AddAssert("selected chart retained", () => carousel.CurrentBeatmap?.ID, () => Is.EqualTo(selected.ID));
        if (sortByLastPlayed)
            AddAssert("played chart sorts first", () => carousel.GetCarouselItems()!.Select(item => item.Model).OfType<GroupedBeatmap>().First().Beatmap.ID,
                () => Is.EqualTo(selected.ID));
    }

    private const int storm_charts_per_set = 3;

    // A final snapshot may require a clear and an add; its notification count must not grow with the library.
    private const int bulk_notification_budget = 2;

    private NotificationStormProbe? notificationStormProbe;
    private IDisposable? heldBulkUpdate;
    private Action? restoreNotificationTables;

    [TearDown]
    public void TearDownNotificationStormProbe()
    {
        // Failed assertion steps skip the remaining visual steps, but must still release subscriptions.
        notificationStormProbe?.Dispose();
        notificationStormProbe = null;
        heldBulkUpdate?.Dispose();
        heldBulkUpdate = null;
        restoreNotificationTables?.Invoke();
        restoreNotificationTables = null;
    }

    public override void TearDownSteps()
    {
        AddStep("stop notification observation", () =>
        {
            notificationStormProbe?.Dispose();
            notificationStormProbe = null;
            heldBulkUpdate?.Dispose();
            heldBulkUpdate = null;
            restoreNotificationTables?.Invoke();
            restoreNotificationTables = null;
        });
        base.TearDownSteps();
    }

    [TestCase(16)]
    [TestCase(64)]
    public void TestBulkDifficultyMarkerRefreshCoalescesSongSelectNotifications(int setCount)
    {
        var directory = string.Empty;
        Task import = null!;
        Task refresh = null!;
        DifficultyNameUpdater updater = null!;
        Guid[] originalIds = [];
        Guid selectedId = Guid.Empty;

        importLampBeatmapSet();
        AddStep("import offline BMS fixture", () =>
        {
            directory = createNotificationStormCharts(setCount);
            import = new BmsFileImporter(Realm, LocalStorage).Import(directory);
        });
        waitForNotificationStormWork("fixture import", () => import);
        loadSongSelect();
        AddStep("select an offline chart which will be renamed", () =>
        {
            var chart = Realm.Run(r => r.All<BeatmapInfo>().AsEnumerable().First(b => b.Metadata.Source.StartsWith(directory, StringComparison.Ordinal)).Detach());
            selectedId = chart.ID;
            songSelect.ScopeToBeatmapSet(chart.BeatmapSet!);
            songSelect.LoadBeatmapSelection(chart);
        });
        AddUntilStep("offline selection loaded", () => songSelect.Beatmap.Value.BeatmapInfo.ID == selectedId && !songSelect.IsFiltering);
        observeNotificationStorm(() => directory);
        AddStep("prepare difficulty table for every fixture chart", () =>
        {
            var charts = notificationStormProbe!.Charts;
            Assert.That(charts, Has.Length.EqualTo(setCount * storm_charts_per_set));
            Assert.That(charts.All(chart => chart.OnlineID <= 0), Is.True,
                "The reproduction needs offline charts, since online IDs hide the rename matching failure.");
            originalIds = charts.Select(chart => chart.ID).ToArray();

            var store = new DifficultyTableStore(null, Path.Combine(directory, "table-cache"));
            // Restore only populates the index, so exactly one real refresh is responsible for the measured changes.
            store.RestoreTable(new DifficultyTable.DifficultyTable
            {
                Name = "Notification storm",
                Symbol = "ST",
                Entries = charts.Select(chart => new TableEntry { Md5Hash = chart.MD5Hash, Level = "1" }).ToList(),
            });
            updater = new DifficultyNameUpdater(Realm, store);
        });
        AddStep("refresh all difficulty markers in background", () => refresh = Task.Run(() => updater.RefreshAllMarkers()));
        waitForNotificationStormWork("marker refresh", () => refresh);
        AddUntilStep("all carousel models receive markers", () => notificationStormProbe!.Charts is var charts
                                                                  && charts.Length == originalIds.Length && charts.All(chart => chart.DifficultyName.EndsWith(" [ST1]", StringComparison.Ordinal)));
        AddUntilStep("carousel finishes filtering", () => !songSelect.IsFiltering);
        AddUntilStep("selected chart receives the new name", () => songSelect.Beatmap.Value.BeatmapInfo.ID == selectedId
                                                                   && songSelect.Beatmap.Value.BeatmapInfo.DifficultyName.EndsWith(" [ST1]", StringComparison.Ordinal));
        AddStep("verify data and notification budget", () =>
        {
            var probe = notificationStormProbe!;
            Assert.That(probe.Charts.Select(chart => chart.ID), Is.EquivalentTo(originalIds));
            TestContext.Out.WriteLine($"Marker refresh ({setCount} sets, {originalIds.Length} charts): {probe}");

            Assert.Multiple(() =>
            {
                Assert.That(probe.Detached.Events, Is.LessThanOrEqualTo(bulk_notification_budget),
                    $"One bulk marker refresh must not fan out into one detached-list event per set. {probe}");
                Assert.That(probe.Carousel.Events, Is.LessThanOrEqualTo(bulk_notification_budget),
                    $"Song select must consume the final bulk state without replaying every chart change. {probe}");
                Assert.That(probe.Carousel.SingleItemRemovals, Is.Zero,
                    "Changing difficulty markers must not delete and re-add charts one at a time.");
            });
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TestBulkNotificationScopeResumesAfterNestedOperation(bool cancel)
    {
        Task work = null!;
        var directory = string.Empty;
        Guid chartId = Guid.Empty;

        importLampBeatmapSet();
        loadSongSelect();
        AddStep("create fixture and hold outer bulk scope", () =>
        {
            directory = createNotificationStormCharts(16);
            heldBulkUpdate = BmsBulkBeatmapUpdate.Begin(Realm);
        });
        observeNotificationStorm(() => directory);
        AddStep("import inside outer bulk scope", () => work = new BmsFileImporter(Realm, LocalStorage).Import(directory));
        waitForNotificationStormWork("nested import", () => work);
        AddWaitStep("allow queued callbacks while outer scope remains active", 3);
        AddStep("nested completion does not publish partial state", () =>
        {
            Assert.That(notificationStormProbe!.RealmChangedSets, Is.EqualTo(16));
            Assert.That(notificationStormProbe.Detached.Events, Is.Zero);
            Assert.That(notificationStormProbe.Carousel.Events, Is.Zero);
        });
        AddStep("close outer scope, including cancellation unwinding", () =>
        {
            try
            {
                using var scope = heldBulkUpdate;
                heldBulkUpdate = null;
                if (cancel)
                    throw new OperationCanceledException();
            }
            catch (OperationCanceledException)
            {
            }
        });
        AddUntilStep("committed charts become visible", () => notificationStormProbe!.Charts.Length == 16 * storm_charts_per_set);
        AddStep("one final snapshot was published", () =>
        {
            Assert.That(notificationStormProbe!.Detached.Events, Is.EqualTo(1));
            Assert.That(notificationStormProbe.Carousel.Events, Is.EqualTo(1));
            chartId = notificationStormProbe.Charts[0].ID;
        });
        AddStep("perform a normal write after the bulk scope", () => Realm.Write(r => r.Find<BeatmapInfo>(chartId)!.Hidden = true));
        AddUntilStep("normal incremental notifications still work", () => notificationStormProbe!.Charts.Single(chart => chart.ID == chartId).Hidden);
    }

    [Test]
    public void TestDifficultyMarkerRefreshWithoutChangesDoesNotNotifySongSelect()
    {
        const int set_count = 16;
        var directory = string.Empty;
        Task work = null!;

        importLampBeatmapSet();
        AddStep("seed offline beatmap metadata", () =>
        {
            // The no-op refresh only reads metadata. Preparing it directly keeps chart parsing,
            // star calculation and file import outside this notification test's setup deadline.
            directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"unchanged-markers-{Guid.NewGuid():N}");
            Realm.Write(r =>
            {
                var ruleset = r.Find<RulesetInfo>(Constant.SHORT_NAME)!;
                for (var setIndex = 0; setIndex < set_count; setIndex++)
                {
                    var set = new BeatmapSetInfo { OnlineID = -1, Hash = Guid.NewGuid().ToString("N") };
                    for (var chartIndex = 0; chartIndex < storm_charts_per_set; chartIndex++)
                    {
                        var hash = Guid.NewGuid().ToString("N");
                        set.Beatmaps.Add(new BeatmapInfo(ruleset)
                        {
                            OnlineID = -1,
                            BeatmapSet = set,
                            Hash = hash,
                            MD5Hash = hash,
                            DifficultyName = $"Difficulty {chartIndex}",
                            Metadata = new BeatmapMetadata
                            {
                                Title = $"Unchanged markers {setIndex}",
                                Source = Path.Combine(directory, $"set-{setIndex:D3}"),
                            },
                        });
                    }
                    r.Add(set);
                }
            });
        });
        loadSongSelect();
        observeNotificationStorm(() => directory);
        AddStep("refresh unchanged difficulty names", () =>
        {
            Assert.That(notificationStormProbe!.Charts, Has.Length.EqualTo(set_count * storm_charts_per_set));
            var store = new DifficultyTableStore(null, Path.Combine(directory, "empty-table-cache"));
            work = Task.Run(() => new DifficultyNameUpdater(Realm, store).RefreshAllMarkers());
        });
        waitForNotificationStormWork("unchanged refresh", () => work);
        AddWaitStep("allow Realm callbacks and scheduled carousel changes", 3);
        AddStep("unchanged data emits no notifications", () =>
        {
            var probe = notificationStormProbe!;
            TestContext.Out.WriteLine($"Unchanged refresh: {probe}");
            Assert.Multiple(() =>
            {
                Assert.That(probe.RealmCallbacks, Is.Zero);
                Assert.That(probe.Detached.Events, Is.Zero);
                Assert.That(probe.Carousel.Events, Is.Zero);
            });
        });
    }

    [Test]
    public void TestBulkImportCoalescesSongSelectNotifications()
    {
        const int set_count = 64;
        var directory = string.Empty;
        Task import = null!;

        importLampBeatmapSet();
        loadSongSelect();
        AddStep("create offline BMS fixture", () => directory = createNotificationStormCharts(set_count));
        observeNotificationStorm(() => directory);
        AddStep("import all directories while song select is active", () =>
            import = new BmsFileImporter(Realm, LocalStorage).Import(directory));
        waitForNotificationStormWork("bulk import", () => import);
        AddUntilStep("all imported charts reach carousel", () => notificationStormProbe!.Charts.Length == set_count * storm_charts_per_set);
        AddUntilStep("carousel finishes filtering", () => !songSelect.IsFiltering);
        AddStep("verify data and notification budget", () =>
        {
            var probe = notificationStormProbe!;
            Assert.That(probe.Charts.Select(chart => chart.BeatmapSet!.ID).Distinct().Count(), Is.EqualTo(set_count));
            TestContext.Out.WriteLine($"Import ({set_count} sets, {set_count * storm_charts_per_set} charts): {probe}");

            Assert.Multiple(() =>
            {
                Assert.That(probe.Detached.Events, Is.LessThanOrEqualTo(bulk_notification_budget),
                    $"One bulk import must publish a bounded number of detached-list notifications. {probe}");
                Assert.That(probe.Carousel.Events, Is.LessThanOrEqualTo(bulk_notification_budget),
                    $"Song select must add imported charts in bulk. {probe}");
            });
        });
    }

    private void observeNotificationStorm(Func<string> getDirectory)
    {
        AddStep("observe real Realm, detached store and carousel", () =>
        {
            notificationStormProbe?.Dispose();
            notificationStormProbe = new NotificationStormProbe(Realm, beatmapStore,
                (BindableList<BeatmapInfo>)carousel_items_field.GetValue(carousel)!, getDirectory(), () => Time.Current);
        });
        AddUntilStep("Realm observer receives initial state", () => notificationStormProbe!.Ready);
    }

    [Test]
    public void TestBulkImportReconcilesUnavailableTableEntries()
    {
        const string missing_hash = "ffffffffffffffffffffffffffffffff";
        var directory = string.Empty;
        var importedHash = string.Empty;
        Guid missingId = Guid.Empty;
        Task import = null!;

        importLampBeatmapSet();
        AddStep("prepare one importable and one missing table entry", () =>
        {
            directory = createNotificationStormCharts(1);
            importedHash = Convert.ToHexString(MD5.HashData(File.ReadAllBytes(Path.Combine(directory, "set-000", "chart-0.bms")))).ToLowerInvariant();
            var previous = BmsRulesetRuntime.DifficultyTableStore;
            restoreNotificationTables = () => BmsRulesetRuntime.DifficultyTableStore = previous;
            var store = new DifficultyTableStore(null, Path.Combine(directory, "tables"));
            store.RestoreTable(new DifficultyTable.DifficultyTable
            {
                Name = "Missing charts", Symbol = "ST",
                Entries = [new TableEntry { Md5Hash = importedHash, Level = "1" }, new TableEntry { Md5Hash = missing_hash, Level = "2" }],
            });
            BmsRulesetRuntime.DifficultyTableStore = store;
        });
        loadSongSelect();
        AddStep("remember missing chart identity", () =>
        {
            var items = (BindableList<BeatmapInfo>)carousel_items_field.GetValue(carousel)!;
            Assert.That(items.Count(UnavailableTableBeatmapFactory.IsUnavailable), Is.EqualTo(2));
            missingId = items.Single(chart => chart.MD5Hash == missing_hash).ID;
        });
        AddStep("import the available chart", () => import = new BmsFileImporter(Realm, LocalStorage).Import(directory));
        waitForNotificationStormWork("fixture import", () => import);
        AddUntilStep("placeholder is replaced by the installed chart", () =>
        {
            var matches = ((BindableList<BeatmapInfo>)carousel_items_field.GetValue(carousel)!).Where(chart => chart.MD5Hash == importedHash).ToArray();
            return matches.Length == 1 && !UnavailableTableBeatmapFactory.IsUnavailable(matches[0]);
        });
        AddStep("remaining missing chart keeps its identity", () =>
        {
            var missing = ((BindableList<BeatmapInfo>)carousel_items_field.GetValue(carousel)!).Where(UnavailableTableBeatmapFactory.IsUnavailable).ToArray();
            Assert.That(missing.Select(chart => chart.ID), Is.EqualTo([missingId]));
        });
    }

    [Test]
    public void TestBulkRemovalClearsDeletedSelection()
    {
        Task import = null!;
        var directory = string.Empty;
        Guid selectedId = Guid.Empty;
        Guid selectedSetId = Guid.Empty;

        importLampBeatmapSet();
        AddStep("import removable BMS set", () =>
        {
            directory = createNotificationStormCharts(1);
            import = new BmsFileImporter(Realm, LocalStorage).Import(directory);
        });
        waitForNotificationStormWork("removable import", () => import);
        loadSongSelect();
        AddStep("select a chart from the removable set", () =>
        {
            var chart = Realm.Run(r => r.All<BeatmapInfo>().AsEnumerable()
                .First(b => b.Metadata.Source.StartsWith(directory, StringComparison.Ordinal) && !b.BeatmapSet!.DeletePending)
                .Detach());
            selectedId = chart.ID;
            selectedSetId = chart.BeatmapSet!.ID;
            songSelect.ScopeToBeatmapSet(chart.BeatmapSet);
            songSelect.LoadBeatmapSelection(chart);
        });
        AddUntilStep("removable chart selected", () => songSelect.Beatmap.Value.BeatmapInfo.ID == selectedId && !songSelect.IsFiltering);
        AddStep("delete selected set inside a bulk scope", () =>
        {
            using var bulk = BmsBulkBeatmapUpdate.Begin(Realm);
            Realm.Write(r => r.Find<BeatmapSetInfo>(selectedSetId)!.DeletePending = true);
        });
        AddUntilStep("deleted set leaves carousel", () => !carousel.GetCarouselItems()!.Select(item => item.Model).OfType<GroupedBeatmap>().Any(beatmap => beatmap.Beatmap.ID == selectedId));
        AddUntilStep("deleted selection is cleared or replaced", () => songSelect.Beatmap.IsDefault || songSelect.Beatmap.Value.BeatmapInfo.ID != selectedId);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TestBulkRemovalSelectsSurvivingNeighbour(bool deleteLast)
    {
        Guid selected = Guid.Empty;
        Guid expected = Guid.Empty;
        ControlledSongSelect controlled = null!;
        var started = false;
        importLampBeatmapSet();
        loadSongSelect(() => controlled = new ControlledSongSelect());
        AddStep("select chart beside batch deletions", () =>
        {
            var ordered = carousel.GetCarouselItems()!.Select(item => item.Model).OfType<GroupedBeatmap>().ToArray();
            selected = ordered[deleteLast ? ordered.Length - 1 : 0].Beatmap.ID;
            expected = ordered[deleteLast ? ordered.Length - 3 : 2].Beatmap.ID;
            songSelect.LoadBeatmapSelection(ordered.Single(item => item.Beatmap.ID == selected).Beatmap);
        });
        AddUntilStep("selected chart loaded", () => songSelect.Beatmap.Value.BeatmapInfo.ID == selected);
        AddStep("remove selected chart and hide its closest neighbour", () =>
        {
            var ordered = carousel.GetCarouselItems()!.Select(item => item.Model).OfType<GroupedBeatmap>().ToArray();
            var hidden = ordered[deleteLast ? ordered.Length - 2 : 1].Beatmap.ID;
            using var bulk = BmsBulkBeatmapUpdate.Begin(Realm);
            Realm.Write(r =>
            {
                var chart = r.Find<BeatmapInfo>(selected)!;
                chart.BeatmapSet!.Beatmaps.Remove(chart);
                r.Remove(chart);
                r.Find<BeatmapInfo>(hidden)!.Hidden = true;
            });
        });
        AddUntilStep("surviving neighbour selected", () => songSelect.Beatmap.Value.BeatmapInfo.ID == expected && !songSelect.IsFiltering);
        AddStep("confirm recovered chart", () => controlled.Confirm(controlled.Beatmap.Value.BeatmapInfo, () => started = true));
        AddUntilStep("recovered selection can start", () => started);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TestBulkReplacementRetainsSelectedChart(bool online)
    {
        Guid selected = Guid.Empty;
        var replacementId = Guid.NewGuid();
        importLampBeatmapSet();
        loadSongSelect();
        AddStep("prepare online or offline selection", () =>
        {
            selected = songSelect.Beatmap.Value.BeatmapInfo.ID;
            if (!online)
                Realm.Write(r => r.Find<BeatmapInfo>(selected)!.OnlineID = -1);
            songSelect.LoadBeatmapSelection(beatmaps.GetWorkingBeatmap(songSelect.Beatmap.Value.BeatmapInfo, true).BeatmapInfo);
        });
        AddUntilStep("selection metadata ready", () => songSelect.Beatmap.Value.BeatmapInfo.ID == selected
                                                       && (songSelect.Beatmap.Value.BeatmapInfo.OnlineID > 0) == online && !songSelect.IsFiltering);
        AddStep("replace selected chart with a new local GUID", () =>
        {
            using var bulk = BmsBulkBeatmapUpdate.Begin(Realm);
            Realm.Write(r =>
            {
                var old = r.Find<BeatmapInfo>(selected)!;
                var set = old.BeatmapSet!;
                var replacement = new BeatmapInfo(old.Ruleset, old.Difficulty.Detach(), old.Metadata.Detach())
                {
                    ID = replacementId,
                    BeatmapSet = set,
                    OnlineID = old.OnlineID,
                    MD5Hash = old.MD5Hash,
                    Hash = old.Hash,
                    DifficultyName = old.DifficultyName + " updated",
                };
                set.Beatmaps.Remove(old);
                set.Beatmaps.Add(replacement);
                r.Remove(old);
            });
        });
        AddUntilStep("replacement chart selected", () => songSelect.Beatmap.Value.BeatmapInfo.ID == replacementId);
        AddAssert("replacement metadata displayed", () => songSelect.Beatmap.Value.BeatmapInfo.DifficultyName.EndsWith(" updated", StringComparison.Ordinal));
    }

    [Test]
    public void TestBulkRemovalOfAllChartsClearsSelection()
    {
        importLampBeatmapSet();
        loadSongSelect();
        AddStep("delete the complete library", () =>
        {
            using var bulk = BmsBulkBeatmapUpdate.Begin(Realm);
            Realm.Write(r =>
            {
                foreach (var set in r.All<BeatmapSetInfo>())
                    set.DeletePending = true;
            });
        });
        AddUntilStep("empty library presented", () => !songSelect.IsFiltering && carousel.MatchedBeatmapsCount == 0);
        AddAssert("working beatmap cleared", () => songSelect.Beatmap.IsDefault);
        AddAssert("carousel selection cleared", () => carousel.CurrentBeatmap == null);
    }

    private void waitForNotificationStormWork(string name, Func<Task> getWork)
    {
        AddUntilStep($"{name} completes", () => getWork().IsCompleted);
        AddStep($"{name} succeeded", () => getWork().GetAwaiter().GetResult());
    }

    private string createNotificationStormCharts(int setCount)
    {
        var root = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"notification-storm-{Guid.NewGuid():N}");

        for (var setIndex = 0; setIndex < setCount; setIndex++)
        {
            var directory = Path.Combine(root, $"set-{setIndex:D3}");
            Directory.CreateDirectory(directory);
            for (var chartIndex = 0; chartIndex < storm_charts_per_set; chartIndex++)
            {
                File.WriteAllText(Path.Combine(directory, $"chart-{chartIndex}.bms"), $"""
                                                                                       #PLAYER 1
                                                                                       #TITLE Notification Storm {Path.GetFileName(root)} {setIndex:D3}
                                                                                       #SUBTITLE Difficulty {chartIndex}
                                                                                       #ARTIST Test
                                                                                       #BPM 150
                                                                                       #PLAYLEVEL 1
                                                                                       #DIFFICULTY 1
                                                                                       #00111:01000100
                                                                                       #00212:00010001
                                                                                       """);
            }
        }

        return root;
    }

    // Observe the actual inherited list without adding production hooks or replacing its update algorithm.
    private static readonly FieldInfo carousel_items_field =
        typeof(Carousel<BeatmapInfo>).GetField("Items", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private sealed class NotificationStormProbe : IDisposable
    {
        public bool Ready { get; private set; }

        public int RealmCallbacks { get; private set; }

        public int RealmChangedSets { get; private set; }

        public NotificationCounts Detached { get; } = new();

        public NotificationCounts Carousel { get; } = new();

        public BeatmapInfo[] Charts => carouselItems.Where(isFixtureChart).ToArray();

        private readonly IBindableList<BeatmapSetInfo> detachedSets;
        private readonly BindableList<BeatmapInfo> carouselItems;
        private readonly string directory;
        private readonly Func<double> getFrameTime;
        private readonly IDisposable realmSubscription;
        private Stopwatch? notificationTimer;
        private double? realmToDetachedMilliseconds;
        private double? realmToCarouselMilliseconds;

        public NotificationStormProbe(RealmAccess realm, RealmDetachedBeatmapStore store, BindableList<BeatmapInfo> carouselItems,
                                      string directory, Func<double> getFrameTime)
        {
            this.carouselItems = carouselItems;
            this.directory = directory + Path.DirectorySeparatorChar;
            this.getFrameTime = getFrameTime;
            detachedSets = store.GetBeatmapSets(null);
            detachedSets.CollectionChanged += detachedChanged;
            carouselItems.CollectionChanged += carouselChanged;
            realmSubscription = realm.RegisterForNotifications(r => r.All<BeatmapSetInfo>().Where(set => !set.DeletePending && !set.Protected), (sender, changes) =>
            {
                if (changes == null)
                {
                    Ready = true;
                    return;
                }

                var affected = changes.InsertedIndices.Concat(changes.NewModifiedIndices).Distinct()
                    .Count(index => sender[index].Beatmaps.Any(isFixtureChart));
                if (affected == 0)
                    return;

                notificationTimer ??= Stopwatch.StartNew();
                RealmCallbacks++;
                RealmChangedSets += affected;
            });
        }

        private bool isFixtureChart(BeatmapInfo chart) => chart.Metadata.Source.StartsWith(directory, StringComparison.Ordinal);

        private void detachedChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            Detached.Record(args, item => item is BeatmapSetInfo set && set.Beatmaps.Any(isFixtureChart), getFrameTime());
            if (notificationTimer != null && realmToDetachedMilliseconds == null && Detached.Events > 0)
                realmToDetachedMilliseconds = notificationTimer.Elapsed.TotalMilliseconds;
        }

        private void carouselChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            Carousel.Record(args, item => item is BeatmapInfo chart && isFixtureChart(chart), getFrameTime());
            if (notificationTimer != null && realmToCarouselMilliseconds == null && Carousel.Events > 0)
                realmToCarouselMilliseconds = notificationTimer.Elapsed.TotalMilliseconds;
        }

        public void Dispose()
        {
            realmSubscription.Dispose();
            detachedSets.CollectionChanged -= detachedChanged;
            detachedSets.UnbindAll();
            carouselItems.CollectionChanged -= carouselChanged;
        }

        public override string ToString() => $"Realm callbacks={RealmCallbacks}, changed sets={RealmChangedSets}; detached {Detached}; carousel {Carousel}; timing realm→detached={realmToDetachedMilliseconds?.ToString("F0") ?? "-"} ms, realm→carousel={realmToCarouselMilliseconds?.ToString("F0") ?? "-"} ms";
    }

    private sealed class NotificationCounts
    {
        public int Events { get; private set; }

        public int AddedItems { get; private set; }

        public int RemovedItems { get; private set; }

        public int ReplacedItems { get; private set; }

        public int SingleItemRemovals { get; private set; }

        private readonly Dictionary<double, int> eventsPerFrame = [];

        public void Record(NotifyCollectionChangedEventArgs args, Func<object, bool> include, double frameTime)
        {
            var oldCount = args.OldItems?.Cast<object>().Count(include) ?? 0;
            var newCount = args.NewItems?.Cast<object>().Count(include) ?? 0;
            if (oldCount + newCount == 0 && args.Action != NotifyCollectionChangedAction.Reset)
                return;

            Events++;
            eventsPerFrame[frameTime] = eventsPerFrame.GetValueOrDefault(frameTime) + 1;
            switch (args.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    AddedItems += newCount;
                    break;

                case NotifyCollectionChangedAction.Remove:
                    RemovedItems += oldCount;
                    if (args.OldItems!.Count == 1)
                        SingleItemRemovals++;
                    break;

                case NotifyCollectionChangedAction.Replace:
                    ReplacedItems += newCount;
                    break;
            }
        }

        public override string ToString() =>
            $"events={Events}, max/frame={eventsPerFrame.Values.DefaultIfEmpty().Max()}, added={AddedItems}, removed={RemovedItems}, replaced={ReplacedItems}";
    }
}
