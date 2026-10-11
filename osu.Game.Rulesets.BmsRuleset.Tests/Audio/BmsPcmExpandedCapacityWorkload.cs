using System;
using System.Reflection;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

internal static class BmsPcmExpandedCapacityWorkload
{
    internal static Expansion Prepare(int peakVoices)
    {
        if (peakVoices < 512 || peakVoices > 8192 || peakVoices % 512 != 0)
            throw new ArgumentOutOfRangeException(nameof(peakVoices));

        var mixer = new BmsPcmVoiceMixer();
        var pulse = BmsPcmTestHelpers.CreateAsset([new BmsPcmChunk(0, 2, [.001f, -.0007f, .001f, -.0007f])]);
        var batch = new BmsVoicePlay[512];
        // Separate reservations retain multiple capacity segments; one large batch
        // would create just one additional segment regardless of the requested peak.
        for (var start = 0; start < peakVoices; start += batch.Length)
        {
            for (var voice = 0; voice < batch.Length; voice++)
                batch[voice] = new BmsVoicePlay(pulse, new BmsTerminationDomain((ushort)(start + voice)), 0);
            mixer.SubmitPlayBatch(batch);
        }

        var output = new float[512 * 2];
        mixer.Render(output.AsSpan(0, 2));
        var peak = ReadCapacity(mixer);
        if (mixer.ActiveVoiceCount != peakVoices || peak.OccupiedVoices != peakVoices)
            throw new InvalidOperationException("The requested expansion voices did not all reach the callback.");

        mixer.Render(output.AsSpan(2));
        var retired = ReadCapacity(mixer);
        if (mixer.ActiveVoiceCount != 0 || retired.OccupiedVoices != 0
            || retired.VoiceCapacity != peakVoices || retired.Segments != peakVoices / 512)
            throw new InvalidOperationException("Expansion must leave the requested empty capacity available for reuse.");
        return new Expansion(mixer, peak, retired);
    }

    internal static void SubmitSparseVoices(BmsPcmVoiceMixer mixer, BmsPcmAsset asset, int voices)
    {
        var plays = new BmsVoicePlay[voices];
        for (var voice = 0; voice < voices; voice++)
            plays[voice] = new BmsVoicePlay(asset, new BmsTerminationDomain((ushort)voice), mixer.RenderedFrames,
                SourceOffsetFrame: voice * 7);
        mixer.SubmitPlayBatch(plays);
    }

    internal static Capacity ReadCapacity(BmsPcmVoiceMixer mixer)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var segment = typeof(BmsPcmVoiceMixer).GetField("firstVoiceSegment", flags)!.GetValue(mixer);
        var segments = 0;
        var capacity = 0;
        var occupiedSegments = 0;
        var occupiedVoices = 0;
        while (segment != null)
        {
            var type = segment.GetType();
            var count = (int)type.GetField("ActiveCount", flags)!.GetValue(segment)!;
            capacity += ((Array)type.GetField("Voices", flags)!.GetValue(segment)!).Length;
            segments++;
            occupiedVoices += count;
            if (count > 0)
                occupiedSegments++;
            segment = type.GetField("Next", flags)!.GetValue(segment);
        }

        return new Capacity(segments, capacity, occupiedSegments, occupiedVoices);
    }

    internal sealed record Expansion(BmsPcmVoiceMixer Mixer, Capacity Peak, Capacity Retired);

    internal sealed record Capacity(int Segments, int VoiceCapacity, int OccupiedSegments, int OccupiedVoices);
}
