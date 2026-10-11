#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[TestFixture]
[NonParallelizable]
public class BmsPcmLeaseScheduleTest
{
    private const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int lengthReads;

    [TestCase(0.5)]
    [TestCase(1)]
    [TestCase(2)]
    public void CompletedSampleExpiresStrictlyAfterItsFinalTriggerAndOriginalDuration(double rate)
    {
        using var controller = createController(rate);
        var asset = BmsPcmTestHelpers.CreateAsset([new BmsPcmChunk(0, 441, Enumerable.Repeat(0.1f, 882).ToArray())]);
        var releases = 0;
        addLease(controller, 1, asset, () => releases++);
        ends(controller)[1] = 100;
        var expectedLength = 10 * rate;
        var end = 100 + expectedLength;

        controller.Update(0);
        controller.Update(end - 1);
        controller.Update(end);
        Assert.That(controller.PreparedSampleKeys, Is.EquivalentTo((ushort[])[1]));
        Assert.That(releases, Is.Zero);
        controller.Update(Math.BitIncrement(end));
        controller.Update(end + 1);
        Assert.That(controller.PreparedSampleKeys, Is.Empty);
        Assert.That(releases, Is.EqualTo(1));
        Assert.That(controller.GetSampleLength(1), Is.EqualTo(expectedLength));
    }

    [Test]
    public void PublishedOriginalDurationControlsExpiryRatherThanResampledFrameCount()
    {
        using var controller = createController(0.5);
        var asset = BmsPcmMaintenanceTest.CreateAsset();
        asset.SetOriginalDuration(250);
        addLease(controller, 1, asset);
        ends(controller)[1] = 10;
        controller.Update(0);
        controller.Update(260);
        Assert.That(controller.GetSampleLength(1), Is.EqualTo(250));
        Assert.That(controller.PreparedSampleKeys, Is.EquivalentTo((ushort[])[1]));
        controller.Update(Math.BitIncrement(260));
        Assert.That(controller.PreparedSampleKeys, Is.Empty);
        Assert.That(controller.GetSampleLength(1), Is.EqualTo(250));
    }

    [Test]
    public void PreparingAssetsRefreshDurationAndReleaseAfterCompletion()
    {
        using var controller = createController();
        var asset = new BmsPcmAsset(44100, 2);
        addLease(controller, 1, asset);
        ends(controller)[1] = 10;
        controller.Update(20);
        Assert.That(controller.PreparedSampleKeys, Is.EquivalentTo((ushort[])[1]));
        asset.Publish(new BmsPcmChunk(0, 441, Enumerable.Repeat(0.1f, 882).ToArray()));
        asset.MarkReady();
        asset.SetOriginalDuration(25);
        controller.Update(20);
        Assert.That(controller.GetSampleLength(1), Is.EqualTo(25));
        asset.Complete(441);
        controller.Update(35);
        Assert.That(controller.PreparedSampleKeys, Is.EquivalentTo((ushort[])[1]));
        controller.Update(Math.BitIncrement(35));
        Assert.That(controller.PreparedSampleKeys, Is.Empty);
    }

    [Test]
    public void CompletionAfterTheReleaseTimeDoesNotKeepAnExpiredLease()
    {
        using var controller = createController();
        var asset = new BmsPcmAsset(44100, 2);
        addLease(controller, 1, asset);
        ends(controller)[1] = 10;
        controller.Update(100);
        asset.Complete(0);
        controller.Update(100);
        Assert.That(controller.PreparedSampleKeys, Is.Empty);
    }

    [Test]
    public void NewLeaseIsCheckedBeforeThePreviousLeaseExpires()
    {
        var (cache, pins) = BmsPcmMaintenanceTest.CreatePinnedCache(2);
        using var controller = createController();
        typeof(BmsPcmPlaybackController).GetField("assetCache", instance)!.SetValue(controller, cache);
        var resources = BmsPcmMaintenanceTest.Field<Dictionary<ushort, string>>(controller, "resolvedResources");
        resources[0] = "0";
        resources[1] = "1";
        ends(controller)[0] = 100;
        ends(controller)[1] = 1;
        controller.PrepareSample(0);
        controller.Update(0);
        controller.PrepareSample(1);
        controller.Update(2);
        Assert.That(controller.PreparedSampleKeys, Is.EquivalentTo((ushort[])[0]));
        Assert.That(controller.GetSampleLength(1), Is.GreaterThan(0));
        foreach (var pin in pins) pin.Dispose();
    }

    [Test]
    public void CompletedAssetsWithoutAUsageScheduleKeepTheirLengthsAndLeases()
    {
        using var controller = createController();
        addLease(controller, 1, BmsPcmMaintenanceTest.CreateAsset());
        controller.Update(0);
        controller.Update(double.MaxValue);
        Assert.That(controller.PreparedSampleKeys, Is.EquivalentTo((ushort[])[1]));
        Assert.That(controller.MaxSampleLengthMilliseconds, Is.GreaterThan(0));
    }

