using System;
using System.Threading;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Processing;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing;

internal readonly record struct BmsAudioDiagnostics(
    long RenderedFrames,
    int ActiveVoices,
    int DrainingVoices,
    int PeakVoices,
    int QueuedCommands,
    int QueueHighWater,
    long QueueOverflows,
    long VoicePoolOverflows,
    long SubmittedVoices,
    long FoldedVoices,
    long StartedVoices,
    long PlaybackUnderflows,
    float InputPeak,
    float OutputPeak,
    float LimiterGain,
    long LimitedFrames);

internal sealed class BmsPcmVoiceMixer
{
    internal const int DEFAULT_VOICE_CAPACITY = 512;
    internal const int DEFAULT_COMMAND_CAPACITY = 4096;

    private const float limiter_ceiling = 0.98f;
    private const float limiter_release_ms = 50;
    private const float attack_fade_ms = 2;
    private const float retrigger_fade_ms = 2;
    private const float stop_all_fade_ms = 5;
    private const float tail_fade_ms = 2;

    private readonly BmsPcmVoice[] voices;
    private readonly BmsVoiceCommandQueue commands;
    private readonly int attackFadeFrames;
    private readonly int retriggerFadeFrames;
    private readonly int stopAllFadeFrames;
    private readonly int tailFadeFrames;
    private readonly float limiterRecoveryPerFrame;

    private long renderedFrames;
    private int currentEpoch;
    private bool paused;
    private float masterGain = 1;
    private float limiterGain = 1;
    private int peakVoices;
    private int queueHighWater;
    private long queueOverflows;
    private long voicePoolOverflows;
    private long submittedVoices;
    private long foldedVoices;
    private long startedVoices;
    private long playbackUnderflows;
    private float inputPeak;
    private float outputPeak;
    private long limitedFrames;

    internal long RenderedFrames => renderedFrames;

    internal BmsPcmVoiceMixer(
        int voiceCapacity = DEFAULT_VOICE_CAPACITY,
        int commandCapacity = DEFAULT_COMMAND_CAPACITY,
        int sampleRate = BmsFixedRatePcmProcessor.OUTPUT_SAMPLE_RATE)
    {
        if (voiceCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(voiceCapacity));

        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        voices = new BmsPcmVoice[voiceCapacity];
        commands = new BmsVoiceCommandQueue(commandCapacity);
        attackFadeFrames = millisecondsToFrames(attack_fade_ms, sampleRate);
        retriggerFadeFrames = millisecondsToFrames(retrigger_fade_ms, sampleRate);
        stopAllFadeFrames = millisecondsToFrames(stop_all_fade_ms, sampleRate);
        tailFadeFrames = millisecondsToFrames(tail_fade_ms, sampleRate);
        limiterRecoveryPerFrame = 1 / (sampleRate * limiter_release_ms / 1000);
    }

    internal bool SubmitPlayBatch(ReadOnlySpan<BmsVoicePlay> plays)
    {
        if (plays.IsEmpty)
            return true;

        Interlocked.Add(ref submittedVoices, plays.Length);
        var batch = new BmsVoiceCommand[plays.Length];
        var count = 0;

        for (var i = 0; i < plays.Length; i++)
        {
            var play = sanitise(plays[i]);
            var superseded = false;

            for (var later = i + 1; later < plays.Length; later++)
            {
                if (plays[later].TargetFrame == play.TargetFrame && plays[later].Domain == play.Domain)
                {
                    superseded = true;
                    break;
                }
            }

            if (!superseded)
                batch[count++] = new BmsVoiceCommand(BmsVoiceCommandType.Play, play.TargetFrame, play.Epoch, play);
        }

        Interlocked.Add(ref foldedVoices, plays.Length - count);

        if (!commands.TryEnqueue(batch.AsSpan(0, count)))
        {
            queueOverflows++;
            return false;
        }

        queueHighWater = Math.Max(queueHighWater, commands.Count);
        return true;
    }

