using System;
using System.Reflection;
using System.Reflection.Emit;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.IO.Stores;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.IO;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps;

/// <summary>
///     A <see cref="WorkingBeatmapCache" /> that wraps BMS beatmaps in a <see cref="BmsWorkingBeatmap" />
///     so that <see cref="BmsPreviewTrack" /> is used as the audio track instead of a silent virtual track.
///     Non-BMS beatmaps pass through to the base implementation unchanged.
/// </summary>
internal class BmsWorkingBeatmapCache : WorkingBeatmapCache
{
    private BmsWorkingBeatmapCache(
        ITrackStore trackStore,
        AudioManager audioManager,
        IResourceStore<byte[]> resources,
        IResourceStore<byte[]> files,
        WorkingBeatmap defaultBeatmap,
        GameHost host,
        RealmAccess realm)
        : base(trackStore, audioManager, resources, files, defaultBeatmap, host, realm)
    {
    }

    public override WorkingBeatmap GetWorkingBeatmap(BeatmapInfo? beatmapInfo)
    {
        bool isBms = beatmapInfo?.Ruleset?.ShortName == "bms";

        if (!isBms)
            return base.GetWorkingBeatmap(beatmapInfo);

        var working = base.GetWorkingBeatmap(beatmapInfo);
        var audioManager = ((IStorageResourceProvider)this).AudioManager;

        return new BmsWorkingBeatmap(working, audioManager);
    }

    #region Reflection-based creation

    /// <summary>
    ///     Wraps an existing <see cref="WorkingBeatmapCache" /> by replacing its internal state
    ///     in a new <see cref="BmsWorkingBeatmapCache" /> instance.
    /// </summary>
    /// <param name="original">The cache instance to wrap. Must be a <see cref="WorkingBeatmapCache" />.</param>
    /// <returns>A new <see cref="BmsWorkingBeatmapCache" /> with the same backing stores.</returns>
    /// <exception cref="InvalidOperationException">If a required private field is missing.</exception>
    internal static BmsWorkingBeatmapCache Wrap(WorkingBeatmapCache original)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;

        var trackStore = getField<ITrackStore>(original, "trackStore", flags);
        var audioManager = getField<AudioManager>(original, "audioManager", flags);
        var resources = getField<IResourceStore<byte[]>>(original, "resources", flags);
        var files = getField<IResourceStore<byte[]>>(original, "files", flags);
        var host = getField<GameHost>(original, "host", flags);
        var realm = getField<RealmAccess>(original, "realm", flags);

        var defaultBeatmap = original.DefaultBeatmap;

        return new BmsWorkingBeatmapCache(trackStore, audioManager, resources, files, defaultBeatmap, host, realm);
    }

    private static T getField<T>(object target, string fieldName, BindingFlags flags)
    {
        var field = target.GetType().GetField(fieldName, flags);

        if (field == null)
        {
            Logger.Log(
                $"BMS WorkingBeatmapCache: Cannot find private field '{fieldName}' on {target.GetType().Name}. "
                + "The osu! framework may have changed; the BMS preview-track hook needs updating.",
                level: LogLevel.Error);
            throw new InvalidOperationException(
                $"Cannot find private field '{fieldName}' on {target.GetType().Name}. "
                + "The osu! framework may have changed; the BMS preview-track hook needs updating.");
        }

        var value = field.GetValue(target);

        if (value == null)
        {
            Logger.Log(
                $"BMS WorkingBeatmapCache: Field '{fieldName}' on {target.GetType().Name} is null — unexpected.",
                level: LogLevel.Error);
            throw new InvalidOperationException(
                $"Field '{fieldName}' on {target.GetType().Name} is null — unexpected.");
        }

        return (T)value;
    }

    #endregion
}

/// <summary>
///     Static helper that patches a <see cref="BeatmapManager" /> via reflection to replace its
///     private <c>workingBeatmapCache</c> field with a <see cref="BmsWorkingBeatmapCache" />.
/// </summary>
/// <remarks>
///     This is the injection point that enables BMS preview audio with zero changes to the osu!
///     game assembly.  Call <see cref="Install" /> once after the <c>BeatmapManager</c> is created,
///     ideally from the host application's startup code:
///     <code>
///         var manager = new BeatmapManager(...);
///         BmsWorkingBeatmapHelper.Install(manager);
///     </code>
/// </remarks>
public static class BmsWorkingBeatmapHelper
{
    private static volatile bool installed;
    private static readonly object install_lock = new();

    /// <summary>
    ///     Replace the private <c>workingBeatmapCache</c> inside <paramref name="manager" />
    ///     with a BMS-aware wrapper.  Safe to call multiple times (idempotent).
    /// </summary>
    /// <param name="manager">The beatmap manager whose cache should be wrapped.</param>
    /// <returns><c>true</c> if the installation succeeded; <c>false</c> otherwise.</returns>
    public static bool Install(BeatmapManager manager)
    {
        if (installed) return true;

        lock (install_lock)
        {
            if (installed) return true;

            var cacheField = typeof(BeatmapManager).GetField("workingBeatmapCache",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (cacheField == null)
            {
                Logger.Log(
                    "BMS WorkingBeatmapHelper: Cannot find private field 'workingBeatmapCache' on BeatmapManager. "
                    + "The osu! framework may have changed; BMS preview audio will not be available.",
                    level: LogLevel.Error);
                return false;
            }

            var original = cacheField.GetValue(manager) as WorkingBeatmapCache;

            if (original == null || original is BmsWorkingBeatmapCache)
                return original != null;

            try
            {
                var wrapped = BmsWorkingBeatmapCache.Wrap(original);
                setReadonlyField(manager, cacheField, wrapped);
                installed = true;
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    /// <summary>
    ///     Writes to a (potentially readonly) instance field, using a <c>DynamicMethod</c>
    ///     fallback when <c>FieldInfo.SetValue</c> is blocked by runtime readonly checks
    ///     (modern .NET 5+).
    /// </summary>
    private static void setReadonlyField(object target, FieldInfo field, object value)
    {
        try
        {
            field.SetValue(target, value);
            return;
        }
        catch (FieldAccessException)
        {
            // Modern .NET may block SetValue on init-only / readonly fields; fall through to IL emit.
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"BMS WorkingBeatmapHelper: Failed to set readonly field '{field.Name}' via SetValue. "
                             + "BMS preview-track hook will not be active.");
            return;
        }

        try
        {
            var dynamicMethod = new DynamicMethod(
                $"WriteField_{field.Name}",
                null,
                [typeof(object), typeof(object)],
                typeof(FieldInfo).Module,
                true);

            var il = dynamicMethod.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, field);
            il.Emit(OpCodes.Ret);

            dynamicMethod.Invoke(null, [target, value]);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"BMS WorkingBeatmapHelper: Failed to write readonly field '{field.Name}' via IL emit. "
                             + "BMS preview-track hook will not be active.");
        }
    }
}
