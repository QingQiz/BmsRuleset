#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.IO.Stores;
using osu.Game.Extensions;
using osu.Game.IO;
using osu.Game.Skinning;
using Realms;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

internal static class BmsLifetimeLocalSkin
{
    internal static ISkinSource Create(IStorageResourceProvider resources, string storage)
    {
        using var realm = Realm.GetInstance(new RealmConfiguration(Path.Combine(storage, "client.realm")) { IsReadOnly = true, SchemaVersion = 52 });
        var info = realm.All<SkinInfo>().ToArray().Single(s => s.ID == Guid.Parse("e8a20a95-0928-4bd6-a296-fbfb0155e5fc"));
        var bytes = info.Files.ToDictionary(f => f.Filename, f => File.ReadAllBytes(Path.Combine(storage, "files", f.File.GetStoragePath())), StringComparer.OrdinalIgnoreCase);
        return new SkinProvidingContainer(new LocalSkin(resources, info.Name, new ByteStore(bytes)));
    }

    private sealed class LocalSkin(IStorageResourceProvider resources, string name, IResourceStore<byte[]> files)
        : LegacySkin(new SkinInfo(name, "Test"), resources, files);

    private sealed class ByteStore(Dictionary<string, byte[]> files) : IResourceStore<byte[]>
    {
        public byte[] Get(string name) => files.GetValueOrDefault(name)!;

        public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Get(name));

        public Stream GetStream(string name) => Get(name) is { } bytes ? new MemoryStream(bytes) : null!;

        public IEnumerable<string> GetAvailableResources() => files.Keys;

        public void Dispose()
        {
        }
    }
}
