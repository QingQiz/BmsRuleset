using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Processing;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[TestFixture]
public class BmsonSamplePlaybackTest
{
    [TestCase(0.5)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task ClipPlaybackAndSeekUseSourceBoundsAtEveryRate(double rate)
    {
        var asset = assetWithForbiddenRegions(rate);
        var mixer = new BmsPcmVoiceMixer();
        using var controller = controllerFor(asset, mixer, rate, new Dictionary<int, BmsSampleDefinition>
        {
            [70000] = new("stem.wav", 200, 700),
        });
        Assert.That(controller.GetSampleLength(70000), Is.EqualTo(500));
        controller.Play(70000, 100, 100);
        var frames = toFrames(400 / rate);
        var output = BmsPcmTestHelpers.RenderFrames(mixer, frames + 100);
        Assert.That(output.Max(), Is.InRange(.09f, .101f), "Playback stays within the quiet source region.");
        Assert.That(output.Skip(frames * 2), Is.All.Zero, "The following slice must never leak through.");
        Assert.That(mixer.ActiveVoiceCount, Is.Zero);
        await controller.WaitForPreparedSamplesAsync(default, new Dictionary<int, double> { [70000] = 100 });
    }

    [Test]
    public void DifferentSlicesCanOverlapWhileTheSameSliceIsDeduplicated()
    {
        var asset = BmsPcmTestHelpers.CreateAsset([new BmsPcmChunk(0, 44100, Enumerable.Repeat(.1f, 88200).ToArray())]);
        var mixer = new BmsPcmVoiceMixer();
        using var controller = controllerFor(asset, mixer, 1, new Dictionary<int, BmsSampleDefinition>
        {
            [1] = new("stem.wav", 0, 600),
            [2] = new("stem.wav", 400, 800),
            [3] = new("stem.wav", 0, 600),
        });
        controller.QueueLivePlay(1, 100);
        controller.QueueLivePlay(1, 100);
        controller.QueueLivePlay(2, 100);
        controller.QueueLivePlay(3, 100);
        controller.SubmitLivePlayBatch();
        var output = BmsPcmTestHelpers.RenderFrames(mixer, 1000);
        Assert.That(mixer.ActiveVoiceCount, Is.EqualTo(3));
        Assert.That(output.Max(), Is.EqualTo(.3f).Within(.001));
    }

    [Test]
    public async Task ReadinessWaitsForTheActualSourceOffset()
    {
        var asset = new BmsPcmAsset(44100, 2);
        asset.Publish(new BmsPcmChunk(0, 8192, new float[8192 * 2]));
        asset.MarkReady();
        var mixer = new BmsPcmVoiceMixer();
        using var controller = controllerFor(asset, mixer, 1, new Dictionary<int, BmsSampleDefinition> { [1] = new("stem.wav", 500, 800) });
        Assert.That(controller.IsSampleReady(1), Is.False);
        Assert.That(controller.CanSchedule(1), Is.False);
        var wait = controller.WaitForPreparedSamplesAsync(default);
        Assert.That(wait.IsCompleted, Is.False);
        asset.Publish(new BmsPcmChunk(8192, 44100, new float[44100 * 2]));
        await wait.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(controller.IsSampleReady(1), Is.True);
        asset.Complete(8192 + 44100);
    }

    [Test]
    public void SourceLengthClampsFinalSliceAndExpiredSeekIsSilent()
    {
        var asset = BmsPcmTestHelpers.CreateAsset([new BmsPcmChunk(0, 44100, new float[88200])]);
        var mixer = new BmsPcmVoiceMixer();
        using var controller = controllerFor(asset, mixer, 1, new Dictionary<int, BmsSampleDefinition>
        {
            [1] = new("stem.wav", 800, 2000),
            [2] = new("stem.wav", 1000),
        });
        Assert.That(controller.GetSampleLength(1), Is.EqualTo(200));
        Assert.That(controller.GetSampleLength(2), Is.Zero);
        controller.Play(1, 100, 200);
        mixer.Render(new float[2]);
        Assert.That(mixer.ActiveVoiceCount, Is.Zero);
    }

    private static BmsPcmPlaybackController controllerFor(BmsPcmAsset asset, BmsPcmVoiceMixer mixer, double rate,
                                                          IReadOnlyDictionary<int, BmsSampleDefinition> definitions)
    {
        var controller = new BmsPcmPlaybackController(definitions, null, rate, null, new BindableDouble(1), () => 0, mixer);
        var resources = BmsPcmMaintenanceTest.Field<Dictionary<int, string>>(controller, "resolvedResources");
        var leases = BmsPcmMaintenanceTest.Field<Dictionary<int, BmsPcmAssetLease>>(controller, "leases");
        foreach (var (key, definition) in definitions)
        {
            resources[key] = definition.Path;
            leases[key] = new BmsPcmAssetLease(asset, Task.CompletedTask, () => { });
        }

        return controller;
    }

    private static BmsPcmAsset assetWithForbiddenRegions(double rate)
    {
        var samples = new float[toFrames(1000 / rate) * 2];
        for (var frame = 0; frame < samples.Length / 2; frame++)
        {
            var value = frame >= toFrames(200 / rate) && frame < toFrames(700 / rate) ? .1f : .8f;
            samples[frame * 2] = samples[frame * 2 + 1] = value;
        }

        return BmsPcmTestHelpers.CreateAsset([new BmsPcmChunk(0, samples.Length / 2, samples)]);
    }

    private static int toFrames(double milliseconds) => (int)Math.Round(milliseconds * BmsFixedRatePcmProcessor.OUTPUT_SAMPLE_RATE / 1000);
}
