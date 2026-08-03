using System.Reflection;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Platform;
using osu.Framework.Threading;
using osu.Game.Rulesets.BmsRuleset.Configuration;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Configuration;

[TestFixture]
public class BmsFrameRateUnlockTest
{
    [Test]
    public void TestUnlockRestoresOriginalHostStateAfterLastLease()
    {
        using var host = new HeadlessGameHost();
        setInputThread(host, new InputThread { ActiveHz = 1000 });
        host.AllowBenchmarkUnlimitedFrames = false;

        var firstLease = BmsFrameRateUnlock.Acquire(host);
        var secondLease = BmsFrameRateUnlock.Acquire(host);

        Assert.That(host.AllowBenchmarkUnlimitedFrames, Is.True);
        Assert.That(host.InputThread.ActiveHz, Is.Zero);

        firstLease.Dispose();
        Assert.That(host.AllowBenchmarkUnlimitedFrames, Is.True);
        Assert.That(host.InputThread.ActiveHz, Is.Zero);

        secondLease.Dispose();
        Assert.That(host.AllowBenchmarkUnlimitedFrames, Is.False);
        Assert.That(host.InputThread.ActiveHz, Is.EqualTo(1000));
    }

    [Test]
    public void TestUnlockPreservesAlreadyUnlockedHostState()
    {
        using var host = new HeadlessGameHost
        {
            AllowBenchmarkUnlimitedFrames = true,
        };
        setInputThread(host, new InputThread { ActiveHz = 1000 });

        using (BmsFrameRateUnlock.Acquire(host))
        {
            Assert.That(host.AllowBenchmarkUnlimitedFrames, Is.True);
            Assert.That(host.InputThread.ActiveHz, Is.Zero);
        }

        Assert.That(host.AllowBenchmarkUnlimitedFrames, Is.True);
        Assert.That(host.InputThread.ActiveHz, Is.EqualTo(1000));
    }

    [Test]
    public void TestInputUnlockIsAppliedAfterFrameLimiterRecalculation()
    {
        using var host = new HeadlessGameHost();
        setInputThread(host, new InputThread { ActiveHz = 1000 });

        using (BmsFrameRateUnlock.Acquire(host, updateFrameSyncMode: () => host.InputThread.ActiveHz = 1000))
            Assert.That(host.InputThread.ActiveHz, Is.Zero);

        Assert.That(host.InputThread.ActiveHz, Is.EqualTo(1000));
    }

    [Test]
    public void TestInputRateFollowsExecutionMode()
    {
        using var host = new HeadlessGameHost();
        setInputThread(host, new InputThread { ActiveHz = 1000 });
        var executionMode = new Bindable<ExecutionMode>(ExecutionMode.MultiThreaded);

        using (BmsFrameRateUnlock.Acquire(host, executionMode))
        {
            Assert.That(host.InputThread.ActiveHz, Is.Zero);

            executionMode.Value = ExecutionMode.SingleThread;
            Assert.That(host.InputThread.ActiveHz, Is.EqualTo(host.MaximumUpdateHz));

            executionMode.Value = ExecutionMode.MultiThreaded;
            Assert.That(host.InputThread.ActiveHz, Is.Zero);

            host.InputThread.ActiveHz = 1000;
            host.InputThread.Scheduler.Update();
            Assert.That(host.InputThread.ActiveHz, Is.Zero);
        }

        Assert.That(host.InputThread.ActiveHz, Is.EqualTo(1000));
    }

    [Test]
    public void TestInputRateRestoresWhenUnlockStartsInSingleThreadMode()
    {
        using var host = new HeadlessGameHost();
        setInputThread(host, new InputThread { ActiveHz = host.MaximumUpdateHz });
        var executionMode = new Bindable<ExecutionMode>(ExecutionMode.SingleThread);

        using (BmsFrameRateUnlock.Acquire(host, executionMode))
        {
            Assert.That(host.InputThread.ActiveHz, Is.EqualTo(host.MaximumUpdateHz));

            executionMode.Value = ExecutionMode.MultiThreaded;
            Assert.That(host.InputThread.ActiveHz, Is.Zero);
        }

        Assert.That(host.InputThread.ActiveHz, Is.EqualTo(1000));
    }

    [Test]
    public void TestInputRemainsUnlockedAfterFrameLimiterChangesDuringGameplay()
    {
        using var host = new HeadlessGameHost();
        setInputThread(host, new InputThread { ActiveHz = 1000 });
        var frameSyncMode = new Bindable<FrameSync>(FrameSync.Limit2x);

        using (BmsFrameRateUnlock.Acquire(host, frameSyncMode: frameSyncMode))
        {
            Assert.That(host.InputThread.ActiveHz, Is.Zero);

            frameSyncMode.Value = FrameSync.Limit8x;

            // GameHost's frame limiter recalculation restores the framework input cap.
            host.InputThread.ActiveHz = 1000;
            host.InputThread.Scheduler.Update();

            Assert.That(host.InputThread.ActiveHz, Is.Zero);
        }

        Assert.That(frameSyncMode.Value, Is.EqualTo(FrameSync.Limit8x));
    }

    [Test]
    public void TestDisablingUnlockUsesLatestFrameLimiterRateInSingleThreadMode()
    {
        using var host = new HeadlessGameHost();
        setInputThread(host, new InputThread { ActiveHz = host.MaximumUpdateHz });
        var executionMode = new Bindable<ExecutionMode>(ExecutionMode.SingleThread);
        var lease = BmsFrameRateUnlock.Acquire(host, executionMode);

        setMaximumUpdateHz(host, 480);
        lease.Dispose();

        Assert.That(host.AllowBenchmarkUnlimitedFrames, Is.False);
        Assert.That(host.InputThread.ActiveHz, Is.EqualTo(480));
    }

    private static void setInputThread(GameHost host, InputThread inputThread)
    {
        var setter = typeof(GameHost).GetProperty(nameof(GameHost.InputThread), BindingFlags.Instance | BindingFlags.Public)?.SetMethod;
        Assert.That(setter, Is.Not.Null);
        setter.Invoke(host, [inputThread]);
    }

    private static void setMaximumUpdateHz(GameHost host, double value)
    {
        var field = typeof(GameHost).GetField("maximumUpdateHz", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field.SetValue(host, value);
    }
}
