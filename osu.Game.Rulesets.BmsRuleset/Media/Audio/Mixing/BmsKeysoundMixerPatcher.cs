using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using HarmonyLib;
using ManagedBass;
using ManagedBass.Mix;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing;

internal readonly record struct BmsMixerBatchItem(Track Track, Track? TrackToStop);

internal readonly record struct BmsMixerBatchResult(bool TrackScheduled, bool TrackStopped);

internal static class BmsKeysoundMixerPatcher
{
    internal const string MIXER_IDENTIFIER = "bms-keysounds";
    internal const string PREVIEW_MIXER_IDENTIFIER = "bms-preview";
    internal const double NativeScheduleLeadMilliseconds = 200;

    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.KeysoundFloatMixer";
    private const float limiter_ceiling = 0.98f;
    private const float limiter_release_ms = 50;
    private const double mixer_attack_ramp_ms = 2;
    private const double mixer_tail_ramp_ms = 2;
    private const int default_mixer_sample_rate = 44100;
    private const int mixer_sample_frame_size = sizeof(float) * 2;

    // ManagedBass 2022 does not expose BASS_MIXER_CHAN_ABSOLUTE from bassmix.h.
    private const BassFlags mixer_chan_absolute = (BassFlags)0x1000;

    private static readonly object install_lock = new();
    private static readonly ConcurrentDictionary<int, LimiterState> limiter_states = new();
    private static readonly ConcurrentDictionary<AudioManager, Action<ValueChangedEvent<int?>>> global_mixer_bindings = new();
    private static readonly Dictionary<AudioManager, int> global_mixer_references = [];
    private static readonly ConcurrentDictionary<int, MixerTimelineState> mixer_timelines = new();
    private static readonly DSPProcedure limiter_dsp = processLimiterDsp;

    private static PropertyInfo? handleProperty;
    private static FieldInfo? activeStreamField;
    private static FieldInfo? hasCompletedField;
    private static FieldInfo? isPlayedField;
    private static FieldInfo? isRunningField;
    private static MethodInfo? enqueueActionMethod;

    internal static bool IsInstalled { get; private set; }

    // The reverse-stream workaround is tested independently because BASS_FX reverse streams
    // can introduce discontinuities when many short BMS samples overlap.
    internal static bool EnableReverseStreamWorkaround => true;

    // Float mixing and limiting are tested as one unit because the limiter processes float samples.
    internal static bool EnableFloatMixerAndLimiter { get; } =
        BmsAudioPlatform.SupportsNativeBass;

    // Chord voices must enter the mixer at the same sample position. Immediate per-track
    // commands can otherwise reach the audio thread at slightly different points.
    internal static bool EnableNativeScheduling => true;

    internal static (long CallbackCount, long LimitedFrameCount, float Peak) GetLimiterDiagnostics(AudioMixer? mixer)
    {
        if (mixer == null)
            return default;

        var handle = getHandle(mixer);
        return handle != 0 && limiter_states.TryGetValue(handle, out var state)
            ? (state.CallbackCount, state.LimitedFrameCount, state.Peak)
            : default;
    }

    /// <summary>
    /// Installs the output limiter on framework's Windows shared-mode mixer. That mixer is
    /// downstream of all game mixers, so limiting only the BMS bus cannot prevent clipping when
    /// BGM and keysounds overlap at the final output.
    /// </summary>
    internal static void BindGlobalMixer(AudioManager audioManager)
    {
        if (!IsInstalled || !EnableFloatMixerAndLimiter)
            return;

        lock (install_lock)
        {
            if (global_mixer_bindings.ContainsKey(audioManager))
            {
                global_mixer_references[audioManager]++;
                return;
            }

            var globalMixerField = AccessTools.Field(typeof(AudioManager), "GlobalMixerHandle");
            if (globalMixerField?.GetValue(audioManager) is not IBindable<int?> globalMixer)
                return;

        Action<ValueChangedEvent<int?>> handler = change =>
        {
            if (change.OldValue is int oldHandle)
                removeLimiter(oldHandle);

            if (change.NewValue is int newHandle)
                installLimiter(newHandle);
        };

            global_mixer_bindings[audioManager] = handler;
            global_mixer_references[audioManager] = 1;

            globalMixer.BindValueChanged(handler, true);
        }
    }

