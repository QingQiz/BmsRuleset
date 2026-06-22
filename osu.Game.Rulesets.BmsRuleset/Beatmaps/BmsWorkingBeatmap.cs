using System.Collections.Generic;
using System.IO;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
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
public class BmsWorkingBeatmap(WorkingBeatmap inner, AudioManager audioManager)
    : WorkingBeatmap(inner.BeatmapInfo, audioManager)
{

    private readonly AudioManager audioManager = audioManager;

    /// <summary>
    ///     Track the active preview track so we can remove it from the
    ///     <see cref="AudioManager" /> before a new one takes its place.
    ///     AudioManager uses a single-threaded action queue processed before
    ///     UpdateChildren, so the removal and addition happen in the same
    ///     cycle with no window where both tracks receive Update calls.
    /// </summary>
    private static BmsPreviewTrack? activePreviewTrack;

    public override bool TryTransferTrack(WorkingBeatmap target) => false;

    public override Texture GetBackground() => inner.GetBackground();

    public override Texture GetPanelBackground() => inner.GetPanelBackground();

    public override Stream GetStream(string storagePath) => inner.GetStream(storagePath);

    /// <summary>
    ///     Stops the currently-active preview track (if any) and removes it from the
    ///     <see cref="AudioManager" />.  Safe to call from any thread.
    /// </summary>
    internal static void StopActivePreview()
    {
        var track = activePreviewTrack;

        if (track == null)
            return;

        track.SuppressEventProcessing = true;
        track.Volume.Value = 0;
        track.Stop();
    }

    protected override Track GetBeatmapTrack()
    {
        // Access inner.Beatmap directly instead of this.Beatmap to avoid
        // triggering WorkingBeatmap.loadBeatmapAsync() side effects on the
        // BmsWorkingBeatmap wrapper.  The inner BeatmapManagerWorkingBeatmap
        // already caches its decoded beatmap; no need to duplicate the work.
        var beatmap = inner.Beatmap;

        if (beatmap is IBmsBeatmap bmsBeatmap)
        {
            // Merge BGM events (#01 channel) with key sounds from every
            // playable hit-object column so the preview plays all audible
            // chart content, not just the BGM layer.
            var allEvents = new List<BmsSampleEvent>(
                bmsBeatmap.BackgroundSampleEvents.Count
                + beatmap.HitObjects.Count);

            allEvents.AddRange(bmsBeatmap.BackgroundSampleEvents);

            foreach (var obj in beatmap.HitObjects)
            {
                if (obj is not BmsHitObject { IsMine: false } hit)
                    continue;

                if (hit.SampleKey != 0)
                    allEvents.Add(new BmsSampleEvent(hit.StartTime, 0, hit.SampleKey));
                if (hit.TailSampleKey != 0)
                    allEvents.Add(new BmsSampleEvent(hit.StartTime + hit.Duration, 0, hit.TailSampleKey));
            }

            allEvents.Sort(static (a, b) => a.Time.CompareTo(b.Time));

            var track = new BmsPreviewTrack(
                allEvents,
                bmsBeatmap.SampleDefinitions,
                Metadata.Source,
                audioManager);

            // Stop and remove the previous preview track before registering the new one.
            // Removing without stopping would leave its StopwatchClock running forever.
            if (activePreviewTrack != null)
            {
                activePreviewTrack.Stop();
                audioManager.RemoveItem(activePreviewTrack);
            }

            audioManager.AddItem(track);
            activePreviewTrack = track;

            return track;
        }

        return null!; // fall back to TrackVirtual by WorkingBeatmap.LoadTrack
    }

    protected override IBeatmap GetBeatmap() => inner.Beatmap;

    protected override ISkin GetSkin() => inner.Skin;

    protected override Storyboard GetStoryboard() => inner.Storyboard;

    protected override Waveform GetWaveform() => inner.Waveform;
}
