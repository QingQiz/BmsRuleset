#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Audio.Track;
using osu.Framework.Testing;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Audio;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Tests.Normal;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Storyboards;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsInitialBackgroundAudio : BmsPlayerTestScene
{
    private ushort backgroundKey;
    private int sampleDefinitionCount;

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
        sampleDefinitionCount = beatmap.SampleDefinitions.Count;
        return beatmap;
    }

    [Test]
    public void TestLongBackgroundTrackStartsDuringLeadIn()
    {
        AddStep("load player", LoadPlayer);
        AddUntilStep("drawable ruleset created", () => Player.DrawableRuleset != null);
        AddUntilStep("sample tracks created", () => getTracks().Count == sampleDefinitionCount);
        AddUntilStep("quarter of sample tracks loaded", () => getLoadedTrackRatio() >= 0.25);
        AddUntilStep("half of sample tracks loaded", () => getLoadedTrackRatio() >= 0.5);
        AddUntilStep("three quarters of sample tracks loaded", () => getLoadedTrackRatio() >= 0.75);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        AddUntilStep("sample store loaded", () => getSampleStore().IsLoaded);
        AddAssert("still before first note", () => Player.GameplayClockContainer.CurrentTime < Player.GameplayState.Beatmap.HitObjects[0].StartTime);
        AddAssert("sample playback enabled", () => !((ISamplePlaybackDisabler)Player).SamplePlaybackDisabled.Value);
        AddAssert("frame clock not catching up", () => !Player.DrawableRuleset.FrameStableClock.IsCatchingUp.Value);
        AddUntilStep("past first background event", () => Player.GameplayClockContainer.CurrentTime >= 1000);
        AddUntilStep("background starts or first note reached", () =>
            getBackgroundTrack()?.IsRunning == true
            || Player.GameplayClockContainer.CurrentTime >= Player.GameplayState.Beatmap.HitObjects[0].StartTime);
        AddAssert("long background track started before first note", () =>
            getBackgroundTrack()?.IsRunning == true
            && Player.GameplayClockContainer.CurrentTime < Player.GameplayState.Beatmap.HitObjects[0].StartTime);
        AddUntilStep("background advances or first note reached", () =>
            getBackgroundTrack()?.CurrentTime > 100
            || Player.GameplayClockContainer.CurrentTime >= Player.GameplayState.Beatmap.HitObjects[0].StartTime);
        AddAssert("long background track advances before first note", () =>
            getBackgroundTrack()?.CurrentTime > 100
            && Player.GameplayClockContainer.CurrentTime < Player.GameplayState.Beatmap.HitObjects[0].StartTime);
        AddAssert("long background track is audible", () => getBackgroundTrack()?.AggregateVolume.Value > 0);
    }

    private BmsSampleStore getSampleStore() =>
        (BmsSampleStore)typeof(BmsDrawableRuleset)
            .GetField("sampleStore", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(Player.DrawableRuleset)!;

    private Track? getBackgroundTrack() => getSampleStore().GetTrack(backgroundKey);

    private Dictionary<ushort, Track> getTracks() =>
        (Dictionary<ushort, Track>)typeof(BmsSampleStore)
            .GetField("tracks", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(getSampleStore())!;

    private double getLoadedTrackRatio()
    {
        var tracks = getTracks();
        return (double)tracks.Values.Count(track => track.IsLoaded) / tracks.Count;
    }
}
