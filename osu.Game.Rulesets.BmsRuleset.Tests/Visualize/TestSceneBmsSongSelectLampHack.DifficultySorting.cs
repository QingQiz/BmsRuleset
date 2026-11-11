using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using System.Reflection;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Difficulty;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Filter;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.IO.Import;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsSongSelectLampHack
{
    [Test]
    public void TestDifficultySortingUsesBackgroundHSCacheAndIgnoresSpeed()
    {
        var filterCount = 0;
        Task refresh = null!;
        importDifficultyCharts();
        loadDifficultySongSelect(() => new BmsSoloSongSelect());
        AddUntilStep("background refresh finished", () => songSelect.Carousel.DifficultyRefresh.ProcessingTask.IsCompleted);
        AddStep("select difficulty sorting", () => config.SetValue(OsuSetting.SongSelectSortingMode, SortMode.Difficulty));
        AddUntilStep("keys before scratch normally", () => !songSelect.IsFiltering
            && carousel.Criteria?.Sort == SortMode.Difficulty && difficultyOrder().SequenceEqual(new[] { "Keys", "Scratch" }));
        AddStep("enable hide scratch", () => songSelect.Mods.Value = [new BmsModHideScratch()]);
        AddUntilStep("scratch first with HS", () => !songSelect.IsFiltering && difficultyOrder().SequenceEqual(new[] { "Scratch", "Keys" }));
        AddStep("remove hide scratch", () => songSelect.Mods.Value = []);
        AddUntilStep("normal order restored", () => !songSelect.IsFiltering && difficultyOrder().SequenceEqual(new[] { "Keys", "Scratch" }));
        AddStep("enable double time", () =>
        {
            filterCount = carousel.FilterCount;
            refresh = songSelect.Carousel.DifficultyRefresh.ProcessingTask;
            songSelect.Mods.Value = [new BmsModDoubleTime()];
        });
        AddWaitStep("allow mod updates", 3);
        AddAssert("double time keeps sort profile", () => carousel.FilterCount, () => Is.EqualTo(filterCount));
        AddStep("change double time speed", () => ((BmsModDoubleTime)songSelect.Mods.Value.Single()).SpeedChange.Value = 1.25);
        AddWaitStep("allow speed updates", 3);
        AddAssert("speed leaves background refresh unchanged", () => ReferenceEquals(refresh, songSelect.Carousel.DifficultyRefresh.ProcessingTask));
        AddAssert("speed keeps sort order", () => difficultyOrder(), () => Is.EqualTo(new[] { "Keys", "Scratch" }));
    }

    [Test]
    public void TestBackgroundDifficultyRefreshPausesWhileSongSelectIsSuspended()
    {
        DelayedDifficultySongSelect controlled = null!;
        importDifficultyCharts();
        loadDifficultySongSelect(() => controlled = new DelayedDifficultySongSelect());
        AddUntilStep("background HS calculation started", () => controlled.DifficultyProbe.HSCalls == Math.Min(2, BmsDifficultyRefresh.CalculationConcurrency));
        AddAssert("song select remains usable during calculation", () => !songSelect.IsFiltering && difficultyOrder().Length == 2);
        AddStep("leave song select", () => songSelect.Push(new ResumeTestScreen()));
        AddUntilStep("child screen loaded", () => Stack.CurrentScreen is ResumeTestScreen { IsLoaded: true });
        AddUntilStep("active calculation cancelled", () => controlled.DifficultyProbe.Stopped == Math.Min(2, BmsDifficultyRefresh.CalculationConcurrency));
        AddWaitStep("remain outside song select", 5);
        AddAssert("no further HS calculations", () => controlled.DifficultyProbe.HSCalls, () => Is.EqualTo(Math.Min(2, BmsDifficultyRefresh.CalculationConcurrency)));
        AddAssert("refresh remains pending", () => !songSelect.Carousel.DifficultyRefresh.ProcessingTask.IsCompleted);
        AddStep("return to song select", () =>
        {
            controlled.DifficultyProbe.DelayHS = false;
            Stack.Exit();
        });
        AddUntilStep("remaining calculations completed", () => controlled.DifficultyProbe.HSCalls == 2 + Math.Min(2, BmsDifficultyRefresh.CalculationConcurrency)
            && songSelect.Carousel.DifficultyRefresh.ProcessingTask.IsCompleted);
    }

    [Test]
    public void TestBackgroundDifficultyNotificationCancelsTheCurrentRefresh()
    {
        DelayedDifficultySongSelect controlled = null!;
        ProgressNotification progress = null!;
        AddStep("load notification overlay", () => Add((NotificationOverlay)notificationOverlay));
        importDifficultyCharts();
        loadDifficultySongSelect(() => controlled = new DelayedDifficultySongSelect());
        AddUntilStep("background calculation started", () => controlled.DifficultyProbe.HSCalls == Math.Min(2, BmsDifficultyRefresh.CalculationConcurrency));
        AddUntilStep("refresh notification visible", () =>
        {
            progress = notificationOverlay.AllNotifications.OfType<ProgressNotification>()
                .SingleOrDefault(notification => notification.State == ProgressNotificationState.Active
                    && notification.Text.Equals(BmsStrings.DifficultyRefreshRunning));
            return progress != null;
        });
        AddStep("cancel from notification", () => progress.Close(false));
        AddUntilStep("calculation and refresh stopped", () => controlled.DifficultyProbe.Stopped == Math.Min(2, BmsDifficultyRefresh.CalculationConcurrency)
            && songSelect.Carousel.DifficultyRefresh.ProcessingTask.IsCompleted);
        AddWaitStep("allow queued progress to arrive", 3);
        AddAssert("notification stays cancelled", () => progress.State, () => Is.EqualTo(ProgressNotificationState.Cancelled));
        AddAssert("cancelled message shown", () => progress.Text.Equals(BmsStrings.DifficultyRefreshCancelled));
        AddStep("suspend song select", () => songSelect.Push(new ResumeTestScreen()));
        AddUntilStep("child screen loaded", () => Stack.CurrentScreen is ResumeTestScreen { IsLoaded: true });
        AddStep("return to song select", () => Stack.Exit());
        AddWaitStep("allow resumed screen to settle", 3);
        AddAssert("manual cancellation survives resume", () => controlled.DifficultyProbe.HSCalls, () => Is.EqualTo(Math.Min(2, BmsDifficultyRefresh.CalculationConcurrency)));
        AddAssert("carousel remains usable", () => !songSelect.IsFiltering && difficultyOrder().Length == 2);
    }

    private void importDifficultyCharts()
    {
        Task import = null!;
        AddStep("import scratch and key charts", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"difficulty-sorting-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "scratch.bme"), $"#COMMENT {directory}\n" + """
                #PLAYER 1
                #TITLE Difficulty sorting
                #SUBTITLE Scratch
                #BPM 150
                #RANK 2
                #00116:01010101010101010101010101010101
                #00216:01010101010101010101010101010101
                #00316:01010101010101010101010101010101
                #00416:01010101010101010101010101010101
                #00419:01
                """);
            File.WriteAllText(Path.Combine(directory, "keys.bme"), $"#COMMENT {directory}\n" + """
                #PLAYER 1
                #TITLE Difficulty sorting
                #SUBTITLE Keys
                #BPM 150
                #RANK 2
                #00111:01000100
                #00212:01000100
                #00313:01000100
                #00419:01000100
                """);
            import = new BmsFileImporter(Realm, LocalStorage).Import(directory);
        });
        AddUntilStep("charts imported", () => import.IsCompleted);
        AddStep("select imported chart", () =>
        {
            import.GetAwaiter().GetResult();
            var first = Realm.Run(r => r.All<BeatmapInfo>().First().Detach());
            Beatmap.Value = beatmaps.GetWorkingBeatmap(first, true);
        });
    }

    private void loadDifficultySongSelect(Func<BmsSoloSongSelect> create)
    {
        AddStep("load song select with title sorting", () => Stack.Push(songSelect = create()));
        AddUntilStep("carousel ready", () => songSelect.CarouselItemsPresented && !songSelect.IsFiltering && difficultyOrder().Length == 2);
    }

    private string[] difficultyOrder() => carousel.GetCarouselItems()?
        .Select(item => item.Model).OfType<GroupedBeatmap>()
        .Select(grouped => grouped.Beatmap.DifficultyName).ToArray() ?? [];

    private partial class DelayedDifficultySongSelect : BmsSoloSongSelect
    {
        internal readonly DifficultyCalculationProbe DifficultyProbe = new();

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            DifficultyProbe.Dispose();
        }
    }

    private sealed class DifficultyCalculationProbe : IDisposable
    {
        private static DifficultyCalculationProbe current;
        private readonly ScopedMethodProbe patch;
        internal volatile bool DelayHS = true;
        internal int HSCalls;
        internal int Stopped;

        internal DifficultyCalculationProbe()
        {
            current = this;
            patch = new ScopedMethodProbe(typeof(BmsDifficultyCalculator).GetMethod("CreateDifficultyAttributes", BindingFlags.Instance | BindingFlags.NonPublic)!,
                prefix: typeof(DifficultyCalculationProbe).GetMethod(nameof(beforeCalculation), BindingFlags.Static | BindingFlags.NonPublic));
        }

        // ReSharper disable once InconsistentNaming
        private static void beforeCalculation(BmsDifficultyCalculator __instance, Mod[] mods)
        {
            var probe = current;
            if (!mods.OfType<BmsModHideScratch>().Any())
                return;

            Interlocked.Increment(ref probe.HSCalls);
            if (!probe.DelayHS)
                return;

            try
            {
                Task.Delay(Timeout.Infinite, __instance.CalculationCancellationToken).GetAwaiter().GetResult();
            }
            finally
            {
                Interlocked.Increment(ref probe.Stopped);
            }
        }

        public void Dispose() => patch.Dispose();
    }
}
