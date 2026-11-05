#nullable enable

using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice.Audio;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[TestFixture]
public class BmsSampleDurationCacheTest
{
    [Test]
    public async Task RepeatedAndConcurrentAttemptsShareResourceQueries()
    {
        var calls = 0;
        var cache = new BmsSampleDurationCache(_ =>
        {
            Interlocked.Increment(ref calls);
            return new BmsSampleDuration(BmsSampleDurationState.Known, 2000);
        });
        var first = cache.GetAsync(["same.wav", "same.wav"], default);
        var second = cache.GetAsync(["same.wav"], default);
        await Task.WhenAll(first, second);
        var retry = await cache.GetAsync(["same.wav"], default);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(retry["same.wav"].Milliseconds, Is.EqualTo(2000));
    }

    [Test]
    public async Task CancellingAnAttemptDoesNotCancelTheSharedQuery()
    {
        var calls = 0;
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new BmsSampleDurationCache(_ =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException();
            return new BmsSampleDuration(BmsSampleDurationState.Known, 2000);
        });
        using var cancellation = new CancellationTokenSource();
        var first = cache.GetAsync(["same.wav"], cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            Assert.ThrowsAsync<TaskCanceledException>(async () => await first);
            var second = cache.GetAsync(["same.wav"], default);
            release.Set();
            Assert.That((await second)["same.wav"].Milliseconds, Is.EqualTo(2000));
            Assert.That(calls, Is.EqualTo(1));
        }
        finally
        {
            release.Set();
        }
    }

    [Test]
    public async Task UnknownAndFailedProbesRemainDistinctAndAreCached()
    {
        var calls = 0;
        var cache = new BmsSampleDurationCache(path =>
        {
            Interlocked.Increment(ref calls);
            return new BmsSampleDuration(path == "unknown" ? BmsSampleDurationState.Unknown : BmsSampleDurationState.Failed);
        });
        await cache.GetAsync(["unknown", "failed"], default);
        var result = await cache.GetAsync(["unknown", "failed"], default);
        Assert.That(result["unknown"].State, Is.EqualTo(BmsSampleDurationState.Unknown));
        Assert.That(result["failed"].State, Is.EqualTo(BmsSampleDurationState.Failed));
        Assert.That(calls, Is.EqualTo(2));
    }

    [TestCase(0, false)]
    [TestCase(88200, true)]
    public void FlacDurationRequiresOnlyStreamInfo(long samples, bool known)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bms-duration-{Guid.NewGuid():N}.flac");
        try
        {
            var header = new byte[42];
            "fLaC"u8.CopyTo(header);
            header[4] = 0x80;
            header[7] = 34;
            BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(18), (44100UL << 44) | (ulong)samples);
            File.WriteAllBytes(path, header);
            // No encoded audio frames exist in this file; probing must not decode it.
            var duration = BmsSampleDurationCache.Probe(path);
            Assert.That(duration.State, Is.EqualTo(known ? BmsSampleDurationState.Known : BmsSampleDurationState.Unknown));
            if (known)
                Assert.That(duration.Milliseconds, Is.EqualTo(2000));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
