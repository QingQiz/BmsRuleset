using System.IO;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Audio;
using osu.Game.Skinning;
using osu.Game.Storyboards;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps;

/// <summary>
///     A <see cref="WorkingBeatmap" /> that wraps a normally-created working beatmap but
///     overrides <see cref="GetBeatmapTrack" /> to return a <see cref="BmsPreviewTrack" />
///     for BMS charts.  All other members are delegated to the inner working beatmap.
/// </summary>
public class BmsWorkingBeatmap : WorkingBeatmap
{
    private readonly WorkingBeatmap inner;
    private readonly AudioManager audioManager;

    public BmsWorkingBeatmap(WorkingBeatmap inner, AudioManager audioManager)
        : base(inner.BeatmapInfo, audioManager)
    {
        this.inner = inner;
        this.audioManager = audioManager;
    }

    protected override Track GetBeatmapTrack()
    {
        var beatmap = this.Beatmap;

        if (beatmap is IBmsBeatmap bmsBeatmap)
        {
            return new BmsPreviewTrack(
                bmsBeatmap.BackgroundSampleEvents,
                bmsBeatmap.SampleDefinitions,
                Metadata.Source,
                audioManager);
        }

        return null; // fall back to TrackVirtual by WorkingBeatmap.LoadTrack
    }

    protected override IBeatmap GetBeatmap() => inner.Beatmap;

    public override Texture GetBackground() => inner.GetBackground();

    public override Texture GetPanelBackground() => inner.GetPanelBackground();

    public override Stream GetStream(string storagePath) => inner.GetStream(storagePath);

    protected override ISkin GetSkin() => inner.Skin;

    protected override Storyboard GetStoryboard() => inner.Storyboard;

    protected override Waveform GetWaveform() => inner.Waveform;
}
