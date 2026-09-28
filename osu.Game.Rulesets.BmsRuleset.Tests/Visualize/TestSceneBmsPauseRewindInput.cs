using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Tests.Visual;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsPauseRewindInput : BmsPlayerTestScene
{
    private const double pause_time = 10000;
    private BmsLayoutVariant layout;
    private BmsNote boundaryNote = null!;

    [TestCase(BmsLayoutVariant.Bme7K, true)]
    [TestCase(BmsLayoutVariant.Bme7K, false)]
    [TestCase(BmsLayoutVariant.Pms9K, false)]
    public void TestEmptyPressesDuringResumeLeadIn(BmsLayoutVariant variant, bool consumedNote)
    {
        var healthBeforePress = 0d;
        var eventsBeforePress = 0;
        var comboBeforePress = 0;
        var column = consumedNote ? 1 : 2;
        var key = variant == BmsLayoutVariant.Pms9K ? Key.D : consumedNote ? Key.Z : Key.S;
        var postRewindKey = variant == BmsLayoutVariant.Pms9K ? Key.F : Key.X;

        AddStep("load player", () =>
        {
            layout = variant;
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Playfield.Stage.IsLoaded);
        AddStep("freeze gameplay time", freezeGameplayTime);
        seekTo(9000);
        AddStep("judge first note", () => Playfield.Stage.Columns[1].HandlePress(9000));
        AddStep("release first note", () => Playfield.Stage.Columns[1].HandleRelease(9000));
        AddAssert("first note judged", () => Player.ScoreProcessor.JudgedHits, () => Is.EqualTo(1));
        seekTo(pause_time);
        AddStep("pause", () => Player.Pause());
        AddStep("resume", () => Player.Resume());
        AddUntilStep("resume rewind active", () => Playfield.IsResumeRewinding);
        AddStep("freeze lead-in time", freezeGameplayTime);
        seekTo(consumedNote ? 8600 : 9700);
        AddAssert("press time is inside lead-in", () => Playfield.IsResumeRewinding);
        AddStep("capture score and gauge", () =>
        {
            healthBeforePress = Player.HealthProcessor.Health.Value;
            eventsBeforePress = ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.Count;
            comboBeforePress = Player.ScoreProcessor.Combo.Value;
        });
        for (var i = 0; i < 5; i++)
        {
            AddStep("press during lead-in", () => InputManager.PressKey(key));
            AddAssert("held state updated", () => ((BmsColumn)Playfield.Stage.Columns[column]).IsPressed);
            AddStep("release during lead-in", () => InputManager.ReleaseKey(key));
            AddAssert("released state updated", () => !((BmsColumn)Playfield.Stage.Columns[column]).IsPressed);
        }

        AddAssert("lead-in presses preserve judgement history", () => ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.Count,
            () => Is.EqualTo(eventsBeforePress));
        AddAssert("lead-in presses preserve gauge", () => Player.HealthProcessor.Health.Value, () => Is.EqualTo(healthBeforePress));
        AddAssert("lead-in presses preserve combo", () => Player.ScoreProcessor.Combo.Value, () => Is.EqualTo(comboBeforePress));
        AddAssert("pending note has no PMS mistake", () => !((BmsColumn)Playfield.Stage.Columns[2]).HasPmsMistake(boundaryNote));

        seekTo(10600);
        AddAssert("lead-in finished", () => !Playfield.IsResumeRewinding);
        AddStep("capture gauge after lead-in", () => healthBeforePress = Player.HealthProcessor.Health.Value);
        AddStep("press early after lead-in", () => InputManager.PressKey(postRewindKey));
        AddAssert("empty POOR resumes after lead-in", () => ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.Count(e => e.Result == HitResult.Miss),
            () => Is.EqualTo(1));
        AddAssert("empty POOR drains gauge after lead-in", () => Player.HealthProcessor.Health.Value < healthBeforePress);
        AddStep("release after lead-in", () => InputManager.ReleaseKey(postRewindKey));
    }

    private void freezeGameplayTime()
    {
        // Stop() pauses the input pipeline too; freeze only the source clock to exercise real key events at fixed times.
        typeof(GameplayClockContainer).GetMethod("StopGameplayClock", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(Player.GameplayClockContainer, null);
    }

    private void seekTo(double time)
    {
        AddStep("seek to " + time, () =>
        {
            Player.GameplayClockContainer.Seek(time);
            freezeGameplayTime();
        });
        AddUntilStep("simulation reaches " + time, () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < 0.001);
    }

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => new(true, false);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = layout,
            TotalColumns = layout == BmsLayoutVariant.Pms9K ? 9 : 8,
            Rank = 2,
            Total = 30,
            HitObjects =
            [
                new BmsNote { StartTime = 9000, Column = 1 },
                boundaryNote = new BmsNote { StartTime = 10100, Column = 2 },
                new BmsNote { StartTime = 11000, Column = 3 },
                new BmsNote { StartTime = 30000, Column = 1 },
            ],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset, 3000, 120);
        return beatmap;
    }
}
