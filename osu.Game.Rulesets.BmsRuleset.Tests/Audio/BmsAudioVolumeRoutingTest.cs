using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Graphics.Audio;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Playback;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[HeadlessTest]
public partial class BmsAudioVolumeRoutingTest : TestScene
{
    [Resolved]
    private AudioManager audioManager { get; set; } = null!;

    [TestCase(100, 0, false)]
    [TestCase(100, 200, true)]
    [TestCase(200, 200, false)]
    public void SeekResumesOnlyLoadedTracksThatAreStillActive(double offset, double length, bool expected)
    {
        Assert.That(BmsBackgroundAudioPlayer.ShouldResumeSampleAfterSeek(offset, length), Is.EqualTo(expected));
    }

    [TestCase(0, 99.999, false)]
    [TestCase(0, 100, true)]
    [TestCase(100, 0, false)]
    public void LateBackgroundEventRequiresReconstruction(double eventTime, double currentTime, bool expected)
    {
        Assert.That(BmsBackgroundAudioPlayer.IsEventTooLateForDirectStart(eventTime, currentTime), Is.EqualTo(expected));
    }

    [Test]
    public void SeekKeepsOnlyLatestEventPerKey()
    {
        BmsBackgroundAudioPlayer.BgmEvent[] events =
        [
            new(0, 1),
            new(1000, 1),
            new(1200, 2),
        ];

        var selected = BmsBackgroundAudioPlayer.SelectEventsForSeek(events, events.Length, 1500, 2000, _ => 2000);

        Assert.That(selected, Has.Count.EqualTo(2));
        Assert.That(selected, Has.Exactly(1).Matches<BmsBackgroundAudioPlayer.SeekedBgm>(item => item.Event.SampleKey == 1 && item.Event.Time == 1000));
        Assert.That(selected, Has.Exactly(1).Matches<BmsBackgroundAudioPlayer.SeekedBgm>(item => item.Event.SampleKey == 2));
    }

    [Test]
    public void PreviewTrackIgnoresOnlyAudioManagerAdjustments()
    {
        AddAssert("preview owner accepts wrapper adjustments", () =>
        {
            var ignoredAudioManager = new AudioAdjustments();
            ignoredAudioManager.Volume.Value = 0;
            ignoredAudioManager.Balance.Value = 1;
            ignoredAudioManager.Frequency.Value = 0;
            ignoredAudioManager.Tempo.Value = 0;

            var track = new TestPreviewTrack(ignoredAudioManager);
            track.Volume.Value = 0.8;
            track.Balance.Value = 0.1;
            track.Frequency.Value = 1.2;
            track.Tempo.Value = 0.9;
            track.BindAdjustments(ignoredAudioManager);

            assertAdjustments(track, 0.8, 0.1, 1.2, 0.9);

            var drawableTrack = new DrawableTrack(track, false);
            drawableTrack.Volume.Value = 0.5;
            drawableTrack.Balance.Value = 0.2;
            drawableTrack.Frequency.Value = 1.5;
            drawableTrack.Tempo.Value = 0.5;

            assertAdjustments(track, 0.4, 0.3, 1.8, 0.45);

            drawableTrack.Volume.Value = 0;
            drawableTrack.Balance.Value = -0.1;
            drawableTrack.Frequency.Value = 0;
            drawableTrack.Tempo.Value = 0;

            assertAdjustments(track, 0, 0, 0, 0);
            drawableTrack.Dispose();
            assertAdjustments(track, 0.8, 0.1, 1.2, 0.9);

            return true;
        });
    }

    [Test]
    public void PreviewOutputIsMutedInGameplayClockOnlyMode()
    {
        AddAssert("clock-only preview output is muted", () =>
        {
            var track = new BmsEventPreviewTrack(
                _ => new BmsEventPreviewTimeline([], BmsEventPreviewTimeline.DEFAULT_LENGTH),
                null,
                audioManager)
            {
                PlaybackMode = BmsPreviewTrackPlaybackMode.GameplayClockOnly,
            };
            return track.PreviewPlaybackGain == 0;
        });
    }

    [Test]
    public void PreviewMixerGainFollowsOwnerVolume()
    {
        AddAssert("preview mixer gain follows owner volume", () =>
        {
            var track = new TestPreviewTrack(new AudioAdjustments());
            track.Volume.Value = 0.4;
            return track.PreviewPlaybackGain == 0.4;
        });
    }

    private static void assertAdjustments(IAggregateAudioAdjustment adjustments, double volume, double balance, double frequency, double tempo)
    {
        Assert.That(adjustments.AggregateVolume.Value, Is.EqualTo(volume).Within(0.0000001));
        Assert.That(adjustments.AggregateBalance.Value, Is.EqualTo(balance).Within(0.0000001));
        Assert.That(adjustments.AggregateFrequency.Value, Is.EqualTo(frequency).Within(0.0000001));
        Assert.That(adjustments.AggregateTempo.Value, Is.EqualTo(tempo).Within(0.0000001));
    }

    private sealed class TestPreviewTrack(IAggregateAudioAdjustment audioManagerAdjustments) : BmsPreviewTrack(audioManagerAdjustments)
    {
        protected override void StartPlayback()
        {
        }

        protected override void StopPlayback()
        {
        }

        protected override void SeekPlayback(double seek, bool wasRunning)
        {
        }

        protected override void ResetPlayback()
        {
        }
    }
}
