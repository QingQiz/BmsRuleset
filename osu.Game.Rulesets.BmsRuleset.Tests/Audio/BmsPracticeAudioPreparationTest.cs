#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice.Audio;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[TestFixture]
public class BmsPracticeAudioPreparationTest
{
    [Test]
    public void SelectionKeepsOnlyActiveLatestTriggersAndUnknownDurations()
    {
        var preparation = new BmsPracticeAudioPreparation(
        [
            new(1, 0, ResumeAfterSeek: true), new(1, 800, ResumeAfterSeek: true),
            new(2, 0, ResumeAfterSeek: true), new(3, 1000, ResumeAfterSeek: true),
            new(4, 0, ResumeAfterSeek: true), new(5, 0, ResumeAfterSeek: true),
            new(6, 1001, ResumeAfterSeek: true), new(7, 500),
        ]);
        preparation.SetDurations(new Dictionary<int, BmsSampleDuration>
        {
            [1] = new(BmsSampleDurationState.Known, 500),
            [2] = new(BmsSampleDurationState.Known, 1000),
            [3] = new(BmsSampleDurationState.Known, 500),
            [4] = new(BmsSampleDurationState.Unknown),
            [5] = new(BmsSampleDurationState.Failed),
        });
        var offsets = preparation.GetResumeOffsets(1000);
        Assert.That(offsets.Keys, Is.EquivalentTo((ushort[])[1, 3, 4]));
        Assert.That(offsets[1], Is.EqualTo(200));
        Assert.That(offsets[3], Is.Zero);
        Assert.That(offsets[4], Is.EqualTo(1000));
        Assert.That(preparation.GetResumeOffsets(1400).Keys,
            Is.EquivalentTo(new[] { 3, 4, 6 }), "Unknown durations remain candidates until PCM preparation resolves them.");
    }

    [TestCase(0.5)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task PreparationWaitsForTheResumeOffsetAndPlaybackSurvives(double rate)
    {
        var chartTime = 1000d;
        var mixer = new BmsPcmVoiceMixer();
        using var controller = new BmsPcmPlaybackController(new Dictionary<int, BmsSampleDefinition>(), null, rate,
            [new BmsSampleUsage(1, 0, ResumeAfterSeek: true)], new BindableDouble(1), () => chartTime, mixer);
        var asset = new BmsPcmAsset(44100, 2);
        asset.SetOriginalDuration(2000);
        publish(8192);
        asset.MarkReady();
        addAsset(controller, 1, asset);
        var prepared = controller.WaitForPreparedSamplesAsync(default, new Dictionary<int, double> { [1] = chartTime });
        Assert.That(prepared.IsCompleted, Is.False, "An initial buffer does not cover a later seek offset.");
        publish((int)(88200 / rate) - 8192);
        asset.Complete(asset.PublishedFrameCount);
        await prepared.WaitAsync(TimeSpan.FromSeconds(5));
        controller.Play(1, 100, 1000);
        Assert.That(BmsPcmTestHelpers.RenderFrames(mixer, 441).Last(), Is.GreaterThan(0));

        void publish(int frames) => asset.Publish(new BmsPcmChunk(asset.PublishedFrameCount, frames,
            Enumerable.Repeat(0.1f, frames * 2).ToArray()));
    }

    [Test]
    public void UndecodedSeekIsRetriedAtTheAdvancedPlaybackPosition()
    {
        var chartTime = 1000d;
        var mixer = new BmsPcmVoiceMixer();
        using var controller = new BmsPcmPlaybackController(new Dictionary<int, BmsSampleDefinition>(), null, 1,
            null, new BindableDouble(1), () => chartTime, mixer);
        var asset = new BmsPcmAsset(44100, 2);
        asset.SetOriginalDuration(2000);
        asset.Publish(new BmsPcmChunk(0, 8192, new float[16384]));
        asset.MarkReady();
        addAsset(controller, 1, asset);
        controller.Play(1, 100, 1000);
        Assert.That(BmsPcmTestHelpers.RenderFrames(mixer, 441), Is.All.Zero);
        chartTime = 1100;
        controller.Update(chartTime);
        Assert.That(mixer.ActiveVoiceCount, Is.Zero);
        var rest = Enumerable.Repeat(0.1f, (88200 - 8192) * 2).ToArray();
        // Only the position after the decoding delay is audible.
        Array.Clear(rest, 0, (48510 - 8192) * 2);
        asset.Publish(new BmsPcmChunk(8192, 88200 - 8192, rest));
        asset.Complete(88200);
        controller.Update(chartTime);
        Assert.That(BmsPcmTestHelpers.RenderFrames(mixer, 441).Last(), Is.GreaterThan(0));
    }

    [Test]
    public async Task PreparationCanBeCancelledWhileWaitingForTheSeekBuffer()
    {
        using var controller = new BmsPcmPlaybackController(new Dictionary<int, BmsSampleDefinition>(), null, 1,
            [new BmsSampleUsage(1, 0, ResumeAfterSeek: true)], new BindableDouble(1), () => 1000, new BmsPcmVoiceMixer());
        var asset = new BmsPcmAsset(44100, 2);
        asset.Publish(new BmsPcmChunk(0, 8192, new float[16384]));
        asset.MarkReady();
        addAsset(controller, 1, asset);
        using var cancellation = new CancellationTokenSource();
        var wait = controller.WaitForPreparedSamplesAsync(cancellation.Token, new Dictionary<int, double> { [1] = 1000 });
        cancellation.Cancel();
        try
        {
            await wait;
            Assert.Fail("Preparation should observe cancellation.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void addAsset(BmsPcmPlaybackController controller, int key, BmsPcmAsset asset)
    {
        BmsPcmMaintenanceTest.Field<Dictionary<int, BmsSampleDefinition>>(controller, "sampleDefinitions")[key] = new("sample");
        BmsPcmMaintenanceTest.Field<Dictionary<int, string>>(controller, "resolvedResources")[key] = "sample";
        BmsPcmMaintenanceTest.Field<Dictionary<int, BmsPcmAssetLease>>(controller, "leases")[key]
            = new BmsPcmAssetLease(asset, Task.CompletedTask, () => { });
    }
}
