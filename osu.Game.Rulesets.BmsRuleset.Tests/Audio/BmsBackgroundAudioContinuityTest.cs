using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Playback;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[TestFixture]
public class BmsBackgroundAudioContinuityTest
{
    private const BindingFlags private_instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly ManualClock source = new();
    private TestClock clock = null!;
    private BmsPcmPlaybackController controller = null!;
    private BmsPcmVoiceMixer mixer = null!;
    private BmsSamplePlayback playback = null!;
    private BmsBackgroundAudioPlayer background = null!;
    private readonly BindableBool disabled = new();
    private readonly BindableBool paused = new();

    [SetUp]
    public void SetUp()
    {
        source.CurrentTime = 0;
        source.IsRunning = true;
        disabled.Value = paused.Value = false;
        clock = new TestClock(source);
        mixer = new BmsPcmVoiceMixer();
        controller = new BmsPcmPlaybackController(new Dictionary<ushort, string>(), null, 1, null,
            new BindableDouble(1), () => source.CurrentTime, mixer);
        var leases = BmsPcmMaintenanceTest.Field<Dictionary<ushort, BmsPcmAssetLease>>(controller, "leases");
        var resources = BmsPcmMaintenanceTest.Field<Dictionary<ushort, string>>(controller, "resolvedResources");
        for (ushort key = 0; key < 2; key++)
        {
            resources[key] = $"sample-{key}";
            var asset = BmsPcmTestHelpers.CreateAsset([new BmsPcmChunk(0, 88200, Enumerable.Repeat(0.1f, 176400).ToArray())]);
            leases[key] = new BmsPcmAssetLease(asset, Task.CompletedTask, () => { });
        }

        // Drive the real controller and mixer synchronously so assertions measure PCM, not audio-thread timing.
        var session = new BmsPcmPlaybackSession(new Dictionary<ushort, string>(), null, 1, null, null!, () => source.CurrentTime);
        typeof(BmsPcmPlaybackSession).GetProperty(nameof(BmsPcmPlaybackSession.Controller), private_instance)!.SetValue(session, controller);
        playback = new BmsSamplePlayback(new Dictionary<ushort, string>());
        typeof(BmsSamplePlayback).GetField("playbackSession", private_instance)!.SetValue(playback, session);
    }

    [TearDown]
    public void TearDown()
    {
        background?.Dispose();
        playback.Dispose();
        paused.UnbindAll();
        disabled.UnbindAll();
    }

    [TestCase(20)]
    [TestCase(300)]
    public void ForwardCatchUpKeepsKeysoundTailAfterRecovery(double elapsed)
    {
        createBackground([]);
        advanceTo(0);
        controller.Play(0, 100, 0);
        assertSounding();

        clock.CatchingUp.Value = disabled.Value = true;
        advanceTo(10);
        assertSounding();
        clock.CatchingUp.Value = disabled.Value = false;
        advanceTo(10 + elapsed);
        assertSounding();
    }

    [Test]
    public void ForwardCatchUpStillPlaysTheLatestLiveKeysound()
    {
        createBackground([]);
        advanceTo(0);
        clock.CatchingUp.Value = disabled.Value = true;
        advanceTo(10);
        for (var i = 0; i < 1000; i++)
            controller.QueueLivePlay(0, 100);
        controller.SubmitLivePlayBatch();
        assertSounding();
        Assert.That(mixer.ActiveVoiceCount, Is.EqualTo(1), "A slow frame should emit the latest trigger, without replaying the entire burst.");
    }

    [Test]
    public void LateBackgroundEventDoesNotCutAnUnrelatedKeysound()
    {
        createBackground([new BmsBackgroundAudioPlayer.BgmEvent(100, 1)]);
        advanceTo(0);
        controller.Play(0, 100, 0);
        assertSounding();
        advanceTo(250);
        BmsPcmTestHelpers.RenderFrames(mixer, 441);
        Assert.That(mixer.ActiveVoiceCount, Is.EqualTo(2), "Recover the late BGM without discarding the key's tail.");
    }

    [Test]
    public void CatchUpResumesBackgroundEventsThatOccurredWhileBlocked()
    {
        createBackground([new BmsBackgroundAudioPlayer.BgmEvent(100, 1)]);
        advanceTo(0);
        clock.CatchingUp.Value = disabled.Value = true;
        advanceTo(200);
        clock.CatchingUp.Value = disabled.Value = false;
        advanceTo(250);
        assertSounding();
    }

    [Test]
    public void RewindStillDiscardsTheOldKeysound()
    {
        createBackground([]);
        advanceTo(1000);
        controller.Play(0, 100, 0);
        assertSounding();
        clock.IsRewinding = true;
        advanceTo(500);
        var output = BmsPcmTestHelpers.RenderFrames(mixer, 441);
        Assert.That(output[^1], Is.Zero);
    }

    [Test]
    public void ExplicitPauseStillSilencesAudioDuringCatchUp()
    {
        createBackground([]);
        advanceTo(0);
        controller.Play(0, 100, 0);
        assertSounding();
        clock.CatchingUp.Value = disabled.Value = true;
        advanceTo(10);
        paused.Value = true;
        advanceTo(10);
        Assert.That(BmsPcmTestHelpers.RenderFrames(mixer, 441), Is.All.Zero);
    }

