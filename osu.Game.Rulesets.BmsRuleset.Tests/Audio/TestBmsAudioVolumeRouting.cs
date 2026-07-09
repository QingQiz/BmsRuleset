using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Audio;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[HeadlessTest]
public partial class TestBmsAudioVolumeRouting : TestScene
{
    [Resolved]
    private AudioManager audioManager { get; set; } = null!;

    [Test]
    public void BackgroundSamplesUseAggregateVolumeOnly()
    {
        AddAssert("BGM volume adjustments use aggregate volume", () =>
        {
            var player = new BmsBackgroundAudioPlayer([], new BindableBool());
            typeof(BmsBackgroundAudioPlayer)
                .GetProperty("audioManager", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(player, audioManager);

            var audio = new RecordingAudioComponent();
            typeof(BmsBackgroundAudioPlayer)
                .GetMethod("bindBgmVolumeAdjustments", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(player, [audio, 100]);

            Assert.That(audio.RemovedProperties, Does.Contain(AdjustableProperty.Volume));
            Assert.That(audio.VolumeAdjustments, Has.Some.SameAs(audioManager.AggregateVolume));
            Assert.That(audio.VolumeAdjustments, Has.None.SameAs(audioManager.VolumeTrack));
            Assert.That(audio.VolumeAdjustments, Has.None.SameAs(audioManager.VolumeSample));

            return true;
        });
    }

    [Test]
    public void BackgroundSampleVolumeAdjustmentIsPerPlayback()
    {
        AddAssert("BGM playback volume uses separate bindables", () =>
        {
            var player = new BmsBackgroundAudioPlayer([], new BindableBool());
            typeof(BmsBackgroundAudioPlayer)
                .GetProperty("audioManager", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(player, audioManager);

            var bindMethod = typeof(BmsBackgroundAudioPlayer)
                .GetMethod("bindBgmVolumeAdjustments", BindingFlags.Instance | BindingFlags.NonPublic)!;

            Assert.That(bindMethod.GetParameters(), Has.Length.EqualTo(2));

            var first = new RecordingAudioComponent();
            var second = new RecordingAudioComponent();

            bindMethod.Invoke(player, [first, 40]);
            bindMethod.Invoke(player, [second, 80]);

            Assert.That(first.VolumeAdjustments[0], Is.Not.SameAs(second.VolumeAdjustments[0]));
            Assert.That(first.VolumeAdjustments[0].Value, Is.EqualTo(0.4));
            Assert.That(second.VolumeAdjustments[0].Value, Is.EqualTo(0.8));

            return true;
        });
    }

    [Test]
    public void ChartSampleVolumeAdjustmentIsPerPlayback()
    {
        AddAssert("chart sample playback volume uses separate bindables", () =>
        {
            var sample = new BmsChartSampleSound();
            typeof(BmsChartSampleSound)
                .GetProperty("audioManager", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(sample, audioManager);

            var bindMethod = typeof(BmsChartSampleSound)
                .GetMethod("bindChartAudioAdjustments", BindingFlags.Instance | BindingFlags.NonPublic)!;

            Assert.That(bindMethod.GetParameters(), Has.Length.EqualTo(2));

            var first = new RecordingAudioComponent();
            var second = new RecordingAudioComponent();

            bindMethod.Invoke(sample, [first, 40]);
            bindMethod.Invoke(sample, [second, 80]);

            Assert.That(first.VolumeAdjustments[0], Is.Not.SameAs(second.VolumeAdjustments[0]));
            Assert.That(first.VolumeAdjustments[0].Value, Is.EqualTo(0.4));
            Assert.That(second.VolumeAdjustments[0].Value, Is.EqualTo(0.8));

            return true;
        });
    }

    [Test]
    public void PreviewSamplesUseAggregateVolumeOnly()
    {
        AddAssert("preview volume adjustments use aggregate volume", () =>
        {
            var track = new BmsPreviewTrack([], new Dictionary<ushort, string>(), null, audioManager);
            var audio = new RecordingAudioComponent();

            typeof(BmsPreviewTrack)
                .GetMethod("bindPreviewVolumeAdjustments", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(track, [audio]);

            Assert.That(audio.RemovedProperties, Does.Contain(AdjustableProperty.Volume));
            Assert.That(audio.VolumeAdjustments, Has.Some.SameAs(audioManager.AggregateVolume));
            Assert.That(audio.VolumeAdjustments, Has.None.SameAs(audioManager.VolumeTrack));
            Assert.That(audio.VolumeAdjustments, Has.None.SameAs(audioManager.VolumeSample));

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

        public void BindAdjustments(IAggregateAudioAdjustment component)
        {
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
