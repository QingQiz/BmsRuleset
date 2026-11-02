using System.Collections.Generic;
using System.IO;
using System.Threading;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Skinning;
using osu.Game.Storyboards;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

public sealed class BmsPracticeWorkingBeatmap : WorkingBeatmap
{
    public BmsPracticeSession Session { get; }

    public BmsPracticeWorkingBeatmap(BmsPracticeSession session, AudioManager audio)
        : base(session.Source.BeatmapInfo.Clone(), audio)
    {
        Session = session;
    }

    public override IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods, CancellationToken token)
        => BmsPracticeBeatmap.Create(Session.GetFullBeatmap(token), Session.Settings);

    protected override IBeatmap GetBeatmap()
    {
        // WorkingBeatmap copies database metadata into the returned map, so this must not be the shared decoded source.
        var source = Session.Source.Beatmap;
        var beatmap = source.Clone();
        beatmap.BeatmapInfo = source.BeatmapInfo.Clone();
        return beatmap;
    }

    public override Texture GetBackground() => Session.Source.GetBackground();

    public override Texture GetPanelBackground() => Session.Source.GetPanelBackground();

    public override Stream GetStream(string storagePath) => Session.Source.GetStream(storagePath);

    // Each attempt owns a silent clock track; its BMS sample voices are provided by gameplay.
    protected override Track GetBeatmapTrack() => GetVirtualTrack();

    protected override ISkin GetSkin() => Session.Source.Skin;

    protected override Storyboard GetStoryboard() => Session.Source.Storyboard;

    protected override Waveform GetWaveform() => Session.Source.Waveform;
}
