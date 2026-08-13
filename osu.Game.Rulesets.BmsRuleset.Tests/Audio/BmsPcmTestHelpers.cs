using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Processing;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

internal static class BmsPcmTestHelpers
{
    internal static BmsPcmAsset CreateAsset(IEnumerable<BmsPcmChunk> chunks, int sampleRate = 44100, int channels = 2)
    {
        var asset = new BmsPcmAsset(sampleRate, channels);
        var totalFrames = 0L;

        foreach (var chunk in chunks)
        {
            asset.Publish(chunk);
            totalFrames += chunk.FrameCount;
        }

        asset.Complete(totalFrames);
        return asset;
    }

    internal static BmsPcmChunk[] ProcessChunks(BmsFixedRatePcmProcessor processor, CancellationToken cancellationToken = default) =>
        processor.ProcessChunks(cancellationToken).ToArray();

    internal static async Task WaitForCompletionAsync(BmsPcmAsset asset)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (asset.State is BmsPcmAssetState.Preparing or BmsPcmAssetState.Ready)
            await Task.Delay(1, timeout.Token);

        if (!asset.IsComplete)
            throw new InvalidOperationException($"PCM processing ended in state {asset.State}.");
    }

    internal static float[] RenderFrames(BmsPcmVoiceMixer mixer, int frameCount, int blockFrames = 1024)
    {
        if (frameCount < 0)
            throw new ArgumentOutOfRangeException(nameof(frameCount));

        if (blockFrames <= 0)
            throw new ArgumentOutOfRangeException(nameof(blockFrames));

        var output = new float[checked(frameCount * 2)];
        var rendered = 0;

        while (rendered < frameCount)
        {
            var count = Math.Min(blockFrames, frameCount - rendered);
            mixer.Render(output.AsSpan(rendered * 2, count * 2));
            rendered += count;
        }

        return output;
    }
}
