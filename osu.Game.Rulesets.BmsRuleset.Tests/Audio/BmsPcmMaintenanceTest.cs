using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[TestFixture]
public class BmsPcmMaintenanceTest
{
    [Test]
    public void UnchangedSampleLifetimesDoNotAllocate()
    {
        using var controller = CreateController(1000);
        // Warm tiered JIT and lazy length storage before checking steady-state allocations.
        for (var i = 0; i < 256; i++)
            controller.Update(0);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
            controller.Update(0);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(allocated, Is.Zero);
        Assert.That(controller.PreparedSampleKeys.Count(), Is.EqualTo(1000));
    }

    [Test]
    public void UnchangedOverBudgetCacheDoesNotAllocate()
    {
        var (cache, leases) = CreatePinnedCache(1000);
        using (cache)
        {
            cache.EvictUnused();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++)
                cache.EvictUnused();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
            Assert.That(leases.All(lease => lease.Asset.IsComplete), Is.True);
        }
    }

    [Test]
    public void ExpiredSamplesAreReleasedOnceAndLengthsRemainAvailable()
    {
        using var controller = CreateController(3);
        var ends = Field<Dictionary<ushort, double>>(controller, "lifetimeEnds");
        ends[0] = 10;
        ends[1] = 20;
        ends[2] = 30;
        controller.Update(20 + 1000d / 44100);
        Assert.That(controller.PreparedSampleKeys, Is.EquivalentTo((ushort[])[1, 2]));
        controller.Update(31);
        controller.Update(32);
        Assert.That(controller.PreparedSampleKeys, Is.Empty);
        Assert.That(controller.GetSampleLength(0), Is.GreaterThan(0));
    }

    [Test]
    public void CacheEvictsReleasedAssetsButPreservesReacquiredAssets()
    {
        var (cache, leases) = CreatePinnedCache(2);
        using (cache)
        {
            cache.EvictUnused();
            leases[0].Dispose();
            using var additional = cache.Acquire("0");
            cache.EvictUnused();
            Assert.That(additional.Asset.IsComplete, Is.True);
            additional.Dispose();
            cache.EvictUnused();
            Assert.That(additional.Asset.State, Is.EqualTo(BmsPcmAssetState.Disposed));
            Assert.That(leases[1].Asset.IsComplete, Is.True);
        }
    }

    [Test]
    public void MultiplePendingPlaysCanBecomeReadyInOneUpdate()
    {
        using var controller = CreateController(2);
        var resources = Field<Dictionary<ushort, string>>(controller, "resolvedResources");
        var leases = Field<Dictionary<ushort, BmsPcmAssetLease>>(controller, "leases");
        for (ushort key = 0; key < 2; key++)
        {
            resources[key] = key.ToString(System.Globalization.CultureInfo.InvariantCulture);
            leases[key].Dispose();
            leases[key] = new BmsPcmAssetLease(new BmsPcmAsset(44100, 2), Task.CompletedTask, () => { });
            controller.Play(key, 100, 0);
        }

        foreach (var lease in leases.Values)
        {
            lease.Asset.Publish(new BmsPcmChunk(0, 1, [0.1f, 0.1f]));
            lease.Asset.Complete(1);
        }

        controller.Update(0);
        Assert.That(Field<IDictionary>(controller, "pendingPlays"), Is.Empty);
        var output = new float[2];
        Field<BmsPcmVoiceMixer>(controller, "mixer").Render(output);
        Assert.That(output[0], Is.GreaterThan(0));
    }

    internal static BmsPcmPlaybackController CreateController(int count)
    {
        var controller = new BmsPcmPlaybackController(new Dictionary<ushort, string>(), null, 1, null,
            new BindableDouble(1), () => 0, new BmsPcmVoiceMixer());
        var leases = Field<Dictionary<ushort, BmsPcmAssetLease>>(controller, "leases");
        for (var i = 0; i < count; i++)
            leases.Add((ushort)i, new BmsPcmAssetLease(CreateAsset(), Task.CompletedTask, () => { }));
        return controller;
    }

    internal static (BmsPcmAssetCache Cache, BmsPcmAssetLease[] Leases) CreatePinnedCache(int count)
    {
        var cache = new BmsPcmAssetCache((_, _) => throw new InvalidOperationException("Unexpected resource load"), 1);
        var entries = Field<IDictionary>(cache, "entries");
        var entryType = entries.GetType().GenericTypeArguments[1];
        var leases = new BmsPcmAssetLease[count];
        for (var i = 0; i < count; i++)
        {
            var key = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            entries.Add(key, Activator.CreateInstance(entryType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, [CreateAsset()], null));
            leases[i] = cache.Acquire(key);
        }

        // Exercise the real eviction threshold without making the test reserve half a gigabyte.
        typeof(BmsPcmAssetCache).GetField("residentPcmBytes", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(cache, 513L * 1024 * 1024);
        return (cache, leases);
    }

    internal static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    internal static BmsPcmAsset CreateAsset() => BmsPcmTestHelpers.CreateAsset([new BmsPcmChunk(0, 1, [0.1f, 0.1f])]);
}
