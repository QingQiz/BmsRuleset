#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using osu.Framework.IO.Stores;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.Extensions;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Scoring;
using osu.Game.Tests.Beatmaps;
using Realms;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Replay;

[TestFixture]
public class BmsReplaySimulationCorpusTest
{
    [Test]
    public void CompareLocalReplays()
    {
        var storage = Environment.GetEnvironmentVariable("BMS_REPLAY_CORPUS");
        if (string.IsNullOrEmpty(storage))
            Assert.Ignore("Set BMS_REPLAY_CORPUS to compare existing local replays.");

        using var realm = Realm.GetInstance(new RealmConfiguration(Path.Combine(storage!, "client.realm")) { IsReadOnly = true, SchemaVersion = 52 });
        using var files = new StorageFiles(Path.Combine(storage!, "files"));
        var candidates = realm.All<ScoreInfo>().ToArray().Where(score => score.Ruleset.ShortName == Constant.SHORT_NAME
            && score.BeatmapInfo != null && score.Files.Any(file => file.Filename == BmsReplayArchive.FILENAME)).OrderByDescending(score => score.Date).ToArray();
        var reports = new List<object>();
        var compactDifferences = 0;
        var cadenceDifferences = 0;

        foreach (var candidate in candidates.Take(24).Concat(candidates.TakeLast(24)).DistinctBy(score => score.ID))
        {
            var source = candidate.DeepClone();
            if (source.BeatmapInfo?.Path is not { } chartPath || !Constant.IsChartFile(chartPath)) continue;
            using var oldStream = files.GetStream(source.Files.Single(file => file.Filename == BmsReplayArchive.FILENAME).File.GetStoragePath());
            var originalBytes = oldStream?.Length;
            var archived = BmsReplayArchive.ReadScore(source, files);
            var beatmapFile = source.BeatmapInfo.BeatmapSet?.Files.FirstOrDefault(file => file.Filename == chartPath);
            if (beatmapFile == null) continue;
            using var stream = files.GetStream(beatmapFile.File.GetStoragePath());
            if (stream == null) continue;
            using var reader = new LineBufferedReader(stream);
            var decoded = Decoder.GetDecoder<Beatmap>(reader).Decode(reader);
            var mods = source.Mods;
            using var windows = BmsJudgementProfileProvider.UseWindowMods(mods.OfType<IApplicableToJudgementWindow>().ToArray());
            var playable = (BmsBeatmap)new TestWorkingBeatmap(decoded).GetPlayableBeatmap(new BmsRuleset().RulesetInfo, mods);
            var simulated = BmsReplaySimulation.Run(playable, archived.Replay, source);
            var compact = BmsTestReplayArchive.RoundTrip(archived);
            var restored = BmsReplaySimulation.Run(playable, compact.Replay, source);
            var sampled = BmsReplaySimulation.Run(playable, addIdleFrames(archived.Replay), source);
            var events = simulated.Judgements;
            var compactEvents = restored.Judgements;
            var gauges = simulated.Gauges;
            var compactGauges = restored.Gauges;
            var changedEvents = Math.Abs(events.Count - compactEvents.Count) + events.Zip(compactEvents).Count(pair => !equivalent(pair.First, pair.Second));
            var changedGauges = Math.Abs(gauges.Count - compactGauges.Count) + gauges.Zip(compactGauges).Count(pair =>
                pair.First.Time != pair.Second.Time || pair.First.ActiveGaugeType != pair.Second.ActiveGaugeType || !pair.First.States.SequenceEqual(pair.Second.States));
            compactDifferences += changedEvents + changedGauges;
            var changedCadenceEvents = Math.Abs(events.Count - sampled.Judgements.Count) + events.Zip(sampled.Judgements).Count(pair => !equivalent(pair.First, pair.Second));
            var changedCadenceGauges = Math.Abs(gauges.Count - sampled.Gauges.Count) + gauges.Zip(sampled.Gauges).Count(pair =>
                pair.First.Time != pair.Second.Time || pair.First.ActiveGaugeType != pair.Second.ActiveGaugeType || !pair.First.States.SequenceEqual(pair.Second.States));
            cadenceDifferences += changedCadenceEvents + changedCadenceGauges;
            var statistics = events.Where(entry => !entry.SuppressPenalty)
                .GroupBy(entry => entry.Result).ToDictionary(group => group.Key, group => group.Count());
            var summaryDifferences = source.Statistics.Keys.Union(statistics.Keys)
                .Where(key => source.Statistics.GetValueOrDefault(key) != statistics.GetValueOrDefault(key)).Select(key => key.ToString()).ToArray();
            using var compactArchive = BmsReplayArchive.Create(archived);
            reports.Add(new
            {
                source.ID,
                Title = source.BeatmapInfo.Metadata.Title,
                OriginalBytes = originalBytes,
                CompactBytes = compactArchive.Get(BmsReplayArchive.FILENAME).Length,
                Frames = archived.Replay.Frames.Count,
                Judgements = events.Count,
                SummaryDifferences = summaryDifferences,
                CompactEventDifferences = changedEvents,
                CompactGaugeDifferences = changedGauges,
                CadenceEventDifferences = changedCadenceEvents,
                CadenceGaugeDifferences = changedCadenceGauges,
            });
        }

        var output = Environment.GetEnvironmentVariable("BMS_REPLAY_CORPUS_REPORT");
        if (!string.IsNullOrEmpty(output))
            File.WriteAllText(output, JsonConvert.SerializeObject(reports, Formatting.Indented));
        TestContext.WriteLine($"Compared {reports.Count} replays. Archive differences: {compactDifferences}; idle frame differences: {cadenceDifferences}.");
        Assert.Multiple(() =>
        {
            Assert.That(reports, Is.Not.Empty);
            Assert.That(compactDifferences, Is.Zero);
            Assert.That(cadenceDifferences, Is.Zero);
        });
    }

    private static bool equivalent(BmsJudgementEvent left, BmsJudgementEvent right)
        => left.Source == right.Source && left.Result == right.Result && left.SuppressPenalty == right.SuppressPenalty
           && left.TimingObservations.SequenceEqual(right.TimingObservations);

    private static osu.Game.Replays.Replay addIdleFrames(osu.Game.Replays.Replay replay)
    {
        var frames = replay.Frames.OfType<BmsReplayFrame>().ToArray();
        var sampled = new osu.Game.Replays.Replay { HasReceivedAllFrames = replay.HasReceivedAllFrames };
        for (var i = 0; i < frames.Length; i++)
        {
            sampled.Frames.Add(frames[i]);
            if (i + 1 == frames.Length) continue;
            for (var time = frames[i].Time + 17.25; time < frames[i + 1].Time; time += 17.25)
                sampled.Frames.Add(new BmsReplayFrame(time, frames[i].Actions.ToArray()));
        }

        return sampled;
    }

    private sealed class StorageFiles(string root) : IResourceStore<byte[]>
    {
        public byte[] Get(string name) => File.ReadAllBytes(Path.Combine(root, name));
        public Stream GetStream(string name) => File.Exists(Path.Combine(root, name)) ? File.OpenRead(Path.Combine(root, name)) : null!;
        public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Get(name));
        public IEnumerable<string> GetAvailableResources() => [];
        public void Dispose() { }
    }
}
