using System.Collections.Generic;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Audio;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Playback;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[HeadlessTest]
public partial class TestBmsAudioVolumeRouting : TestScene
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
    public void PreviewEventTracksRetainInheritedZeroAdjustments()
    {
        AddAssert("event tracks retain inherited zero adjustments", () =>
        {
            var track = new TestPreviewTrack(new AudioAdjustments());
            var inherited = new AudioAdjustments();
            inherited.Volume.Value = 0.25;
            inherited.Balance.Value = -0.25;
            inherited.Frequency.Value = 0.75;
            inherited.Tempo.Value = 0.5;
            var audio = new AudioAdjustments();
            audio.BindAdjustments(inherited);

            track.BindPreviewAdjustments(audio, 40);
            assertAdjustments(audio, 0.1, -0.25, 0.75, 0.5);

            inherited.Volume.Value = 0;
            inherited.Balance.Value = 0.5;
            inherited.Frequency.Value = 0;
            inherited.Tempo.Value = 0;

            assertAdjustments(audio, 0, 0.5, 0, 0);
            return true;
        });
    }

    [Test]
    public void PreviewEventTracksMirrorOwnerAdjustments()
    {
        AddAssert("event tracks mirror owner adjustments", () =>
        {
            var track = new TestPreviewTrack(new AudioAdjustments());
            var audio = new AudioAdjustments();
            var adjustments = track.BindPreviewAdjustments(audio, 40);

            track.Volume.Value = 0;
            track.Balance.Value = -0.5;
            track.Frequency.Value = 0;
            track.Tempo.Value = 0;
            adjustments.Update();

            assertAdjustments(audio, 0, -0.5, 0, 0);

            track.Volume.Value = 0.5;
            track.Balance.Value = 0.25;
            track.Frequency.Value = 1.5;
            track.Tempo.Value = 0.75;
            adjustments.Update();

            assertAdjustments(audio, 0.2, 0.25, 1.5, 0.75);
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
            var audio = new AudioAdjustments();

            track.BindPreviewAdjustments(audio, 100);

            return audio.AggregateVolume.Value == 0;
        });
    }

    [Test]
    public void PreviewTrackVolumeIsIsolatedPerPlayback()
    {
        AddAssert("preview track playback volume is isolated", () =>
        {
            var track = new BmsEventPreviewTrack(
                _ => new BmsEventPreviewTimeline([], BmsEventPreviewTimeline.DEFAULT_LENGTH),
                null,
                audioManager);

            var first = new AudioAdjustments();
            var second = new AudioAdjustments();

            track.BindPreviewAdjustments(first, 40);
            track.BindPreviewAdjustments(second, 80);

            Assert.That(first.AggregateVolume.Value, Is.EqualTo(0.4));
            Assert.That(second.AggregateVolume.Value, Is.EqualTo(0.8));

            return true;
        });
    }

    private static void assertAdjustments(IAggregateAudioAdjustment adjustments, double volume, double balance, double frequency, double tempo)
    {
        Assert.That(adjustments.AggregateVolume.Value, Is.EqualTo(volume).Within(0.0000001));
        Assert.That(adjustments.AggregateBalance.Value, Is.EqualTo(balance).Within(0.0000001));
        Assert.That(adjustments.AggregateFrequency.Value, Is.EqualTo(frequency).Within(0.0000001));
        Assert.That(adjustments.AggregateTempo.Value, Is.EqualTo(tempo).Within(0.0000001));
    }

    private sealed class TestPreviewTrack : BmsPreviewTrack
    {
        public TestPreviewTrack(IAggregateAudioAdjustment audioManagerAdjustments)
            : base(audioManagerAdjustments)
        {
        }

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
