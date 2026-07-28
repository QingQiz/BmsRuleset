using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Tests.Visual;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsPauseRewind : BmsPlayerTestScene
{
    private const double judged_note_time = 9000;
    private const double pause_time = 10000;
    private const double pause_boundary_note_time = pause_time - 100;

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => new(allowPause: true, showResults: false);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            HitObjects =
            {
                new BmsNote { StartTime = judged_note_time, Column = 1 },
                new BmsNote { StartTime = pause_boundary_note_time, Column = 2 },
                new BmsNote { StartTime = 30000, Column = 1 },
            },
        };

        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset, endPadding: 3000, bpm: 120);
        return beatmap;
    }

    [Test]
    public void TestResumeRewindsImmediatelyWithoutResettingJudgement()
    {
        AddStep("load player", () => LoadPlayer());
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Playfield.Stage.IsLoaded);

        AddStep("seek to judged note", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(judged_note_time);
            Player.GameplayClockContainer.Start();
        });
        AddStep("stop at judged note", () => Player.GameplayClockContainer.Stop());
        AddStep("judge note", () => Playfield.Stage.Columns[1].HandlePress(judged_note_time));
        AddAssert("note judged once", () => Player.ScoreProcessor.JudgedHits, () => Is.EqualTo(1));

        AddStep("seek to pause time", () =>
        {
            Player.GameplayClockContainer.Seek(pause_time);
            Player.GameplayClockContainer.Start();
        });
        AddStep("pause", () => Player.Pause());
        AddAssert("pause recorded", () => Player.Score.ScoreInfo.Pauses, () => Has.Count.EqualTo(1));

        AddStep("resume", () => Player.Resume());
        AddAssert("rewind applied next frame", () =>
        {
            var expected = expectedRewindTime();
            var frameTime = Player.DrawableRuleset.FrameStableClock.CurrentTime;

            return System.Math.Abs(Player.GameplayClockContainer.CurrentTime - expected) <= 250
                   && System.Math.Abs(frameTime - expected) <= 250
                   && Playfield.DisplayTime - frameTime > 1000
                   && Player.ScoreProcessor.JudgedHits == 1;
        });
        AddUntilStep("visual rewind completes", () => !Playfield.IsResumeRewindAnimating);
        AddAssert("display clock rejoins gameplay", () => Playfield.DisplayTime, () => Is.EqualTo(Player.DrawableRuleset.FrameStableClock.CurrentTime).Within(20));
        AddAssert("resume rewind active", () => Playfield.IsResumeRewinding);
        AddAssert("judgement retained", () => Player.ScoreProcessor.JudgedHits, () => Is.EqualTo(1));
        AddStep("press pause during rewind", () => InputManager.Click(MouseButton.Middle));
        AddAssert("gameplay paused during rewind", () => Player.GameplayClockContainer.IsPaused.Value);
        AddAssert("second pause recorded", () => Player.Score.ScoreInfo.Pauses, () => Has.Count.EqualTo(2));
        AddStep("resume again", () => Player.Resume());
        AddAssert("reuses first rewind target", () => Player.GameplayClockContainer.CurrentTime, () => Is.EqualTo(pause_time - BmsPlayfield.RESUME_REWIND_DURATION).Within(250));
        AddAssert("rewind window unchanged", () => Playfield.ResumeRewindEndTime, () => Is.EqualTo(pause_time).Within(250));
    }

    [Test]
    public void TestNoteCrossingJudgementLineAtPauseRemainsHittable()
    {
        var judgedHitsBeforePress = 0;

        AddStep("load player", () => LoadPlayer());
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Playfield.Stage.IsLoaded);
        AddStep("seek to pause boundary", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(pause_time);
            Player.GameplayClockContainer.Start();
        });
        AddStep("pause", () => Player.Pause());
        AddStep("resume", () => Player.Resume());
        AddUntilStep("boundary note reaches line during rewind", () =>
            Playfield.IsResumeRewinding
            && Player.GameplayClockContainer.CurrentTime >= pause_boundary_note_time
            && Playfield.AllColumnAliveObjects().Any(drawable => drawable.HitObject.StartTime == pause_boundary_note_time));
        AddStep("capture judged hits", () => judgedHitsBeforePress = Player.ScoreProcessor.JudgedHits);
        AddStep("press boundary note key", () => InputManager.PressKey(Key.S));
        AddAssert("boundary note judged on key down", () => Player.ScoreProcessor.JudgedHits, () => Is.EqualTo(judgedHitsBeforePress + 1));
        AddStep("release boundary note key", () => InputManager.ReleaseKey(Key.S));
    }

    private double expectedRewindTime() => Player.Score.ScoreInfo.Pauses.Single() - BmsPlayfield.RESUME_REWIND_DURATION;
}