    [Test]
    public void EarlierClockTimeKeepsFutureLeasesAndLaterJumpReleasesAllExpiredLeases()
    {
        using var controller = createController();
        for (ushort key = 0; key < 3; key++)
        {
            addLease(controller, key, BmsPcmMaintenanceTest.CreateAsset());
            ends(controller)[key] = 10 * (key + 1);
        }
        controller.Update(5);
        controller.Update(-100);
        controller.Update(10);
        Assert.That(controller.PreparedSampleKeys.Count(), Is.EqualTo(3));
        controller.Update(1000);
        Assert.That(controller.PreparedSampleKeys, Is.Empty);
        Assert.That(controller.MaxSampleLengthMilliseconds, Is.GreaterThan(0));
    }

    [Test]
    public void SeekReacquiresPreviouslyExpiredResourcesAndRebuildsTheirExpiry()
    {
        var chartTime = 0d;
        var (cache, pins) = BmsPcmMaintenanceTest.CreatePinnedCache(2);
        using var controller = new BmsPcmPlaybackController(new Dictionary<ushort, string>(), null, 1,
            [new BmsSampleUsage(0, 100, ResumeAfterSeek: true), new BmsSampleUsage(1, 20)],
            new BindableDouble(1), () => chartTime, new BmsPcmVoiceMixer());
        typeof(BmsPcmPlaybackController).GetField("assetCache", instance)!.SetValue(controller, cache);
        var resources = BmsPcmMaintenanceTest.Field<Dictionary<ushort, string>>(controller, "resolvedResources");
        resources[0] = "0";
        resources[1] = "1";
        var lifetimes = typeof(BmsPcmPlaybackController).GetMethod("createLifetimes", instance)!.Invoke(controller, null);
        typeof(BmsPcmPlaybackController).GetField("lifetimes", instance)!.SetValue(controller, lifetimes);
        controller.StopAll();
        controller.Update(0);
        chartTime = 101;
        controller.Update(chartTime);
        Assert.That(controller.PreparedSampleKeys, Is.Empty);
        chartTime = 0;
        controller.StopAll();
        controller.Update(chartTime);
        Assert.That(controller.PreparedSampleKeys, Is.EquivalentTo((ushort[])[0, 1]));
        controller.Update(21);
        Assert.That(controller.PreparedSampleKeys, Is.EquivalentTo((ushort[])[0]));
        controller.Update(101);
        Assert.That(controller.PreparedSampleKeys, Is.Empty);
        foreach (var pin in pins) pin.Dispose();
    }

    [Test]
    public void StableCompletedLeasesDoNotRepeatLengthReadsBeforeExpiry()
    {
        using var controller = BmsPcmMaintenanceTest.CreateController(1000);
        foreach (var key in controller.PreparedSampleKeys) ends(controller)[key] = 100;
        using var probe = new ScopedMethodProbe(typeof(BmsPcmPlaybackController).GetMethod("rememberSampleLength", instance)!,
            typeof(BmsPcmLeaseScheduleTest).GetMethod(nameof(observeLengthRead), BindingFlags.Static | BindingFlags.NonPublic)!);
        lengthReads = 0;
        controller.Update(0);
        Assert.That(lengthReads, Is.EqualTo(1000), "Confirm that the probe sees the initial duration reads.");
        lengthReads = 0;
        for (var i = 0; i < 64; i++) controller.Update(i);
        Assert.That(lengthReads, Is.Zero, "Completed resources have no new duration or expiry information in this interval.");
        controller.Update(101);
        Assert.That(controller.PreparedSampleKeys, Is.Empty);
        Assert.That(lengthReads, Is.EqualTo(1000));
    }

    private static void observeLengthRead() => lengthReads++;

    private static BmsPcmPlaybackController createController(double rate = 1)
        => new(new Dictionary<ushort, string>(), null, rate, null, new BindableDouble(1), () => 0, new BmsPcmVoiceMixer());

    private static Dictionary<ushort, double> ends(BmsPcmPlaybackController controller)
        => BmsPcmMaintenanceTest.Field<Dictionary<ushort, double>>(controller, "lifetimeEnds");

    private static void addLease(BmsPcmPlaybackController controller, ushort key, BmsPcmAsset asset, Action? release = null)
        => BmsPcmMaintenanceTest.Field<Dictionary<ushort, BmsPcmAssetLease>>(controller, "leases")
            .Add(key, new BmsPcmAssetLease(asset, Task.CompletedTask, release ?? (() => { })));
}