    internal static void UnbindGlobalMixer(AudioManager audioManager)
    {
        lock (install_lock)
        {
            if (!global_mixer_bindings.TryGetValue(audioManager, out var handler))
                return;

            if (--global_mixer_references[audioManager] > 0)
                return;

            global_mixer_references.Remove(audioManager);
            global_mixer_bindings.TryRemove(audioManager, out _);

            var globalMixerField = AccessTools.Field(typeof(AudioManager), "GlobalMixerHandle");
            if (globalMixerField?.GetValue(audioManager) is not IBindable<int?> globalMixer)
                return;

            globalMixer.ValueChanged -= handler;

            if (globalMixer.Value is int handle)
                removeLimiter(handle);
        }
    }

    internal static void InstallOnce()
    {
        if (!BmsAudioPlatform.SupportsNativeBass)
            return;

        lock (install_lock)
        {
            if (IsInstalled)
                return;

            var mixerType = typeof(AudioMixer).Assembly.GetType("osu.Framework.Audio.Mixing.Bass.BassAudioMixer");
            var createMixer = mixerType == null ? null : AccessTools.Method(mixerType, "createMixer");
            var dispose = mixerType == null ? null : AccessTools.Method(mixerType, "Dispose", [typeof(bool)]);
            var transpiler = AccessTools.Method(typeof(BmsKeysoundMixerPatcher), nameof(createMixerTranspiler));
            var createPrefix = AccessTools.Method(typeof(BmsKeysoundMixerPatcher), nameof(createMixerPrefix));
            var createPostfix = AccessTools.Method(typeof(BmsKeysoundMixerPatcher), nameof(createMixerPostfix));
            var disposePrefix = AccessTools.Method(typeof(BmsKeysoundMixerPatcher), nameof(disposePrefixMethod));
            handleProperty = mixerType == null ? null : AccessTools.Property(mixerType, "Handle");
            var trackBassType = typeof(Track).Assembly.GetType("osu.Framework.Audio.Track.TrackBass");
            activeStreamField = trackBassType == null ? null : AccessTools.Field(trackBassType, "activeStream");
            hasCompletedField = trackBassType == null ? null : AccessTools.Field(trackBassType, "hasCompleted");
            isPlayedField = trackBassType == null ? null : AccessTools.Field(trackBassType, "isPlayed");
            isRunningField = trackBassType == null ? null : AccessTools.Field(trackBassType, "isRunning");
            enqueueActionMethod = AccessTools.Method(typeof(AudioComponent), "EnqueueAction", [typeof(Action)]);

            var missingMembers = new (string name, MemberInfo? member)[]
            {
                ("BassAudioMixer.createMixer", createMixer),
                ("BassAudioMixer.Dispose", dispose),
                ("BassAudioMixer.Handle", handleProperty),
                ("TrackBass.activeStream", activeStreamField),
                ("TrackBass.hasCompleted", hasCompletedField),
                ("TrackBass.isPlayed", isPlayedField),
                ("TrackBass.isRunning", isRunningField),
                ("AudioComponent.EnqueueAction", enqueueActionMethod),
                ("BmsKeysoundMixerPatcher.createMixerTranspiler", transpiler),
                ("BmsKeysoundMixerPatcher.createMixerPrefix", createPrefix),
                ("BmsKeysoundMixerPatcher.createMixerPostfix", createPostfix),
                ("BmsKeysoundMixerPatcher.disposePrefixMethod", disposePrefix),
            }.Where(member => member.member == null).Select(member => member.name).ToArray();

            if (missingMembers.Length > 0)
            {
                BmsLogger.Log("BMS keysound float mixer: Cannot install Harmony patch. Missing: " + string.Join(", ", missingMembers), LogLevel.Error);
                return;
            }

            try
            {
                var harmony = new Harmony(harmony_id);
                harmony.Patch(createMixer,
                    prefix: new HarmonyMethod(createPrefix),
                    postfix: new HarmonyMethod(createPostfix),
                    transpiler: new HarmonyMethod(transpiler));
                harmony.Patch(dispose, prefix: new HarmonyMethod(disposePrefix));
                IsInstalled = true;
            }
            catch (Exception exception)
            {
                BmsLogger.Error(exception, "BMS keysound float mixer: Failed to install Harmony patch. Falling back to the framework mixer.");
            }
        }
    }