    [Test]
    public void PauseDuringCatchUpStillResetsVoicesOnResume()
    {
        createBackground([]);
        advanceTo(0);
        controller.Play(0, 100, 0);
        assertSounding();
        clock.CatchingUp.Value = disabled.Value = true;
        advanceTo(10);
        paused.Value = true;
        advanceTo(10);
        paused.Value = false;
        advanceTo(20);
        clock.CatchingUp.Value = disabled.Value = false;
        advanceTo(500);
        Assert.That(BmsPcmTestHelpers.RenderFrames(mixer, 441)[^1], Is.Zero);
    }

    [Test]
    public void ForwardRecoveryDoesNotRestartConsumedBackground()
    {
        var leases = BmsPcmMaintenanceTest.Field<Dictionary<ushort, BmsPcmAssetLease>>(controller, "leases");
        leases[1].Dispose();
        var samples = Enumerable.Range(0, 88200).SelectMany(i => new[] { i / 88200f, i / 88200f }).ToArray();
        leases[1] = new BmsPcmAssetLease(BmsPcmTestHelpers.CreateAsset([new BmsPcmChunk(0, 88200, samples)]), Task.CompletedTask, () => { });
        createBackground([new BmsBackgroundAudioPlayer.BgmEvent(0, 1)]);
        advanceTo(0);
        BmsPcmTestHelpers.RenderFrames(mixer, 4410);
        clock.CatchingUp.Value = disabled.Value = true;
        advanceTo(200);
        clock.CatchingUp.Value = disabled.Value = false;
        advanceTo(500);
        var output = BmsPcmTestHelpers.RenderFrames(mixer, 441);
        Assert.That(output[^1], Is.EqualTo(4850 / 88200f).Within(0.00001), "A consumed BGM keeps its actual audio cursor.");
    }

    [Test]
    public void StoppedForwardSeekStillDiscardsKeysounds()
    {
        createBackground([]);
        advanceTo(0);
        controller.Play(0, 100, 0);
        assertSounding();
        source.IsRunning = false;
        advanceTo(500);
        Assert.That(BmsPcmTestHelpers.RenderFrames(mixer, 441)[^1], Is.Zero);
    }

    [TestCase(1000, 2000, 1900)]
    [TestCase(2000, 1000, 1100)]
    public void ExplicitRunningSeekSuppressesHistoricalKeysUntilTarget(double start, double target, double intermediate)
    {
        createBackground([]);
        advanceTo(start);
        controller.Play(0, 100, 0);
        assertSounding();
        background.NotifySeek(target);
        clock.IsRewinding = target < start;
        clock.CatchingUp.Value = disabled.Value = true;
        advanceTo(intermediate);
        controller.QueueLivePlay(0, 100);
        controller.SubmitLivePlayBatch();
        Assert.That(BmsPcmTestHelpers.RenderFrames(mixer, 441), Is.All.Zero);

        clock.IsRewinding = false;
        clock.CatchingUp.Value = disabled.Value = false;
        advanceTo(target);
        controller.QueueLivePlay(1, 100);
        controller.SubmitLivePlayBatch();
        assertSounding();
        Assert.That(mixer.ActiveVoiceCount, Is.EqualTo(1), "Only the new target's key should play.");
    }

    private void createBackground(BmsBackgroundAudioPlayer.BgmEvent[] events)
    {
        background = new BmsBackgroundAudioPlayer(events, paused) { Clock = clock };
        typeof(BmsBackgroundAudioPlayer).GetProperty("samplePlayback", private_instance)!.SetValue(background, playback);
        typeof(BmsBackgroundAudioPlayer).GetProperty("gameplayClock", private_instance)!.SetValue(background, clock);
        BmsPcmMaintenanceTest.Field<BindableBool>(background, "sourceIsPaused").BindTo(paused);
        BmsPcmMaintenanceTest.Field<IBindable<bool>>(background, "samplePlaybackDisabled").BindTo(disabled);
    }

    private void advanceTo(double time)
    {
        source.CurrentTime = time;
        clock.ProcessFrame();
        typeof(BmsBackgroundAudioPlayer).GetMethod("Update", private_instance)!.Invoke(background, null);
        controller.SubmitLivePlayBatch();
    }

    private void assertSounding()
    {
        var output = BmsPcmTestHelpers.RenderFrames(mixer, 441);
        Assert.That(output[^1], Is.GreaterThan(0.05), "Sound must survive after the 5 ms epoch-replacement fade.");
    }

    private sealed class TestClock(ManualClock sourceClock) : FramedClock(sourceClock), IFrameStableClock
    {
        public BindableBool CatchingUp { get; } = new();
        public IBindable<bool> IsCatchingUp => CatchingUp;
        public IBindable<bool> WaitingOnFrames { get; } = new BindableBool();
        public double StartTime => 0;
        public double GameplayStartTime => 0;
        public IAdjustableAudioComponent AdjustmentsFromMods { get; } = new AudioAdjustments();
        public IBindable<bool> IsPaused { get; } = new BindableBool();
        public bool IsRewinding { get; set; }
    }
}
