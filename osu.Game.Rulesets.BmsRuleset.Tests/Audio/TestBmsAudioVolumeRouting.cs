using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Audio.Playback;
using osu.Game.Rulesets.BmsRuleset.Audio.Preview;
using osu.Game.Rulesets.BmsRuleset.Audio.Samples;

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
    public void GameplayTracksUseAggregateVolumeOnly()
    {
        AddAssert("gameplay Track volume uses aggregate volume", () =>
        {
            var store = new BmsSampleStore(new Dictionary<ushort, string>());
            typeof(BmsSampleStore)
                .GetProperty("audioManager", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(store, audioManager);

            var audio = new RecordingAudioComponent();
            typeof(BmsSampleStore)
                .GetMethod("bindTrackVolumeAdjustments", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(store, [audio, 100]);

            Assert.That(audio.RemovedProperties, Does.Contain(AdjustableProperty.Volume));
            Assert.That(audio.VolumeAdjustments, Has.Some.SameAs(audioManager.AggregateVolume));
            Assert.That(audio.VolumeAdjustments, Has.None.SameAs(audioManager.VolumeTrack));
            Assert.That(audio.VolumeAdjustments, Has.None.SameAs(audioManager.VolumeSample));

            return true;
        });
    }

    [Test]
    public void GameplayTrackVolumeAdjustmentIsPerPlayback()
    {
        AddAssert("gameplay Track volume uses separate bindables", () =>
        {
            var store = new BmsSampleStore(new Dictionary<ushort, string>());
            typeof(BmsSampleStore)
                .GetProperty("audioManager", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(store, audioManager);

            var bindMethod = typeof(BmsSampleStore)
                .GetMethod("bindTrackVolumeAdjustments", BindingFlags.Instance | BindingFlags.NonPublic)!;

            Assert.That(bindMethod.GetParameters(), Has.Length.EqualTo(2));

            var first = new RecordingAudioComponent();
            var second = new RecordingAudioComponent();

            bindMethod.Invoke(store, [first, 40]);
            bindMethod.Invoke(store, [second, 80]);

            Assert.That(first.VolumeAdjustments[0], Is.Not.SameAs(second.VolumeAdjustments[0]));
            Assert.That(first.VolumeAdjustments[0].Value, Is.EqualTo(0.4));
            Assert.That(second.VolumeAdjustments[0].Value, Is.EqualTo(0.8));

            return true;
        });
    }

    [Test]
    public void PreviewTracksUseTrackAggregateVolume()
    {
        AddAssert("preview volume adjustments use track aggregate volume", () =>
        {
            var track = new BmsEventPreviewTrack(() => [], new Dictionary<ushort, string>(), null, audioManager);
            var audio = new RecordingAudioComponent();

            typeof(BmsPreviewTrack)
                .GetMethod("BindPreviewAdjustments", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(track, [audio, 100]);

            Assert.That(audio.RemovedProperties, Does.Contain(AdjustableProperty.Volume));
            Assert.That(audio.BoundAdjustments, Is.SameAs(track));

            return true;
        });
    }

    [Test]
    public void PreviewOutputIsMutedInGameplayClockOnlyMode()
    {
        AddAssert("clock-only preview output is muted", () =>
        {
            var track = new BmsEventPreviewTrack(() => [], new Dictionary<ushort, string>(), null, audioManager)
            {
                PlaybackMode = BmsPreviewTrackPlaybackMode.GameplayClockOnly,
            };
            var audio = new RecordingAudioComponent();

            typeof(BmsPreviewTrack)
                .GetMethod("BindPreviewAdjustments", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(track, [audio, 100]);

            return audio.VolumeAdjustments.Exists(adjustment => adjustment.Value == 0);
        });
    }

    [Test]
    public void PreviewTrackVolumeAdjustmentIsPerPlayback()
    {
        AddAssert("preview track playback volume uses separate bindables", () =>
        {
            var track = new BmsEventPreviewTrack(() => [], new Dictionary<ushort, string>(), null, audioManager);

            var bindMethod = typeof(BmsPreviewTrack)
                .GetMethod("BindPreviewAdjustments", BindingFlags.Instance | BindingFlags.NonPublic)!;

            Assert.That(bindMethod.GetParameters(), Has.Length.EqualTo(2));

            var first = new RecordingAudioComponent();
            var second = new RecordingAudioComponent();

            bindMethod.Invoke(track, [first, 40]);
            bindMethod.Invoke(track, [second, 80]);

            Assert.That(first.VolumeAdjustments[0], Is.Not.SameAs(second.VolumeAdjustments[0]));
            Assert.That(first.VolumeAdjustments[0].Value, Is.EqualTo(0.4));
            Assert.That(second.VolumeAdjustments[0].Value, Is.EqualTo(0.8));

            return true;
        });
    }

    private sealed class RecordingAudioComponent : IAdjustableAudioComponent
    {
        public BindableNumber<double> Volume { get; } = new BindableDouble(1);

        public BindableNumber<double> Balance { get; } = new BindableDouble();

        public BindableNumber<double> Frequency { get; } = new BindableDouble(1);

        public BindableNumber<double> Tempo { get; } = new BindableDouble(1);

        public IBindable<double> AggregateVolume => Volume;

        public IBindable<double> AggregateBalance => Balance;

        public IBindable<double> AggregateFrequency => Frequency;

        public IBindable<double> AggregateTempo => Tempo;

        public List<AdjustableProperty> RemovedProperties { get; } = [];

        public List<IBindable<double>> VolumeAdjustments { get; } = [];

        public IAggregateAudioAdjustment BoundAdjustments { get; private set; } = null!;

        public void BindAdjustments(IAggregateAudioAdjustment component)
        {
            BoundAdjustments = component;
        }

        public void UnbindAdjustments(IAggregateAudioAdjustment component)
        {
        }

        public void AddAdjustment(AdjustableProperty type, IBindable<double> adjustBindable)
        {
            if (type == AdjustableProperty.Volume)
                VolumeAdjustments.Add(adjustBindable);
        }

        public void RemoveAdjustment(AdjustableProperty type, IBindable<double> adjustBindable)
        {
        }

        public void RemoveAllAdjustments(AdjustableProperty type) => RemovedProperties.Add(type);
    }
}
