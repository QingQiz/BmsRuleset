#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.IO.Stores;
using osu.Game.Extensions;
using osu.Game.Models;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

internal static class BmsTestReplayArchive
{
    public static Score RoundTrip(Score original, Func<string, string>? editJson = null)
    {
        using var archive = BmsReplayArchive.Create(original);
        var bytes = archive.Get(BmsReplayArchive.FILENAME);
        if (editJson != null)
        {
            using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            bytes = Encoding.UTF8.GetBytes(editJson(reader.ReadToEnd()));
        }

        // Statistics belong to the score database; the archive owns input and derived observations.
        var target = original.ScoreInfo.DeepClone();
        target.Files.Clear();
        var file = new RealmFile { Hash = "abcdef" };
        target.Files.Add(new RealmNamedFileUsage(file, BmsReplayArchive.FILENAME));
        using var store = new Store(file.GetStoragePath(), bytes);
        return BmsReplayArchive.ReadScore(target, store);
    }

    private sealed class Store(string path, byte[] bytes) : IResourceStore<byte[]>
    {
        public byte[] Get(string name) => name == path ? bytes : null!;

        public Stream GetStream(string name) => new MemoryStream(Get(name));

        public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Get(name));

        public IEnumerable<string> GetAvailableResources() => [path];

        public void Dispose()
        {
        }
    }
}
