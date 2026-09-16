using NUnit.Framework;
using osu.Game.Online;
using osu.Game.Online.Spectator;
using osu.Game.Replays.Legacy;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Replays;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Replay;

[TestFixture]
public partial class BmsSpectatorFrameTest
{
    [OneTimeSetUp]
    public void SetUp()
    {
        BmsReplayPatcher.InstallOnce();
        Assert.That(BmsReplayPatcher.IsInstalled, Is.True);
    }

    [Test]
    public void TestBmsFramesDoNotScheduleSpectatorWork()
    {
        using var client = new TestSpectatorClient();

        for (var i = 0; i < 120; i++)
            client.HandleFrame(new BmsReplayFrame(i * 1000d / 60, BmsAction.Key1));

        Assert.That(client.HasPendingFrames, Is.False);
    }

    [Test]
    public void TestOtherReplayFramesStillScheduleSpectatorWork()
    {
        using var client = new TestSpectatorClient();
        client.HandleFrame(new LegacyReplayFrame(1000, 0, 0, ReplayButtonState.None));

        Assert.That(client.HasPendingFrames, Is.True);
    }

    private partial class TestSpectatorClient() : OnlineSpectatorClient(new EndpointConfiguration())
    {
        public bool HasPendingFrames => Scheduler.HasPendingTasks;
    }
}