    private static IEnumerable<CodeInstruction> createMixerTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var createMixerStream = AccessTools.Method(typeof(BassMix), nameof(BassMix.CreateMixerStream), [typeof(int), typeof(int), typeof(BassFlags)]);
        var adjustFlagsMethod = AccessTools.Method(typeof(BmsKeysoundMixerPatcher), nameof(adjustFlags));
        var patchedCalls = 0;

        foreach (var instruction in instructions)
        {
            if (instruction.Calls(createMixerStream))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, adjustFlagsMethod);

                patchedCalls++;
            }

            yield return instruction;
        }

        if (patchedCalls != 2)
            throw new InvalidOperationException($"Expected two BASS mixer creation calls, found {patchedCalls}.");
    }

    private static BassFlags adjustFlags(BassFlags flags, AudioMixer mixer) =>
        isBmsMixer(mixer) && EnableFloatMixerAndLimiter ? flags | BassFlags.Float : flags;

    // ReSharper disable InconsistentNaming
    private static void createMixerPrefix(object __instance) => removeLimiter(__instance);

    private static void createMixerPostfix(object __instance)
    {
        if (__instance is not AudioMixer mixer || !isBmsMixer(mixer))
            return;

        var handle = getHandle(__instance);

        if (handle == 0)
            return;

        if (!EnableFloatMixerAndLimiter)
            return;

        installLimiter(handle);
    }

    private static void disposePrefixMethod(object __instance) => removeLimiter(__instance);

    private static void removeLimiter(object mixer)
    {
        var handle = getHandle(mixer);

        removeLimiter(handle);
    }

    private static void installLimiter(int handle)
    {
        if (handle == 0 || limiter_states.ContainsKey(handle))
            return;

        var state = new LimiterState();
        var dspHandle = Bass.ChannelSetDSP(handle, limiter_dsp, IntPtr.Zero);

        if (dspHandle == 0)
        {
            BmsLogger.Log($"BMS keysound float mixer: Failed to install limiter DSP ({Bass.LastError}).", LogLevel.Error);
            return;
        }

        state.DspHandle = dspHandle;
        limiter_states[handle] = state;
    }

    private static void removeLimiter(int handle)
    {

        if (handle == 0)
            return;

        mixer_timelines.TryRemove(handle, out _);

        if (!limiter_states.TryRemove(handle, out var state))
            return;

        Bass.ChannelRemoveDSP(handle, state.DspHandle);
    }
    // ReSharper restore InconsistentNaming

    private static int getHandle(object mixer) => handleProperty?.GetValue(mixer) as int? ?? 0;

    private static bool isBmsMixer(AudioMixer mixer) =>
        mixer.Identifier is MIXER_IDENTIFIER or PREVIEW_MIXER_IDENTIFIER;

    internal static bool TryApplyTailRamp(AudioMixer? mixer, Track track, double offset)
    {
        if (!IsInstalled || mixer == null || activeStreamField == null)
            return false;

        var mixerHandle = getHandle(mixer);
        var trackHandle = activeStreamField.GetValue(track) as int? ?? 0;

        return mixerHandle != 0
               && trackHandle != 0
               && applyTailRamp(mixerHandle, trackHandle, track.Length - offset, track.AggregateTempo.Value);
    }

    internal static async Task<(bool TrackScheduled, bool TrackStopped)> TryScheduleTrackAsync(
        AudioMixer? mixer,
        Track track,
        Track? trackToStop,
        double delayMilliseconds,
        double targetTime)
    {
        if (!IsInstalled || mixer == null || enqueueActionMethod == null)
            return (false, false);

        var trackScheduled = false;
        var trackStopped = trackToStop == null;
        var task = enqueueActionMethod.Invoke(mixer,
        [
            new Action(() =>
            {
                trackScheduled = tryScheduleTrack(mixer, track, delayMilliseconds, targetTime);

                if (trackScheduled && trackToStop != null)
                    trackStopped = tryScheduleStop(mixer, trackToStop, delayMilliseconds, targetTime);
            }),
        ]) as Task;

        if (task == null)
            return (false, false);

        await task.ConfigureAwait(false);
        return (trackScheduled, trackStopped);
    }

    internal static async Task<BmsMixerBatchResult[]> TryScheduleBatchAsync(
        AudioMixer? mixer,
        IReadOnlyList<BmsMixerBatchItem> items,
        double targetTime)
    {
        var results = new BmsMixerBatchResult[items.Count];

        if (!IsInstalled || mixer == null || enqueueActionMethod == null || items.Count == 0)
            return results;

        var task = enqueueActionMethod.Invoke(mixer,
        [
            new Action(() => scheduleBatch(mixer, items, targetTime, results)),
        ]) as Task;

        if (task == null)
            return results;

        await task.ConfigureAwait(false);
        return results;
    }

    private static void scheduleBatch(
        AudioMixer mixer,
        IReadOnlyList<BmsMixerBatchItem> items,
        double targetTime,
        BmsMixerBatchResult[] results)
    {
        var mixerHandle = getHandle(mixer);

        if (mixerHandle == 0 || !Bass.ChannelLock(mixerHandle))
            return;

        try
        {
            var generatedPosition = getGeneratedPosition(mixerHandle);
            var absoluteStart = getLiveBatchStart(mixerHandle, generatedPosition, targetTime);

            if (absoluteStart < 0)
                return;

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var trackScheduled = tryScheduleTrackAt(mixerHandle, item.Track, absoluteStart);
                var trackStopped = item.TrackToStop == null;

                if (trackScheduled && item.TrackToStop != null)
                    trackStopped = tryStopTrackForBatch(mixerHandle, item.TrackToStop, generatedPosition, absoluteStart);

                results[i] = new BmsMixerBatchResult(trackScheduled, trackStopped);
            }
        }
        finally
        {
            Bass.ChannelLock(mixerHandle, false);
        }
    }

    internal static async Task<bool> TryRestartTrackAsync(AudioMixer? mixer, Track track, double offset)
    {
        if (!IsInstalled || mixer == null || enqueueActionMethod == null)
            return false;

        var restarted = false;
        var task = enqueueActionMethod.Invoke(mixer,
        [
            new Action(() => restarted = tryRestartTrack(mixer, track, offset)),
        ]) as Task;

        if (task == null)
            return false;

        await task.ConfigureAwait(false);
        return restarted;
    }

    private static bool tryRestartTrack(AudioMixer mixer, Track track, double offset)
    {
        if (activeStreamField == null || hasCompletedField == null)
            return false;

        var mixerHandle = getHandle(mixer);
        var trackHandle = activeStreamField.GetValue(track) as int? ?? 0;

        if (mixerHandle == 0 || trackHandle == 0)
            return false;

        BassMix.MixerRemoveChannel(trackHandle);

        var position = Bass.ChannelSeconds2Bytes(trackHandle, Math.Max(0, offset) / 1000);

        if (position < 0 || !Bass.ChannelSetPosition(trackHandle, position))
            return false;

        hasCompletedField.SetValue(track, false);

        if (!BassMix.MixerAddChannel(mixerHandle, trackHandle, getMixerChannelFlags()))
            return false;

        applyTailRamp(mixerHandle, trackHandle, track.Length - offset, track.AggregateTempo.Value);

        isPlayedField?.SetValue(track, true);
        isRunningField?.SetValue(track, true);
        return true;
    }

    private static bool tryScheduleTrack(AudioMixer mixer, Track track, double delayMilliseconds, double targetTime)
    {
        if (delayMilliseconds <= 0 || activeStreamField == null || hasCompletedField == null)
            return false;

        var mixerHandle = getHandle(mixer);
        var trackHandle = activeStreamField.GetValue(track) as int? ?? 0;

        if (mixerHandle == 0 || trackHandle == 0)
            return false;

        var absoluteStart = getAbsoluteStart(mixerHandle, delayMilliseconds, targetTime);

        if (absoluteStart < 0)
            return false;

        return tryScheduleTrackAt(mixerHandle, track, absoluteStart);
    }

    private static bool tryScheduleTrackAt(int mixerHandle, Track track, long absoluteStart)
    {
        if (activeStreamField == null || hasCompletedField == null)
            return false;

        var trackHandle = activeStreamField.GetValue(track) as int? ?? 0;

        if (mixerHandle == 0 || trackHandle == 0 || absoluteStart < 0)
            return false;

        // A completed TrackBass may already be detached from the mixer. It is still a valid
        // reusable voice; only remove it when BASS reports that it is currently attached.
        BassMix.MixerRemoveChannel(trackHandle);

        // TrackBass.SeekAsync() flushes the shared mixer when the framework considers a channel
        // stopped. Reposition the detached source directly so reusing one voice cannot discard
        // buffered output from every other keysound.
        if (!Bass.ChannelSetPosition(trackHandle, 0))
        {
            BassMix.MixerAddChannel(mixerHandle, trackHandle, getMixerChannelFlags(true));
            return false;
        }

        hasCompletedField.SetValue(track, false);

        var flags = getMixerChannelFlags() | mixer_chan_absolute;

        if (BassMix.MixerAddChannel(mixerHandle, trackHandle, flags, absoluteStart, 0))
        {
            applyTailRamp(mixerHandle, trackHandle, track.Length, track.AggregateTempo.Value);

            // The native insertion starts the channel without going through TrackBass.StartAsync.
            // Keep TrackBass's managed state coherent without re-adding the channel normally,
            // which would discard the absolute mixer position.
            isPlayedField?.SetValue(track, true);
            isRunningField?.SetValue(track, true);
            return true;
        }

        BassMix.MixerAddChannel(mixerHandle, trackHandle, getMixerChannelFlags(true));
        return false;
    }

    private static bool tryStopTrackForBatch(int mixerHandle, Track track, long generatedPosition, long absoluteStart)
    {
        if (activeStreamField == null)
            return false;

        var trackHandle = activeStreamField.GetValue(track) as int? ?? 0;

        if (trackHandle == 0)
            return false;

        if (absoluteStart > generatedPosition)
            return tryScheduleStopAt(mixerHandle, trackHandle, generatedPosition, absoluteStart);

        if (!BassMix.MixerRemoveChannel(trackHandle))
            return false;

        isPlayedField?.SetValue(track, false);
        isRunningField?.SetValue(track, false);
        return true;
    }

    private static BassFlags getMixerChannelFlags(bool paused = false)
    {
        var flags = BassFlags.MixerChanBuffer;

        if (paused)
            flags |= BassFlags.MixerChanPause;

        return flags;
    }

    private static bool applyTailRamp(int mixerHandle, int trackHandle, double remainingLengthMilliseconds, double tempo)
    {
        if (remainingLengthMilliseconds <= 0)
            return false;

        var playbackLength = remainingLengthMilliseconds / Math.Max(tempo, 0.01);
        var lengthBytes = Bass.ChannelSeconds2Bytes(mixerHandle, playbackLength / 1000);
        var requestedAttackBytes = Bass.ChannelSeconds2Bytes(mixerHandle, mixer_attack_ramp_ms / 1000);
        var requestedRampBytes = Bass.ChannelSeconds2Bytes(mixerHandle, mixer_tail_ramp_ms / 1000);

        var nodes = CreateStartAndTailRampNodes(lengthBytes, requestedAttackBytes, requestedRampBytes);

        if (nodes.Length == 0)
            return false;

        return BassMix.ChannelSetEnvelope(trackHandle, MixEnvelope.Volume, nodes, nodes.Length)
               && BassMix.ChannelSetEnvelopePosition(trackHandle, MixEnvelope.Volume, 0);
    }

    internal static MixerNode[] CreateTailRampNodes(long lengthBytes, long requestedRampBytes)
    {
        if (lengthBytes <= 0 || requestedRampBytes <= 0)
            return [];

        var rampBytes = Math.Min(requestedRampBytes, lengthBytes / 2);
        return
        [
            new() { Position = 0, Value = 1 },
            new() { Position = lengthBytes - rampBytes, Value = 1 },
            new() { Position = lengthBytes, Value = 0 },
        ];
    }

    internal static MixerNode[] CreateStartAndTailRampNodes(long lengthBytes, long requestedAttackBytes, long requestedRampBytes)
    {
        if (lengthBytes <= 0 || requestedAttackBytes <= 0 || requestedRampBytes <= 0)
            return [];

        var attackBytes = Math.Min(requestedAttackBytes, lengthBytes / 3);
        var tailBytes = Math.Min(requestedRampBytes, (lengthBytes - attackBytes) / 2);

        if (attackBytes <= 0 || tailBytes <= 0)
            return [];

        return
        [
            new() { Position = 0, Value = 0 },
            new() { Position = attackBytes, Value = 1 },
            new() { Position = lengthBytes - tailBytes, Value = 1 },
            new() { Position = lengthBytes, Value = 0 },
        ];
    }

    internal static void ResetTimeline(AudioMixer? mixer)
    {
        if (mixer == null)
            return;

        var mixerHandle = getHandle(mixer);

        if (mixerHandle != 0)
            mixer_timelines.TryRemove(mixerHandle, out _);
    }

    private static bool tryScheduleStop(AudioMixer mixer, Track track, double delayMilliseconds, double targetTime)
    {
        if (delayMilliseconds <= 0 || activeStreamField == null)
            return false;

        var mixerHandle = getHandle(mixer);
        var trackHandle = activeStreamField.GetValue(track) as int? ?? 0;

        if (mixerHandle == 0 || trackHandle == 0)
            return false;

        var absoluteStart = getAbsoluteStart(mixerHandle, delayMilliseconds, targetTime);
        var generatedPosition = getGeneratedPosition(mixerHandle);

        if (absoluteStart < 0 || generatedPosition < 0)
            return false;

        return tryScheduleStopAt(mixerHandle, trackHandle, generatedPosition, absoluteStart);
    }

    private static bool tryScheduleStopAt(int mixerHandle, int trackHandle, long generatedPosition, long absoluteStart)
    {
        var lengthBytes = absoluteStart - generatedPosition;

        if (lengthBytes <= 0 || !BassMix.MixerRemoveChannel(trackHandle))
            return false;

        const BassFlags flags = BassFlags.MixerChanBuffer | BassFlags.MixerChanNoRampin | mixer_chan_absolute;

        if (BassMix.MixerAddChannel(mixerHandle, trackHandle, flags, generatedPosition, lengthBytes))
        {
            // Scheduled same-key retriggers cut the old voice at an arbitrary sample. A short
            // tail envelope reaches zero exactly at the cut, preventing a discontinuity without
            // moving the retrigger's scheduled start.
            var rampBytes = Bass.ChannelSeconds2Bytes(mixerHandle, mixer_tail_ramp_ms / 1000);
            var nodes = CreateTailRampNodes(lengthBytes, rampBytes);
            if (nodes.Length > 0)
            {
                BassMix.ChannelSetEnvelope(trackHandle, MixEnvelope.Volume, nodes, nodes.Length);
                BassMix.ChannelSetEnvelopePosition(trackHandle, MixEnvelope.Volume, 0);
            }

            return true;
        }

        BassMix.MixerAddChannel(mixerHandle, trackHandle, BassFlags.MixerChanBuffer | BassFlags.MixerChanNoRampin);
        return false;
    }

    private static long getLiveBatchStart(int mixerHandle, long generatedPosition, double targetTime)
    {
        if (generatedPosition < 0)
            return -1;

        var timeline = mixer_timelines.GetOrAdd(mixerHandle, _ => new MixerTimelineState());

        lock (timeline)
        {
            if (!timeline.IsInitialised)
            {
                timeline.IsInitialised = true;
                timeline.OriginTime = targetTime;
                timeline.OriginPosition = generatedPosition;
                timeline.SampleRate = getMixerSampleRate(mixerHandle);
            }

            var mappedPosition = timeline.OriginPosition + GetSampleOffsetBytes(timeline.OriginTime, targetTime, timeline.SampleRate);
            return SelectLiveBatchStart(generatedPosition, mappedPosition);
        }
    }

    internal static long SelectLiveBatchStart(long generatedPosition, long mappedPosition) =>
        Math.Max(generatedPosition, mappedPosition);

    private static long getAbsoluteStart(int mixerHandle, double delayMilliseconds, double targetTime)
    {
        var generatedPosition = getGeneratedPosition(mixerHandle);
        var delayBytes = Bass.ChannelSeconds2Bytes(mixerHandle, delayMilliseconds / 1000);

        if (generatedPosition < 0 || delayBytes < 0)
            return -1;

        var timeline = mixer_timelines.GetOrAdd(mixerHandle, _ => new MixerTimelineState());

        lock (timeline)
        {
            if (!timeline.IsInitialised)
            {
                timeline.IsInitialised = true;
                timeline.OriginTime = targetTime;
                timeline.OriginPosition = generatedPosition + delayBytes;
                timeline.SampleRate = getMixerSampleRate(mixerHandle);
            }

            return timeline.OriginPosition + GetSampleOffsetBytes(timeline.OriginTime, targetTime, timeline.SampleRate);
        }
    }

    internal static long GetSampleOffsetBytes(double originTime, double targetTime)
        => GetSampleOffsetBytes(originTime, targetTime, default_mixer_sample_rate);

    internal static long GetSampleOffsetBytes(double originTime, double targetTime, int sampleRate)
        => getSampleOffsetBytesCore(originTime, targetTime, sampleRate);

    private static long getSampleOffsetBytesCore(double originTime, double targetTime, int sampleRate)
    {
        var exactSampleOffset = (targetTime - originTime) / 1000 * sampleRate;
        return (long)Math.Round(exactSampleOffset, MidpointRounding.AwayFromZero) * mixer_sample_frame_size;
    }

    private static int getMixerSampleRate(int mixerHandle) =>
        Bass.ChannelGetAttribute(mixerHandle, ChannelAttribute.Frequency, out var frequency) && frequency > 0
            ? (int)Math.Round(frequency)
            : default_mixer_sample_rate;

    private static long getGeneratedPosition(int mixerHandle)
    {
        var mixerPosition = Bass.ChannelGetPosition(mixerHandle);
        var bufferedBytes = Bass.ChannelGetData(mixerHandle, IntPtr.Zero, (int)DataFlags.Available);
        return mixerPosition < 0 || bufferedBytes < 0 ? -1 : mixerPosition + bufferedBytes;
    }

    private static unsafe void processLimiterDsp(int handle, int channel, IntPtr buffer, int length, IntPtr user)
    {
        if (!limiter_states.TryGetValue(channel, out var state))
            return;

        var samples = new Span<float>((void*)buffer, length / sizeof(float));
        state.CallbackCount++;
        for (var i = 0; i + 1 < samples.Length; i += 2)
        {
            var peak = Math.Max(Math.Abs(samples[i]), Math.Abs(samples[i + 1]));
            state.Peak = Math.Max(state.Peak, peak);
            if (peak > limiter_ceiling)
                state.LimitedFrameCount++;
        }

        ProcessSamples(samples, ref state.Gain);
    }

    internal static void ProcessSamples(Span<float> samples, ref float gain)
    {
        var peak = 0f;
        for (var i = 0; i + 1 < samples.Length; i += 2)
            peak = Math.Max(peak, Math.Max(Math.Abs(samples[i]), Math.Abs(samples[i + 1])));

        var requiredGain = peak > limiter_ceiling ? limiter_ceiling / peak : 1;
        // Attack must be applied before the first frame so no sample can exceed the ceiling;
        // only recovery is interpolated to keep the gain continuous between callback blocks.
        var startGain = Math.Min(gain, requiredGain);
        var targetGain = requiredGain < gain
            ? requiredGain
            : Math.Min(1, gain + samples.Length / 2f / (default_mixer_sample_rate * limiter_release_ms / 1000f));
        gain = targetGain;

        if (startGain >= 0.999999f && targetGain >= 0.999999f)
            return;

        var frameCount = samples.Length / 2;
        for (var frame = 0; frame < frameCount; frame++)
        {
            var frameGain = startGain + (targetGain - startGain) * (frame + 1) / frameCount;
            var i = frame * 2;
            samples[i] *= frameGain;
            samples[i + 1] *= frameGain;
        }
    }

    private sealed class LimiterState
    {
        public int DspHandle;
        public float Gain = 1;
        public long CallbackCount;
        public long LimitedFrameCount;
        public float Peak;
    }

    private sealed class MixerTimelineState
    {
        public bool IsInitialised;
        public double OriginTime;
        public long OriginPosition;
        public int SampleRate = default_mixer_sample_rate;
    }
}
