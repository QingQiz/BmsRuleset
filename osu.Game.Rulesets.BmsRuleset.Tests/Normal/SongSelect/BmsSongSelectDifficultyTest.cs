#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Carousel;
using LineBufferedReader = osu.Game.IO.LineBufferedReader;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Difficulty;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Filter;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;
using osu.Game.Tests.Beatmaps;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.SongSelect;

[TestFixture]
public partial class BmsSongSelectDifficultyTest
{
    private string directory = null!;
    private string cachePath => Path.Combine(directory, "difficulty.bin");
    private RulesetInfo ruleset = null!;
    private ControlledDifficultyCache calculator = null!;
    private BmsSongSelectDifficultyCache cache = null!;
    private BmsDifficultyRefresh refresh = null!;
    private FilterCriteria criteria = null!;

    [SetUp]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), $"bms-song-select-difficulty-{Guid.NewGuid():N}");
        ruleset = new BmsRuleset().RulesetInfo;
        calculator = new ControlledDifficultyCache();
        cache = new BmsSongSelectDifficultyCache(cachePath);
        refresh = new BmsDifficultyRefresh(cache, calculateDifficulty);
        refresh.Resume();
        criteria = new FilterCriteria { Ruleset = ruleset, Mods = [], Sort = SortMode.Difficulty, AllowConvertedBeatmaps = true };
    }

    [TearDown]
    public async Task TearDown()
    {
        refresh.Dispose();
        await refresh.ProcessingTask.WaitAsync(TimeSpan.FromSeconds(5));
        calculator.Dispose();
        Directory.Delete(directory, true);
    }

    [Test]
    public async Task BackgroundRefreshOnlyCalculatesNativeHSAndConvertedBase()
    {
        var native = chart("native", 8);
        var mania = chart("mania", 1, maniaRuleset());
        await refreshCharts(native, mania);
        Assert.That(calculator.Requests.Select(request => request.Mods), Is.EquivalentTo(new[] { "HS", "" }));
        Assert.That(calculator.Requests.Select(request => request.Ruleset), Is.All.EqualTo(Constant.SHORT_NAME));
        Assert.That(cache.GetStars(native, false), Is.EqualTo(8));
        Assert.That(cache.GetStars(mania, true), Is.EqualTo(cache.GetStars(mania, false)));
        Assert.That(cache.Contains(mania.Hash, false), Is.True);
        Assert.That(cache.Contains(mania.Hash, true), Is.True);
        Assert.That(new FileInfo(cachePath).Length, Is.EqualTo(8 + 3 * 41));

        using var restarted = new BmsDifficultyRefresh(new BmsSongSelectDifficultyCache(cachePath), calculateDifficulty);
        restarted.Resume();
        restarted.Refresh([native, mania]);
        await restarted.ProcessingTask;
        Assert.That(calculator.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task SortingGroupingAndStarSearchUseOnlyBaseAndHSProfiles()
    {
        var scratch = chart("scratch", 8);
        var keys = chart("keys", 4);
        var mania = chart("mania", 1, maniaRuleset());
        calculator.Calculate = (beatmap, _, _) => Task.FromResult<StarDifficulty?>(new StarDifficulty(
            beatmap.Metadata.Title == "scratch" ? 2 : beatmap.Metadata.Title == "mania" ? 5.2 : 4, 0));
        await refreshCharts(scratch, keys, mania);
        var filter = createFilter();
        var source = items(scratch, keys, mania);
        Assert.That(names(await filter.Run(source, default)), Is.EqualTo(new[] { "keys", "mania", "scratch" }));

        criteria.Mods = [new BmsModHideScratch(), new BmsModDoubleTime { SpeedChange = { Value = 1.25 } }];
        criteria.Group = GroupMode.Difficulty;
        var result = await filter.Run(source, default);
        Assert.That(names(result), Is.EqualTo(new[] { "scratch", "keys", "mania" }));
        Assert.That(result.Select(item => item.Model).OfType<StarDifficultyGroupDefinition>().Select(group => group.Difficulty.Stars),
            Is.EqualTo(new[] { 2d, 4d, 5d }));
        ((BmsModDoubleTime)criteria.Mods[1]).SpeedChange.Value = 1.5;
        Assert.That(names(await filter.Run(source, default)), Is.EqualTo(new[] { "scratch", "keys", "mania" }));

        criteria.StarDifficulty.Min = 4.5;
        criteria.StarDifficulty.Max = 6;
        Assert.That(names(await filter.Run(source, default)), Is.EqualTo(new[] { "mania" }));
        Assert.That(calculator.Requests, Has.Count.EqualTo(3), "Filtering must only read completed ratings.");
        Assert.That(scratch.StarRating, Is.EqualTo(8));
        Assert.That(mania.StarRating, Is.EqualTo(1));
    }

    [Test]
    public async Task MissingRatingsDoNotBlockFilteringOrUseManiaStars()
    {
        var mania = chart("mania", 5, maniaRuleset());
        criteria.StarDifficulty.Min = 4;
        Assert.That(names(await createFilter().Run(items(mania), default)), Is.Empty);
        Assert.That(calculator.Requests, Is.Empty);
    }

    [Test]
    public async Task DeletionRetainsRecordsAndImportsOnlyAppendMissingContent()
    {
        var first = chart("first", 8);
        var second = chart("second", 4);
        await refreshCharts(first);
        var initial = File.ReadAllBytes(cachePath);
        await refreshCharts();
        Assert.That(File.ReadAllBytes(cachePath), Is.EqualTo(initial));
        await refreshCharts(first);
        Assert.That(calculator.Requests, Has.Count.EqualTo(1));
        await refreshCharts(first, second);
        var appended = File.ReadAllBytes(cachePath);
        Assert.That(appended.Take(initial.Length), Is.EqualTo(initial));
        Assert.That(appended.Length, Is.EqualTo(initial.Length + 41));
        var replacement = chart("replacement", 4);
        replacement.ID = second.ID;
        await refreshCharts(first, replacement);
        Assert.That(calculator.Requests, Has.Count.EqualTo(3));
        Assert.That(cache.Contains(second.Hash, true), Is.True);
    }

    [Test]
    public async Task VersionChangeRebuildsTheIndex()
    {
        var beatmap = chart("chart", 8);
        await refreshCharts(beatmap);
        using (var writer = new BinaryWriter(File.OpenWrite(cachePath)))
        {
            writer.BaseStream.Position = sizeof(int);
            writer.Write(BmsDifficultyCalculator.DIFFICULTY_VERSION - 1);
        }
        var updatedCache = new BmsSongSelectDifficultyCache(cachePath);
        Assert.That(updatedCache.Contains(beatmap.Hash, true), Is.False);
        Assert.That(new FileInfo(cachePath).Length, Is.EqualTo(8));
        using var updated = new BmsDifficultyRefresh(updatedCache, calculateDifficulty);
        updated.Resume();
        updated.Refresh([beatmap]);
        await updated.ProcessingTask;
        Assert.That(calculator.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task InterruptedAppendPreservesCompleteRecords()
    {
        var beatmap = chart("chart", 8);
        await refreshCharts(beatmap);
        using (var stream = File.Open(cachePath, FileMode.Append))
            stream.Write(new byte[15]);
        var recovered = new BmsSongSelectDifficultyCache(cachePath);
        Assert.That(recovered.GetStars(beatmap, true), Is.EqualTo(2));
        Assert.That(new FileInfo(cachePath).Length, Is.EqualTo(8 + 41));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SuspensionStopsCurrentCalculationAndResumesFromCompletedWork(bool returnNullOnCancellation)
    {
        var first = chart("first", 8);
        var second = chart("second", 4);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        calculator.Calculate = async (beatmap, _, token) =>
        {
            if (beatmap.Hash == first.Hash)
                return new StarDifficulty(2, 0);
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException) when (returnNullOnCancellation)
            {
                return null;
            }
            finally
            {
                stopped.SetResult();
            }
            throw new AssertionException("The pending calculation must be cancelled.");
        };
        refresh.Refresh([first, second]);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(() => cache.Contains(first.Hash, true), Is.True.After(5000, 10));
        refresh.Pause();
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(refresh.ProcessingTask.IsCompleted, Is.False);
        Assert.That(calculator.Requests, Has.Count.EqualTo(2));
        Assert.That(() => new BmsSongSelectDifficultyCache(cachePath).Contains(first.Hash, true), Is.True.After(5000, 10));
        Assert.That(names(await createFilter().Run(items(first, second), default)), Is.EqualTo(new[] { "second", "first" }));

        calculator.Calculate = (_, _, _) => Task.FromResult<StarDifficulty?>(new StarDifficulty(3, 0));
        refresh.Resume();
        await refresh.ProcessingTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(calculator.Requests, Has.Count.EqualTo(3));
        Assert.That(cache.GetStars(second, true), Is.EqualTo(3));
    }

    [Test]
    public async Task PausedRefreshCanBeReplacedAndDisposed()
    {
        refresh.Pause();
        refresh.Refresh([chart("first", 8)]);
        var superseded = refresh.ProcessingTask;
        refresh.Refresh([chart("second", 4)]);
        await superseded.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(calculator.Requests, Is.Empty);
        refresh.Resume();
        await refresh.ProcessingTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(calculator.Requests.Single().Title, Is.EqualTo("second"));

        var paused = new BmsDifficultyRefresh(cache, calculateDifficulty);
        paused.Refresh([chart("third", 6)]);
        paused.Dispose();
        await paused.ProcessingTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(calculator.Requests, Has.Count.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CancelPublishesCompletedWorkAndDoesNotResume(bool paused)
    {
        var first = chart("first", 8);
        var second = chart("second", 4);
        var progress = new List<BmsDifficultyRefresh.Progress>();
        var initial = new TaskCompletionSource<BmsDifficultyRefresh.Progress>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        refresh.ProgressChanged += value =>
        {
            progress.Add(value);
            initial.TrySetResult(value);
        };
        calculator.Calculate = async (beatmap, _, token) =>
        {
            if (beatmap.Hash == second.Hash)
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            }
            return new StarDifficulty(2, 0);
        };

        refresh.Refresh([first, second]);
        var operation = await initial.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(() => cache.Contains(first.Hash, true), Is.True.After(5000, 10));
        if (paused)
            refresh.Pause();
        refresh.Cancel(operation.RequestToken);
        await refresh.ProcessingTask.WaitAsync(TimeSpan.FromSeconds(5));
        var final = progress.Last();
        Assert.That(final.Finished, Is.True);
        Assert.That(final.RequestToken.IsCancellationRequested, Is.True);
        Assert.That(final.Processed, Is.EqualTo(1));
        Assert.That(final.Total, Is.EqualTo(2));
        Assert.That(final.Failed, Is.Zero);
        Assert.That(new BmsSongSelectDifficultyCache(cachePath).Contains(first.Hash, true), Is.True);
        Assert.That(cache.Contains(second.Hash, true), Is.False);

        var cancelledTask = refresh.ProcessingTask;
        refresh.Resume();
        refresh.Refresh([first, second]);
        Assert.That(refresh.ProcessingTask, Is.SameAs(cancelledTask));
        Assert.That(calculator.Requests, Has.Count.EqualTo(2));

        calculator.Calculate = (_, _, _) => Task.FromResult<StarDifficulty?>(new StarDifficulty(3, 0));
        using var reopened = new BmsDifficultyRefresh(cache, calculateDifficulty);
        reopened.Resume();
        reopened.Refresh([first, second]);
        await reopened.ProcessingTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(calculator.Requests, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task SupersededNotificationCannotCancelReplacement()
    {
        var firstStart = new TaskCompletionSource<BmsDifficultyRefresh.Progress>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStart = new TaskCompletionSource<BmsDifficultyRefresh.Progress>(TaskCreationOptions.RunContinuationsAsynchronously);
        refresh.ProgressChanged += progress =>
        {
            if (!progress.Finished && !firstStart.TrySetResult(progress))
                secondStart.TrySetResult(progress);
        };
        calculator.Calculate = async (_, _, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return null;
        };
        refresh.Refresh([chart("first", 8)]);
        var first = await firstStart.Task.WaitAsync(TimeSpan.FromSeconds(5));
        refresh.Refresh([chart("second", 4)]);
        var second = await secondStart.Task.WaitAsync(TimeSpan.FromSeconds(5));
        refresh.Cancel(first.RequestToken);
        Assert.That(refresh.IsCurrent(first.RequestToken), Is.False);
        Assert.That(refresh.IsCurrent(second.RequestToken), Is.True);
        Assert.That(second.RequestToken.IsCancellationRequested, Is.False);
        refresh.Cancel(second.RequestToken);
        await refresh.ProcessingTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task FastRefreshCoalescesProgressByTime()
    {
        var timer = Stopwatch.StartNew();
        var updates = new List<(TimeSpan Time, BmsDifficultyRefresh.Progress Progress)>();
        refresh.ProgressChanged += progress => updates.Add((timer.Elapsed, progress));
        await refreshCharts(Enumerable.Range(0, 128).Select(i => chart($"chart-{i}", 4)).ToArray());
        Assert.That(updates.First().Progress.Processed, Is.Zero);
        Assert.That(updates.Last().Progress.Processed, Is.EqualTo(128));
        Assert.That(updates.Last().Progress.Finished, Is.True);
        for (var i = 1; i < updates.Count - 1; i++)
            Assert.That(updates[i].Time - updates[i - 1].Time, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(10)));
        Assert.That(updates.Count, Is.LessThanOrEqualTo(2 + (int)(timer.Elapsed.TotalSeconds / 10)));
    }

    [Test]
    public async Task ParallelWorkersAreBoundedAndCancelTogether()
    {
        var concurrency = BmsDifficultyRefresh.CalculationConcurrency;
        var running = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new ConcurrentQueue<BmsDifficultyRefresh.Progress>();
        refresh.ProgressChanged += progress.Enqueue;
        calculator.Calculate = async (_, _, token) =>
        {
            var count = Interlocked.Increment(ref running);
            if (count == concurrency)
                started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, token);
                return null;
            }
            finally
            {
                Interlocked.Decrement(ref running);
            }
        };
        refresh.Refresh(Enumerable.Range(0, concurrency * 3).Select(i => chart($"parallel-{i}", 4)));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(Volatile.Read(ref running), Is.EqualTo(concurrency));
        Assert.That(calculator.Requests, Has.Count.EqualTo(concurrency));
        refresh.Cancel(progress.First().RequestToken);
        await refresh.ProcessingTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(Volatile.Read(ref running), Is.Zero);
        Assert.That(calculator.Requests, Has.Count.EqualTo(concurrency));
        Assert.That(progress.Last().Processed, Is.Zero);
        Assert.That(progress.Last().Finished, Is.True);
    }

    [Test]
    public async Task FailedChartDoesNotAbortOtherParallelCalculations()
    {
        var charts = Enumerable.Range(0, 12).Select(i => chart($"chart-{i}", 4)).ToArray();
        calculator.Calculate = (beatmap, _, _) => beatmap.Hash == charts[3].Hash
            ? Task.FromException<StarDifficulty?>(new InvalidDataException("Unreadable test chart"))
            : Task.FromResult<StarDifficulty?>(new StarDifficulty(2, 0));
        var progress = new ConcurrentQueue<BmsDifficultyRefresh.Progress>();
        refresh.ProgressChanged += progress.Enqueue;
        await refreshCharts(charts);
        Assert.That(calculator.Requests, Has.Count.EqualTo(charts.Length));
        Assert.That(progress.Last().Processed, Is.EqualTo(charts.Length));
        Assert.That(progress.Last().Failed, Is.EqualTo(1));
        Assert.That(cache.Contains(charts[3].Hash, true), Is.False);
        Assert.That(charts.Where((_, index) => index != 3).All(beatmap => cache.Contains(beatmap.Hash, true)), Is.True);
    }

    [Test]
    public async Task ParallelCalculatorsMatchSequentialRatingsAndPreserveSourceCharts()
    {
        var maps = Enumerable.Range(0, 12).Select(i =>
        {
            var text = $"#TITLE chart-{i}\n#BPM {120 + i * 7}\n#RANK {i % 4}\n#00116:0101010101010101\n#00111:0100010001000100\n#00212:01010101\n#00319:01";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            using var reader = new LineBufferedReader(stream);
            var decoded = new BmsBeatmapDecoder().Decode(reader);
            decoded.BeatmapInfo.Ruleset = ruleset;
            decoded.BeatmapInfo.Hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            return new TestWorkingBeatmap(decoded);
        }).ToDictionary(working => working.BeatmapInfo.Hash);
        var expected = maps.ToDictionary(pair => pair.Key, pair =>
            new BmsDifficultyCalculator(ruleset, pair.Value).Calculate([new BmsModHideScratch()]).StarRating);
        var sourceCounts = maps.ToDictionary(pair => pair.Key, pair => pair.Value.Beatmap.HitObjects.Count);
        using var parallel = new BmsDifficultyRefresh(cache, (beatmap, mods, token) => Task.Run<double?>(() =>
            new BmsDifficultyCalculator(ruleset, maps[beatmap.Hash]).Calculate(mods, token).StarRating, token));
        parallel.Resume();
        parallel.Refresh(maps.Values.Select(working => working.BeatmapInfo));
        await parallel.ProcessingTask.WaitAsync(TimeSpan.FromSeconds(10));
        foreach (var (hash, working) in maps)
        {
            Assert.That(cache.GetStars(working.BeatmapInfo, true), Is.EqualTo(expected[hash]).Within(1e-12));
            Assert.That(working.Beatmap.HitObjects, Has.Count.EqualTo(sourceCounts[hash]));
        }
    }

    [Test]
    public async Task CacheAppendsBatchesAndFlushesRemainingRecords()
    {
        var charts = Enumerable.Range(0, 270).Select(i => chart($"batch-{i}", 4)).ToArray();
        await Parallel.ForEachAsync(charts.Take(255), (beatmap, _) =>
        {
            cache.Store(beatmap.Hash, true, 2);
            return ValueTask.CompletedTask;
        });
        Assert.That(new FileInfo(cachePath).Length, Is.EqualTo(8), "Individual results should remain in memory.");
        Assert.That(charts.Take(255).All(beatmap => cache.Contains(beatmap.Hash, true)), Is.True);

        cache.Store(charts[255].Hash, true, 2);
        Assert.That(new FileInfo(cachePath).Length, Is.EqualTo(8 + 256 * 41));
        foreach (var beatmap in charts.Skip(256))
            cache.Store(beatmap.Hash, true, 2);
        Assert.That(new FileInfo(cachePath).Length, Is.EqualTo(8 + 256 * 41));
        cache.Flush();
        Assert.That(new FileInfo(cachePath).Length, Is.EqualTo(8 + charts.Length * 41));
        cache.Store(charts[0].Hash, true, 2);
        cache.Flush();
        Assert.That(new FileInfo(cachePath).Length, Is.EqualTo(8 + charts.Length * 41));
        var persisted = new BmsSongSelectDifficultyCache(cachePath);
        Assert.That(charts.All(beatmap => persisted.GetStars(beatmap, true) == 2), Is.True);
    }

    private async Task<double?> calculateDifficulty(BeatmapInfo beatmap, Mod[] mods, CancellationToken token) =>
        (await calculator.GetDifficultyAsync(beatmap, ruleset, mods, token))?.Stars;

    private async Task refreshCharts(params BeatmapInfo[] beatmaps)
    {
        refresh.Refresh(beatmaps);
        await refresh.ProcessingTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private BmsBeatmapCarouselDifficultyFilter createFilter() => new(() => criteria, () => cache,
    [
        new BmsBeatmapCarouselFilterMatching(() => criteria),
        new BeatmapCarouselFilterSorting(() => criteria),
        new BeatmapCarouselFilterGrouping
        {
            GetCriteria = () => criteria,
            GetCollections = () => [],
            GetLocalUserTopRanks = _ => new Dictionary<Guid, ScoreRank>(),
            GetFavouriteBeatmapSets = () => [],
        },
    ]);

    private BeatmapInfo chart(string title, double stars, RulesetInfo? sourceRuleset = null)
    {
        var beatmap = new BeatmapInfo(sourceRuleset ?? ruleset, new BeatmapDifficulty { CircleSize = 7 })
        {
            Hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(title))),
            MD5Hash = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(title))),
            StarRating = stars,
            Metadata = new BeatmapMetadata { Title = title },
        };
        if (sourceRuleset == null)
            new BmsDifficultyInfo { Rank = 2, LayoutVariant = BmsLayoutVariant.Bme7K }.WriteToOsuDifficulty(beatmap.Difficulty);
        beatmap.BeatmapSet = new BeatmapSetInfo([beatmap]);
        return beatmap;
    }

    private static RulesetInfo maniaRuleset() => new() { ShortName = "mania", OnlineID = 3 };
    private static List<CarouselItem> items(params BeatmapInfo[] beatmaps) => beatmaps.Select(beatmap => new CarouselItem(beatmap)).ToList();
    private static IEnumerable<string> names(IEnumerable<CarouselItem> items) =>
        items.Select(item => item.Model).OfType<GroupedBeatmap>().Select(grouped => grouped.Beatmap.Metadata.Title);

    private partial class ControlledDifficultyCache : BeatmapDifficultyCache
    {
        public readonly ConcurrentQueue<(string Title, string Ruleset, string Mods)> Requests = new();
        public Func<IBeatmapInfo, IEnumerable<Mod>, CancellationToken, Task<StarDifficulty?>> Calculate =
            (_, _, _) => Task.FromResult<StarDifficulty?>(new StarDifficulty(2, 0));

        public override Task<StarDifficulty?> GetDifficultyAsync(IBeatmapInfo beatmapInfo, IRulesetInfo? rulesetInfo = null,
            IEnumerable<Mod>? mods = null, CancellationToken cancellationToken = default, int computationDelay = 0)
        {
            var selectedMods = mods!.ToArray();
            Requests.Enqueue((beatmapInfo.Metadata.Title, rulesetInfo!.ShortName, string.Join(',', selectedMods.Select(mod => mod.Acronym))));
            return Calculate(beatmapInfo, selectedMods, cancellationToken);
        }
    }
}