    internal bool SubmitControl(BmsVoiceCommandType type, long targetFrame, int epoch, float value = 0)
    {
        if (type == BmsVoiceCommandType.Play)
            throw new ArgumentOutOfRangeException(nameof(type));

        if (!commands.TryEnqueue(new BmsVoiceCommand(type, Math.Max(targetFrame, renderedFrames), epoch, Value: value)))
        {
            queueOverflows++;
            return false;
        }

        queueHighWater = Math.Max(queueHighWater, commands.Count);
        return true;
    }

    internal bool SubmitVoiceControl(BmsVoiceCommandType type, long voiceId, long targetFrame, int epoch, float value = 0)
    {
        if (type is not (BmsVoiceCommandType.StopVoice or BmsVoiceCommandType.SetVoiceGain))
            throw new ArgumentOutOfRangeException(nameof(type));

        if (!commands.TryEnqueue(new BmsVoiceCommand(type, Math.Max(targetFrame, renderedFrames), epoch, Value: value, VoiceId: voiceId)))
        {
            queueOverflows++;
            return false;
        }

        queueHighWater = Math.Max(queueHighWater, commands.Count);
        return true;
    }

    internal void Render(Span<float> output)
    {
        if (output.Length % 2 != 0)
            throw new ArgumentException("PCM output must contain complete stereo frames.", nameof(output));

        output.Clear();
        var frameCount = output.Length / 2;

        for (var frame = 0; frame < frameCount; frame++)
        {
            var absoluteFrame = renderedFrames + frame;
            consumeCommands(absoluteFrame);

            var left = 0f;
            var right = 0f;

            if (!paused)
            {
                for (var voiceIndex = 0; voiceIndex < voices.Length; voiceIndex++)
                {
                    ref var voice = ref voices[voiceIndex];
                    if (voice.State == BmsPcmVoiceState.Free)
                        continue;

                    var asset = voice.Asset!;
                    if (voice.SourceFrame >= voice.EndFrame || !asset.TryReadStereoFrame(voice.SourceFrame, out var voiceLeft, out var voiceRight))
                    {
                        if (!asset.IsComplete || voice.SourceFrame < asset.TotalFrameCount)
                            playbackUnderflows++;

                        voice = default;
                        continue;
                    }

                    var gain = voice.BaseGain * getEnvelopeGain(ref voice, absoluteFrame);
                    left += voiceLeft * gain;
                    right += voiceRight * gain;
                    voice.SourceFrame++;

                    if (voice.SourceFrame >= voice.EndFrame || gain <= 0)
                        voice = default;
                }
            }

            left *= masterGain;
            right *= masterGain;
            inputPeak = Math.Max(inputPeak, Math.Max(Math.Abs(left), Math.Abs(right)));
            applyLimiter(ref left, ref right);
            outputPeak = Math.Max(outputPeak, Math.Max(Math.Abs(left), Math.Abs(right)));

            var outputIndex = frame * 2;
            output[outputIndex] = left;
            output[outputIndex + 1] = right;
        }

        renderedFrames += frameCount;
        updatePeakVoiceCount();
    }

