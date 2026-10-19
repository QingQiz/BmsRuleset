#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Storyboards;
using osu.Game.Scoring;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsZeroTimeNotes : BmsPlayerTestScene
{
    private BmsTestSkins.SkinKind skin;
    private bool useReplay;
    private BmsWorkingBeatmap workingBeatmap = null!;
    private readonly List<(double Time, float Position)> visiblePositions = [];
    private double previousStartupTime;
    private double timeBeforeFirstVisible;
    private bool observeStartup;

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(
        useReplay ? _ => [new BmsReplayFrame(-3000), new BmsReplayFrame(12000)] : null, skin);

    protected override WorkingBeatmap CreateWorkingBeatmap(IBeatmap beatmap, Storyboard storyboard = null!)
    {
        workingBeatmap = new BmsWorkingBeatmap(base.CreateWorkingBeatmap(beatmap, storyboard), Audio);
        workingBeatmap.LoadTrack().Start();
        return workingBeatmap;
    }

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = BmsTestBeatmaps.CreateBeatmapFromChart("""
                                                            #TITLE Zero-time notes
                                                            #BPM 120
                                                            #LNTYPE 1
                                                            #00012:01
                                                            #00051:0101
                                                            #00411:01
                                                            """);
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset, bpm: 120);
        return beatmap;
    }

    [Test]
    public void TestLegacyReplayAtZeroSurvivesRewind()
    {
        Score legacy = null!;
        double[] nativeTimes = [];
        AddStep("load replay player", () =>
        {
            skin = BmsTestSkins.SkinKind.Argon;
            useReplay = true;
            observeStartup = false;
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        AddStep("load version 4 replay with historical lead-in", () =>
        {
            Player.GameplayClockContainer.Stop();
            var replay = new BmsAutoGenerator((BmsBeatmap)Player.GameplayState.Beatmap).Generate();
            nativeTimes = replay.Frames.Select(frame => frame.Time).ToArray();
            replay.Frames = replay.Frames.Cast<BmsReplayFrame>().Select(frame => (osu.Game.Rulesets.Replays.ReplayFrame)
                new BmsReplayFrame(frame.Time + 2000, frame.Actions.ToArray()) { Version = 4 }).ToList();
            legacy = BmsTestReplayArchive.RoundTrip(new Score { Replay = replay },
                chart: "#BPM 120\n#LNTYPE 1\n#00012:01\n#00051:0101\n#00411:01");
            Player.DrawableRuleset.SetReplayScore(legacy);
        });
        AddAssert("player exposes native frame times", () => legacy.Replay.Frames.Select(frame => frame.Time), () => Is.EqualTo(nativeTimes));
        for (var i = 0; i < 2; i++)
        {
            AddStep("rewind before chart zero", () => Player.Seek(-2000));
            AddUntilStep("rewound", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime + 2000) < 0.001);
            AddStep("seek past all notes", () => Player.Seek(8500));
            AddUntilStep("simulation caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 8500) < 0.001);
            AddAssert("all four endpoints hit on time", () =>
            {
                var observations = ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.SelectMany(entry => entry.TimingObservations).ToArray();
                return observations.Length == 4 && observations.All(entry => Math.Abs(entry.TimeOffset) < 0.001);
            });
            AddStep("reload corrected replay", () => Player.DrawableRuleset.SetReplayScore(legacy));
        }
    }

    [Test]
    public void TestZeroTimeNotesEnterFromTop(
        [Values(BmsTestSkins.SkinKind.Argon, BmsTestSkins.SkinKind.Classic, BmsTestSkins.SkinKind.Legacy)] BmsTestSkins.SkinKind selectedSkin,
        [Values] bool replay)
    {
        AddStep("load player with zero-time notes", () =>
        {
            skin = selectedSkin;
            useReplay = replay;
            visiblePositions.Clear();
            previousStartupTime = double.NaN;
            timeBeforeFirstVisible = double.NaN;
            observeStartup = true;
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        AddAssert("uses real preview track", () => workingBeatmap.Track is BmsPreviewTrack);
        AddAssert("chart starts at zero", () => Player.GameplayState.Beatmap.HitObjects[0].StartTime, () => Is.Zero);
        AddAssert("player starts two seconds before zero", () => Player.GameplayClockContainer.StartTime, () => Is.EqualTo(-2000));
        AddUntilStep("natural playback passes zero", () => Player.GameplayClockContainer.CurrentTime > 50);
        AddStep("stop observing startup", () => observeStartup = false);
        AddStep("report startup geometry", () => TestContext.WriteLine(
            $"ZERO_TIME_STARTUP skin={skin} replay={useReplay} start={Player.GameplayClockContainer.StartTime} "
            + $"previous={timeBeforeFirstVisible} first={visiblePositions.FirstOrDefault()} last={visiblePositions.LastOrDefault()} frames={visiblePositions.Count}"));
        AddAssert("observed scrolling before zero", () => visiblePositions.Count, () => Is.GreaterThan(10));
        AddAssert("note enters at top within first observed frame", () =>
        {
            var first = visiblePositions[0];
            var last = visiblePositions[^1];
            // A slow host frame can span the top edge; use the measured constant-speed motion to bound that unobserved interval.
            var speed = (last.Position - first.Position) / (last.Time - first.Time);
            return first.Position - speed * (first.Time - timeBeforeFirstVisible);
        }, () => Is.LessThan(0.15));
        AddAssert("note visible during negative time", () => visiblePositions[0].Time, () => Is.LessThan(-500));
        AddAssert("note scrolls down continuously", () => visiblePositions.Zip(visiblePositions.Skip(1))
            .All(pair => pair.Second.Position >= pair.First.Position - 0.001f));
        // The framework freezes live-play simulation while paused; replays can seek to an exact frame.
        if (!replay)
            return;

        AddStep("pause at chart zero", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.Seek(0);
        });
        AddUntilStep("simulation reaches zero", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime) < 0.001);
        AddUntilStep("both zero-time notes alive", () => zeroTimeNotes().Length == 2);
        AddAssert("tap and long-note head reach judgement line", () => zeroTimeNotes().All(note =>
            Math.Abs(note.ToScreenSpace(Vector2.Zero).Y - Playfield.JudgementLineY()) < 1));
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();

        if (!observeStartup || Player?.IsLoaded != true || !Player.LoadedBeatmapSuccessfully
                            || Player.Alpha <= 0 || !Playfield.Stage.IsLoaded)
            return;

        var time = Player.DrawableRuleset.FrameStableClock.CurrentTime;
        var previousTime = double.IsNaN(previousStartupTime) ? Player.GameplayClockContainer.StartTime : previousStartupTime;
        previousStartupTime = time;

        var note = zeroTimeNotes().FirstOrDefault(n => n.HitObject is not BmsLongNote);
        if (note == null || !note.IsPresent)
            return;

        var top = Playfield.Stage.ToScreenSpace(Vector2.Zero).Y;
        var line = Playfield.JudgementLineY();
        var y = note.ToScreenSpace(Vector2.Zero).Y;
        if (line > top && y >= top && y <= line)
        {
            if (visiblePositions.Count == 0)
                timeBeforeFirstVisible = previousTime;

            visiblePositions.Add((time, (y - top) / (line - top)));
        }
    }

    private DrawableBmsHitObject[] zeroTimeNotes() => Playfield.AllColumnAliveObjects().OfType<DrawableBmsHitObject>()
        .Where(note => note.HitObject.StartTime == 0).ToArray();
}
