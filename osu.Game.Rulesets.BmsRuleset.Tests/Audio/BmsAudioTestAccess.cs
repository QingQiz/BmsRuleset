#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using osu.Framework.Audio.Mixing;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

internal static class BmsAudioTestAccess
{
    private const BindingFlags instance_flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static AudioMixer? GetOutputMixer(BmsSamplePlayback playback)
    {
        var session = getSession(playback);
        return session == null ? null : getOptionalFieldValue<AudioMixer>(session, "outputMixer");
    }

    internal static int GetActiveVoiceCount(BmsSamplePlayback playback)
    {
        var session = getSession(playback);
        if (session == null)
            return 0;

        var mixer = getOptionalFieldValue<object>(session, "pcmMixer");
        return mixer == null ? 0 : getRequiredPropertyValue<int>(mixer, "ActiveVoiceCount");
    }

    internal static bool IsSessionDisposed(BmsSamplePlayback playback) => getSession(playback) == null;

    internal static int GetCachedAssetCount(BmsSamplePlayback playback)
    {
        var session = getSession(playback);
        if (session == null)
            return 0;

        var controller = getOptionalPropertyValue<object>(session, "Controller");
        if (controller == null)
            return 0;

        var cache = getOptionalFieldValue<object>(controller, "assetCache");
        if (cache == null)
            return 0;

        var entries = getRequiredFieldValue<object>(cache, "entries");
        return getRequiredPropertyValue<int>(entries, "Count");
    }

    internal static bool HasPreviewPlayback(BmsPreviewTrack track) => getPreviewPlayback(track) != null;

    internal static int GetPreviewActiveVoiceCount(BmsPreviewTrack track)
    {
        var playback = getPreviewPlayback(track);
        return playback == null ? 0 : getRequiredPropertyValue<int>(playback, "ActiveVoiceCount");
    }

    internal static double GetPreviewOutputGain(BmsPreviewTrack track)
    {
        var playback = getPreviewPlayback(track);
        if (playback == null)
            return 0;

        var masterGain = getRequiredFieldValue<object>(playback, "masterGain");
        return getRequiredPropertyValue<double>(masterGain, "Value");
    }

    internal static double GetRestoreFadeVolume(BmsPreviewTrack track) => getBindableDoubleValue(track, "restoreFadeVolume");

    internal static double GetSeekFadeVolume(BmsPreviewTrack track) => getBindableDoubleValue(track, "seekFadeVolume");

    internal static string GetFirstPreviewSamplePath(BmsPreviewTrack track)
    {
        var playback = getPreviewPlayback(track)
                       ?? throw new InvalidOperationException("Preview playback has not been created.");
        var events = getRequiredFieldValue<System.Collections.IEnumerable>(playback, "sortedEvents");
        var enumerator = events.GetEnumerator();

        try
        {
            if (!enumerator.MoveNext())
                throw new InvalidOperationException("Preview playback contains no sample events.");

            return getRequiredPropertyValue<string>(enumerator.Current!, "SamplePath");
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }

    internal static int GetPreviewPreparedSampleCount(BmsPreviewTrack track)
    {
        var controller = getPreviewController(track);
        if (controller == null)
            return 0;

        var leases = getRequiredFieldValue<object>(controller, "leases");
        return getRequiredPropertyValue<int>(leases, "Count");
    }

    internal static IDisposable HoldPreviewSampleNotReady(BmsPreviewTrack track, ushort sampleKey)
    {
        var controller = getPreviewController(track)
                         ?? throw new InvalidOperationException("Preview playback controller has not been created.");
        var leases = getRequiredFieldValue<Dictionary<ushort, BmsPcmAssetLease>>(controller, "leases");

        if (!leases.TryGetValue(sampleKey, out var originalLease))
            throw new InvalidOperationException($"Preview sample {sampleKey} has not been prepared.");

        var heldLease = new BmsPcmAssetLease(
            new BmsPcmAsset(originalLease.Asset.SampleRate, originalLease.Asset.Channels),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task,
            () => { });
        leases[sampleKey] = heldLease;
        return new DelegateDisposable(() =>
        {
            if (leases.TryGetValue(sampleKey, out var current) && ReferenceEquals(current, heldLease))
                leases[sampleKey] = originalLease;

            heldLease.Dispose();
        });
    }

    private static object? getSession(BmsSamplePlayback playback) => getOptionalFieldValue<object>(playback, "playbackSession");

    private static object? getPreviewPlayback(BmsPreviewTrack track) => getOptionalFieldValue<object>(track, "playback");

    private static object? getPreviewController(BmsPreviewTrack track)
    {
        var playback = getPreviewPlayback(track);
        if (playback == null)
            return null;

        var session = getOptionalFieldValue<object>(playback, "playbackSession");
        return session == null ? null : getOptionalPropertyValue<object>(session, "Controller");
    }

    private static double getBindableDoubleValue(BmsPreviewTrack track, string fieldName)
    {
        var bindable = getRequiredFieldValue<object>(track, fieldName);
        return getRequiredPropertyValue<double>(bindable, "Value");
    }

    private static T getRequiredFieldValue<T>(object instance, string name)
    {
        var field = findField(instance.GetType(), name)
                    ?? throw new InvalidOperationException($"Required field '{instance.GetType().FullName}.{name}' was not found.");
        return castRequiredValue<T>(field.GetValue(instance), $"field '{field.DeclaringType?.FullName}.{name}'");
    }

    private static T? getOptionalFieldValue<T>(object instance, string name)
        where T : class
    {
        var field = findField(instance.GetType(), name)
                    ?? throw new InvalidOperationException($"Required field '{instance.GetType().FullName}.{name}' was not found.");
        return castOptionalValue<T>(field.GetValue(instance), $"field '{field.DeclaringType?.FullName}.{name}'");
    }

    private static T getRequiredPropertyValue<T>(object instance, string name)
    {
        var property = instance.GetType().GetProperty(name, instance_flags)
                       ?? throw new InvalidOperationException($"Required property '{instance.GetType().FullName}.{name}' was not found.");
        return castRequiredValue<T>(property.GetValue(instance), $"property '{property.DeclaringType?.FullName}.{name}'");
    }

    private static T? getOptionalPropertyValue<T>(object instance, string name)
        where T : class
    {
        var property = instance.GetType().GetProperty(name, instance_flags)
                       ?? throw new InvalidOperationException($"Required property '{instance.GetType().FullName}.{name}' was not found.");
        return castOptionalValue<T>(property.GetValue(instance), $"property '{property.DeclaringType?.FullName}.{name}'");
    }

    private static FieldInfo? findField(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (current.GetField(name, instance_flags | BindingFlags.DeclaredOnly) is { } field)
                return field;
        }

        return null;
    }

    private static T castRequiredValue<T>(object? value, string member)
    {
        if (value is T typed)
            return typed;

        throw new InvalidOperationException($"Required {member} has value type '{value?.GetType().FullName ?? "null"}', expected '{typeof(T).FullName}'.");
    }

    private static T? castOptionalValue<T>(object? value, string member)
        where T : class
    {
        if (value == null)
            return null;

        return value as T
               ?? throw new InvalidOperationException($"Required {member} has value type '{value.GetType().FullName}', expected '{typeof(T).FullName}'.");
    }

    private sealed class DelegateDisposable(Action dispose) : IDisposable
    {
        private Action? disposeAction = dispose;

        public void Dispose() => System.Threading.Interlocked.Exchange(ref disposeAction, null)?.Invoke();
    }
}
