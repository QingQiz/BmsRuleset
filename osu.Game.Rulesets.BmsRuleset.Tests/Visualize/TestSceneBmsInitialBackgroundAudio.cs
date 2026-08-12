#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;
using osu.Game.Rulesets.BmsRuleset.Tests.Normal;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Storyboards;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsInitialBackgroundAudio : BmsPlayerTestScene
{
    private ushort backgroundKey;

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(null);

    protected override WorkingBeatmap CreateWorkingBeatmap(IBeatmap beatmap, Storyboard storyboard = null!)
    {
        var working = new BmsWorkingBeatmap(base.CreateWorkingBeatmap(beatmap, storyboard), Audio);
        working.LoadTrack().Start();
        return working;
    }

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var directory = Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, "Destr0yer (by 削除 feat. Nikki Simmons)");
        var path = Path.Combine(directory, "destr0yer_starhyper.bms");

        using var stream = new MemoryStream(File.ReadAllBytes(path));
        using var reader = new LineBufferedReader(stream);
        var decoded = new BmsBeatmapDecoder().Decode(reader);
        var beatmap = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()).Convert();

        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset, endPadding: 3000, bpm: 120);
        beatmap.BeatmapInfo.Metadata.Source = directory;
        backgroundKey = beatmap.SampleDefinitions.Single(pair => pair.Value.Equals("bgm1.wav", StringComparison.OrdinalIgnoreCase)).Key;
        return beatmap;
    }

    [Test]
    public void TestLongBackgroundTrackStartsDuringLeadIn()
    {
        AddStep("load player", LoadPlayer);
        AddUntilStep("drawable ruleset created", () => Player.DrawableRuleset != null);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        AddUntilStep("sample playback loaded", () => getSamplePlayback().IsLoaded);
        AddUntilStep("initial background sample loaded", () => getSamplePlayback().IsSampleReady(backgroundKey));
        AddAssert("still before first note", () => Player.GameplayClockContainer.CurrentTime < Player.GameplayState.Beatmap.HitObjects[0].StartTime);
        AddAssert("sample playback enabled", () => !((ISamplePlaybackDisabler)Player).SamplePlaybackDisabled.Value);
        AddAssert("frame clock not catching up", () => !Player.DrawableRuleset.FrameStableClock.IsCatchingUp.Value);
        AddUntilStep("past first background event", () => Player.GameplayClockContainer.CurrentTime >= 1000);
        AddUntilStep("background starts or first note reached", () =>
            getSamplePlayback().DiagnosticSnapshot.Audio.ActiveVoices > 0
            || Player.GameplayClockContainer.CurrentTime >= Player.GameplayState.Beatmap.HitObjects[0].StartTime);
        AddAssert("long background sample started before first note", () =>
            getSamplePlayback().DiagnosticSnapshot.Audio.ActiveVoices > 0
            && Player.GameplayClockContainer.CurrentTime < Player.GameplayState.Beatmap.HitObjects[0].StartTime);
        AddUntilStep("background is audible or first note reached", () =>
            getSamplePlayback().DiagnosticSnapshot.Audio.OutputPeak > 0
            || Player.GameplayClockContainer.CurrentTime >= Player.GameplayState.Beatmap.HitObjects[0].StartTime);
        AddAssert("long background sample is audible", () => getSamplePlayback().DiagnosticSnapshot.Audio.OutputPeak > 0);
    }

    private BmsSamplePlayback getSamplePlayback() =>
        (BmsSamplePlayback)typeof(BmsDrawableRuleset)
            .GetField("samplePlayback", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(Player.DrawableRuleset)!;

}