    internal float[] RenderFrames(int frameCount, int blockFrames = 1024)
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
            Render(output.AsSpan(rendered * 2, count * 2));
            rendered += count;
        }

        return output;
    }

    internal BmsAudioDiagnostics GetDiagnostics()
    {
        var active = 0;
        var draining = 0;

        foreach (var voice in voices)
        {
            if (voice.State == BmsPcmVoiceState.Active)
                active++;
            else if (voice.State == BmsPcmVoiceState.Draining)
                draining++;
        }

        return new BmsAudioDiagnostics(
            renderedFrames,
            active,
            draining,
            peakVoices,
            commands.Count,
            queueHighWater,
            queueOverflows,
            voicePoolOverflows,
            Interlocked.Read(ref submittedVoices),
            Interlocked.Read(ref foldedVoices),
            startedVoices,
            playbackUnderflows,
            inputPeak,
            outputPeak,
            limiterGain,
            limitedFrames);
    }

    private void consumeCommands(long frame)
    {
        while (commands.TryPeek(out var command) && command.TargetFrame <= frame)
        {
            commands.TryDequeue(out command);

            switch (command.Type)
            {
                case BmsVoiceCommandType.Play:
                    if (command.Epoch == currentEpoch && !paused)
                        startVoice(command.Play, frame);
                    break;

                case BmsVoiceCommandType.Pause:
                    if (command.Epoch == currentEpoch)
                        paused = true;
                    break;

                case BmsVoiceCommandType.Resume:
                    if (command.Epoch == currentEpoch)
                        paused = false;
                    break;

                case BmsVoiceCommandType.StopAll:
                    if (command.Epoch == currentEpoch)
                        drainAll(frame, stopAllFadeFrames);
                    break;

                case BmsVoiceCommandType.SetMasterGain:
                    if (command.Epoch == currentEpoch)
                        masterGain = sanitiseGain(command.Value);
                    break;

                case BmsVoiceCommandType.ReplaceEpoch:
                    currentEpoch = command.Epoch;
                    drainAll(frame, stopAllFadeFrames);
                    break;

                case BmsVoiceCommandType.StopVoice:
                    if (command.Epoch == currentEpoch)
                        stopVoice(command.VoiceId, frame);
                    break;

                case BmsVoiceCommandType.SetVoiceGain:
                    if (command.Epoch == currentEpoch)
                        setVoiceGain(command.VoiceId, sanitiseGain(command.Value));

                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    private void startVoice(BmsVoicePlay play, long frame)
    {
        for (var i = 0; i < voices.Length; i++)
        {
            ref var existing = ref voices[i];
            if (existing.State == BmsPcmVoiceState.Free || existing.Domain != play.Domain)
                continue;

            beginFade(ref existing, frame, retriggerFadeFrames);
        }

        var freeIndex = -1;
        for (var i = 0; i < voices.Length; i++)
        {
            if (voices[i].State != BmsPcmVoiceState.Free)
                continue;

            freeIndex = i;
            break;
        }

        if (freeIndex < 0)
        {
            voicePoolOverflows++;
            return;
        }

        var sliceStart = Math.Max(0, play.Domain.SliceStartFrame);
        var sourceStart = Math.Max(sliceStart, play.SourceOffsetFrame);
        var assetEnd = play.Asset.TotalFrameCount >= 0 ? play.Asset.TotalFrameCount : long.MaxValue;
        var sliceEnd = play.Domain.SliceFrameCount < 0
            ? assetEnd
            : Math.Min(assetEnd, sliceStart + play.Domain.SliceFrameCount);

        if (sourceStart >= sliceEnd)
            return;

        voices[freeIndex] = new BmsPcmVoice
        {
            State = BmsPcmVoiceState.Active,
            Asset = play.Asset,
            Domain = play.Domain,
            SourceFrame = sourceStart,
            EndFrame = sliceEnd,
            BaseGain = sanitiseGain(play.Gain),
            VoiceId = play.VoiceId,
            StartFrame = frame,
            FadeStartGain = 1,
        };
        startedVoices++;

        updatePeakVoiceCount();
    }

    private float getEnvelopeGain(ref BmsPcmVoice voice, long frame)
    {
        var tailFramesRemaining = voice.EndFrame - voice.SourceFrame;
        var tailGain = tailFramesRemaining >= tailFadeFrames
            ? 1
            : Math.Max(0, tailFramesRemaining / (float)tailFadeFrames);
        var attackGain = Math.Clamp((frame - voice.StartFrame + 1) / (float)attackFadeFrames, 0, 1);
        var boundaryGain = Math.Min(tailGain, attackGain);

        if (voice.State != BmsPcmVoiceState.Draining)
            return boundaryGain;

        if (frame >= voice.FadeEndFrame)
            return 0;

        var fadeProgress = (frame - voice.FadeStartFrame) / (float)Math.Max(1, voice.FadeEndFrame - voice.FadeStartFrame);
        return Math.Min(boundaryGain, voice.FadeStartGain * (1 - Math.Clamp(fadeProgress, 0, 1)));
    }

    private void beginFade(ref BmsPcmVoice voice, long frame, int durationFrames)
    {
        var currentGain = getEnvelopeGain(ref voice, frame);
        voice.State = BmsPcmVoiceState.Draining;
        voice.FadeStartFrame = frame;
        voice.FadeEndFrame = frame + durationFrames;
        voice.FadeStartGain = currentGain;
    }

    private void drainAll(long frame, int durationFrames)
    {
        for (var i = 0; i < voices.Length; i++)
        {
            if (voices[i].State != BmsPcmVoiceState.Free)
                beginFade(ref voices[i], frame, durationFrames);
        }
    }

    private void stopVoice(long voiceId, long frame)
    {
        for (var i = 0; i < voices.Length; i++)
        {
            if (voices[i].State != BmsPcmVoiceState.Free && voices[i].VoiceId == voiceId)
                beginFade(ref voices[i], frame, retriggerFadeFrames);
        }
    }

    private void setVoiceGain(long voiceId, float gain)
    {
        for (var i = 0; i < voices.Length; i++)
        {
            if (voices[i].State != BmsPcmVoiceState.Free && voices[i].VoiceId == voiceId)
                voices[i].BaseGain = gain;
        }
    }

    private void applyLimiter(ref float left, ref float right)
    {
        var peak = Math.Max(Math.Abs(left), Math.Abs(right));
        var requiredGain = peak > limiter_ceiling ? limiter_ceiling / peak : 1;

        if (requiredGain < 1)
            limiterGain = Math.Min(limiterGain, requiredGain);
        else
            limiterGain = Math.Min(1, limiterGain + limiterRecoveryPerFrame);

        if (limiterGain < 0.999999f)
        {
            left *= limiterGain;
            right *= limiterGain;
        }

        var outputFramePeak = Math.Max(Math.Abs(left), Math.Abs(right));
        if (outputFramePeak > limiter_ceiling)
        {
            var correction = limiter_ceiling / outputFramePeak;
            left = Math.Clamp(left * correction, -limiter_ceiling, limiter_ceiling);
            right = Math.Clamp(right * correction, -limiter_ceiling, limiter_ceiling);
        }

        if (requiredGain < 1)
            limitedFrames++;
    }

    private void updatePeakVoiceCount()
    {
        var count = 0;
        for (var i = 0; i < voices.Length; i++)
        {
            if (voices[i].State != BmsPcmVoiceState.Free)
                count++;
        }

        peakVoices = Math.Max(peakVoices, count);
    }

    private static BmsVoicePlay sanitise(BmsVoicePlay play)
    {
        ArgumentNullException.ThrowIfNull(play.Asset);
        return play with
        {
            TargetFrame = Math.Max(0, play.TargetFrame),
            SourceOffsetFrame = Math.Max(0, play.SourceOffsetFrame),
            Gain = sanitiseGain(play.Gain),
        };
    }

    private static float sanitiseGain(float gain) => float.IsFinite(gain) ? Math.Max(0, gain) : 0;

    private static int millisecondsToFrames(float milliseconds, int sampleRate) => Math.Max(1, (int)Math.Round(milliseconds * sampleRate / 1000));

    private enum BmsPcmVoiceState : byte
    {
        Free,
        Active,
        Draining,
    }

    private struct BmsPcmVoice
    {
        public BmsPcmVoiceState State;
        public BmsPcmAsset? Asset;
        public BmsTerminationDomain Domain;
        public long SourceFrame;
        public long EndFrame;
        public float BaseGain;
        public long VoiceId;
        public long StartFrame;
        public long FadeStartFrame;
        public long FadeEndFrame;
        public float FadeStartGain;
    }

}
