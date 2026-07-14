using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Logging;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Models;
using osu.Game.Rulesets.BmsRuleset.Audio;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Skinning;
using osu.Game.Storyboards;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps;

/// <summary>
///     A <see cref="WorkingBeatmap" /> that wraps a normally-created working beatmap but
///     overrides <see cref="GetBeatmapTrack" /> to return a <see cref="BmsPreviewTrack" />
///     for BMS charts.  All other members are delegated to the inner working beatmap.
/// </summary>
public class BmsWorkingBeatmap(WorkingBeatmap inner, AudioManager audioManager, TextureStore? externalTextureStore = null)
    : WorkingBeatmap(createWrapperBeatmapInfo(inner), audioManager)
{

    internal static BmsPreviewTrack? ActivePreviewTrack { get; private set; }

    private readonly AudioManager audioManager = audioManager;

    private bool externalBackgroundResolved;
    private List<string> resolvedBackgroundPaths = null!;
    private List<string> resolvedPanelBackgroundPaths = null!;

    public override bool TryTransferTrack(WorkingBeatmap target)
    {
        if (!TrackLoaded || target is not BmsWorkingBeatmap || BeatmapInfo.ID != target.BeatmapInfo.ID)
            return false;

        return base.TryTransferTrack(target);
    }

    public override Texture GetBackground() => getExternalBackground(false) ?? inner.GetBackground();

    public override Texture GetPanelBackground() => getExternalBackground(true) ?? inner.GetPanelBackground();

    public override Stream GetStream(string storagePath) => inner.GetStream(storagePath);

    /// <summary>
    ///     Switches the currently-active preview track (if any) to clock-only mode.  Gameplay
    ///     seeks and starts this track as a clock source, but audible BMS playback should come from the
    ///     <see cref="BmsBackgroundAudioPlayer" />.
    /// </summary>
    internal static void SwitchActivePreviewToGameplayClockOnly()
    {
        var track = ActivePreviewTrack;

        track?.PlaybackMode = BmsPreviewTrackPlaybackMode.GameplayClockOnly;
    }

    internal static void RestoreActivePreview()
    {
        var track = ActivePreviewTrack;

        if (track == null || track.IsDisposed)
            return;

        track.PlaybackMode = BmsPreviewTrackPlaybackMode.Preview;
        track.Volume.Value = 1;
        track.Start();
    }

    protected override Track GetBeatmapTrack()
    {
        var beatmap = Beatmap;

        if (beatmap is IBmsBeatmap bmsBeatmap)
        {
            var track = new BmsPreviewTrack(
                () => createPreviewEvents(beatmap, bmsBeatmap),
                bmsBeatmap.SampleDefinitions,
                Metadata.Source,
                audioManager,
                bmsBeatmap.PreviewFile,
                BmsRuleset.UseDedicatedPreviewAudio);

            // Stop and remove the previous preview track before registering the new one.
            // Disposing via the audio update loop also releases the per-chart SampleStore.
            if (ActivePreviewTrack != null)
            {
                ActivePreviewTrack.Stop();
                ActivePreviewTrack.Dispose();
            }

            audioManager.AddItem(track);
            ActivePreviewTrack = track;

            return track;
        }

        return null!; // fall back to TrackVirtual by WorkingBeatmap.LoadTrack
    }

    private static IReadOnlyList<BmsSampleEvent> createPreviewEvents(IBeatmap beatmap, IBmsBeatmap bmsBeatmap)
    {
        var allEvents = new List<BmsSampleEvent>(
            bmsBeatmap.BackgroundSampleEvents.Count
            + beatmap.HitObjects.Count);

        allEvents.AddRange(bmsBeatmap.BackgroundSampleEvents);

        // Song preview needs playable keysounds alongside #01 BGM because gameplay normally
        // routes those two sources through separate players.
        foreach (var obj in beatmap.HitObjects)
        {
            if (obj is not BmsHitObject hit || hit is BmsLandmine)
                continue;

            if (hit.SampleKey != 0)
                allEvents.Add(new BmsSampleEvent(hit.StartTime, 0, hit.SampleKey, hit.SampleVolume));
            if (hit is BmsLongNote ln && ln.TailSampleKey != 0)
                allEvents.Add(new BmsSampleEvent(hit.StartTime + ln.Duration, 0, ln.TailSampleKey, ln.TailSampleVolume));
        }

        allEvents.Sort(static (a, b) => a.Time.CompareTo(b.Time));
        return allEvents;
    }

    protected override IBeatmap GetBeatmap() => tryDecodeExternalBeatmap(BeatmapInfo) ?? inner.Beatmap;

    protected override ISkin GetSkin() => inner.Skin;

    protected override Storyboard GetStoryboard() => inner.Storyboard;

    protected override Waveform GetWaveform() => inner.Waveform;

    private static BeatmapInfo createWrapperBeatmapInfo(WorkingBeatmap inner)
    {
        var beatmapInfo = cloneBeatmapInfo(inner.BeatmapInfo);
        var bmsBeatmap = tryDecodeExternalBeatmap(beatmapInfo) as IBmsBeatmap ?? inner.Beatmap as IBmsBeatmap;
        var backgroundPaths = resolveExternalBackgroundPaths(beatmapInfo.Metadata.Source, bmsBeatmap, false);
        var panelBackgroundPaths = resolveExternalBackgroundPaths(beatmapInfo.Metadata.Source, bmsBeatmap, true);
        applyExternalBackgroundMarker(beatmapInfo, backgroundPaths.FirstOrDefault(), panelBackgroundPaths.FirstOrDefault());

        return beatmapInfo;
    }

    private static BeatmapInfo cloneBeatmapInfo(BeatmapInfo source)
    {
        var clone = source.Clone();
        clone.Metadata = source.Metadata.DeepClone();
        clone.BeatmapSet = cloneBeatmapSetInfo(clone.BeatmapSet, clone);
        return clone;
    }

    private static BeatmapSetInfo? cloneBeatmapSetInfo(BeatmapSetInfo? source, BeatmapInfo owner)
    {
        if (source == null)
            return null;

        var clone = new BeatmapSetInfo
        {
            ID = source.ID,
            OnlineID = source.OnlineID,
            DateAdded = source.DateAdded,
            DateSubmitted = source.DateSubmitted,
            DateRanked = source.DateRanked,
            Status = source.Status,
            DeletePending = source.DeletePending,
            Hash = source.Hash,
            Protected = source.Protected,
        };

        foreach (var file in source.Files)
            clone.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = file.File.Hash }, file.Filename));

        clone.Beatmaps.Add(owner);

        return clone;
    }

    private static void applyExternalBackgroundMarker(BeatmapInfo beatmapInfo, string? backgroundPath, string? panelBackgroundPath)
    {
        var markerPath = panelBackgroundPath ?? backgroundPath;

        if (markerPath == null)
            return;

        // Song-select panels check this metadata before loading textures, so it must be ready
        // as soon as the wrapper is constructed rather than on first GetBackground().
        beatmapInfo.Metadata.BackgroundFile = markerPath;

        if (beatmapInfo.BeatmapSet?.GetFile(markerPath) == null)
            beatmapInfo.BeatmapSet?.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = $"{backgroundPath}|{panelBackgroundPath}" }, markerPath));
    }

    private static IBeatmap? tryDecodeExternalBeatmap(BeatmapInfo beatmapInfo)
    {
        var chartPath = tryResolveExternalChartPath(beatmapInfo);

        if (chartPath == null)
            return null;

        try
        {
            return BmsBeatmapDecoder.DecodeBytes(File.ReadAllBytes(chartPath), cloneBeatmapInfo(beatmapInfo));
        }
        catch (Exception e)
        {
            Logger.Error(e, $"BMS external beatmap decode failed for {chartPath}");
            return null;
        }
    }

    private static string? tryResolveExternalChartPath(BeatmapInfo beatmapInfo)
    {
        if (string.IsNullOrWhiteSpace(beatmapInfo.Metadata.Source) || !Directory.Exists(beatmapInfo.Metadata.Source) || string.IsNullOrWhiteSpace(beatmapInfo.Path))
            return null;

        var baseFullPath = Path.GetFullPath(beatmapInfo.Metadata.Source);

        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(baseFullPath, beatmapInfo.Path));
            return BmsFileResourceStore.IsPathInsideDirectory(fullPath, baseFullPath) && File.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<string> resolveExternalBackgroundPaths(string? sourceDirectory, IBmsBeatmap? bmsBeatmap, bool preferBanner)
    {
        var paths = new List<string>();

        // Source is only a directory for external-audio charts; bail before touching
        // inner.Beatmap so normal imports don't pay for a decode just to resolve backgrounds.
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
            return paths;

        if (bmsBeatmap == null)
            return paths;

        var baseFullPath = Path.GetFullPath(sourceDirectory);

        foreach (var candidate in getBackgroundCandidates(bmsBeatmap, preferBanner))
        {
            string fullPath;

            try
            {
                fullPath = Path.GetFullPath(Path.Combine(baseFullPath, candidate));
            }
            catch (Exception)
            {
                // #STAGEFILE / #BACKBMP / #BANNER values are chart-controlled and only
                // quote-trimmed by the parser; illegal path characters would otherwise throw
                // and crash background loading for the whole chart.
                continue;
            }

            if (BmsFileResourceStore.IsPathInsideDirectory(fullPath, baseFullPath) && File.Exists(fullPath))
                paths.Add(fullPath);
        }

        return paths;
    }

    private static IEnumerable<string> getBackgroundCandidates(IBmsBeatmap bmsBeatmap, bool preferBanner)
    {
        if (preferBanner && !string.IsNullOrWhiteSpace(bmsBeatmap.Banner))
            yield return bmsBeatmap.Banner;

        foreach (var candidate in bmsBeatmap.GetSongSelectBackgroundCandidates())
        {
            if (preferBanner && string.Equals(candidate, bmsBeatmap.Banner, StringComparison.OrdinalIgnoreCase))
                continue;

            yield return candidate;
        }
    }

    private Texture? getExternalBackground(bool preferBanner)
    {
        ensureExternalBackgroundResolved();

        if (externalTextureStore == null)
            return null;

        var paths = preferBanner ? resolvedPanelBackgroundPaths : resolvedBackgroundPaths;

        // Try each candidate in priority order so a corrupt or unreadable primary asset
        // (e.g. a broken #STAGEFILE) still falls back to #BACKBMP / #BANNER instead of
        // caching a permanent miss for the wrapper's lifetime.
        foreach (var path in paths)
        {
            var texture = externalTextureStore.Get(path);

            if (texture != null)
                return texture;
        }

        return null;
    }

    private void ensureExternalBackgroundResolved()
    {
        if (externalBackgroundResolved)
            return;

        externalBackgroundResolved = true;

        var bmsBeatmap = tryDecodeExternalBeatmap(BeatmapInfo) as IBmsBeatmap ?? inner.Beatmap as IBmsBeatmap;

        resolvedBackgroundPaths = resolveExternalBackgroundPaths(Metadata.Source, bmsBeatmap, false);
        resolvedPanelBackgroundPaths = resolveExternalBackgroundPaths(Metadata.Source, bmsBeatmap, true);

        var backgroundPath = resolvedBackgroundPaths.FirstOrDefault();
        var panelBackgroundPath = resolvedPanelBackgroundPaths.FirstOrDefault();
        applyExternalBackgroundMarker(BeatmapInfo, backgroundPath, panelBackgroundPath);
    }
}
