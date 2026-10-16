#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.IO.Stores;
using osu.Game.Beatmaps;
using osu.Game.Extensions;
using osu.Game.Models;
using osu.Game.IO.Archives;
using osu.Game.IO.Serialization;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

internal static class BmsTestReplayArchive
{
    public static Score RoundTrip(Score original, Func<string, string>? editJson = null, bool legacy = false, string? chart = null)
    {
        using var archive = legacy ? CreateLegacy(original) : BmsReplayArchive.Create(original);
        var bytes = archive.Get(BmsReplayArchive.FILENAME);
        if (editJson != null)
        {
            using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            bytes = Encoding.UTF8.GetBytes(editJson(reader.ReadToEnd()));
        }

        // Score summaries and gauge mods belong to the database; inputs and algorithm belong to the replay.
        var target = original.ScoreInfo.DeepClone();
        target.Files.Clear();
        var file = new RealmFile { Hash = "abcdef" };
        target.Files.Add(new RealmNamedFileUsage(file, BmsReplayArchive.FILENAME));
        var resources = new Dictionary<string, byte[]> { [file.GetStoragePath()] = bytes };
        if (chart != null)
        {
            var chartFile = new RealmFile { Hash = "fedcba" };
            target.BeatmapInfo = new BeatmapInfo(new BmsRuleset().RulesetInfo) { Hash = chartFile.Hash, BeatmapSet = new BeatmapSetInfo() };
            target.BeatmapInfo.BeatmapSet.Files.Add(new RealmNamedFileUsage(chartFile, "test.bms"));
            resources[chartFile.GetStoragePath()] = Encoding.UTF8.GetBytes(chart);
        }
        using var store = new Store(resources);
        return BmsReplayArchive.ReadScore(target, store);
    }

    internal static ArchiveReader CreateLegacy(Score score)
    {
        var payload = new
        {
            Version = 3,
            score.Replay.HasReceivedAllFrames,
            score.Replay.Frames,
        };
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(payload.Serialize()));
        return new ByteArrayArchiveReader(stream.ToArray(), BmsReplayArchive.FILENAME);
    }

    private sealed class Store(Dictionary<string, byte[]> resources) : IResourceStore<byte[]>
    {
        public byte[] Get(string name) => resources.GetValueOrDefault(name)!;

        public Stream GetStream(string name) => new MemoryStream(Get(name));

        public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Get(name));

        public IEnumerable<string> GetAvailableResources() => resources.Keys;

        public void Dispose()
        {
        }
    }
}
