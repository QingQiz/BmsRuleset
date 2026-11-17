using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsHitErrorMeter : BmsPlayerTestScene
{
    private BmsHitErrorMeter meter => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single();
    private BmsScoreProcessor score => (BmsScoreProcessor)Player.ScoreProcessor;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(850, BmsAction.Key1), new BmsReplayFrame(860)]);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects =
            [
                new BmsNote { StartTime = 1000, Column = 1 },
                new BmsNote { StartTime = 2000, Column = 1 },
                new BmsNote { StartTime = 10000, Column = 1 },
            ],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset, endPadding: 3000);
        return beatmap;
    }

    [TestCase(true)]
    [TestCase(false)]
    public void EarlyMissKeepsItsTimingOffset(bool lazer)
    {
        load(lazer);
        AddStep("play early miss", () => SeekReplayInFrames(850));
        AddUntilStep("early miss reaches meter", () => meter.Judgements.Lines.Count > 0);
        AddAssert("miss stays at the press offset", () =>
        {
            var line = meter.Judgements.Lines.Single();
            var domain = BmsHitErrorMeter.CreateDomain(BmsHitErrorMeter.CreateWindows(score.EngineChart));
            Assert.That(score.JudgementEvents.Single().Result, Is.EqualTo(HitResult.Miss));
            Assert.That(line.X, Is.EqualTo(domain.RelativePosition(-150)).Within(0.00001));
            Assert.That(line.BoundaryIsFast, Is.Null);
            Assert.That(meter.ChildrenOfType<Triangle>().Single().X, Is.EqualTo(0.5f));
            return true;
        });
        AddStep("hide empty inputs", () => meter.ShowEmptyPoor.Value = false);
        AddAssert("fast miss keeps its position after boundary update", () =>
            meter.Judgements.Lines.Single().X == BmsHitErrorMeter.CreateDomain(BmsHitErrorMeter.CreateWindows(score.EngineChart)).RelativePosition(-150));
        AddStep("clear and play passive miss", () =>
        {
            meter.Clear();
            SeekReplayInFrames(2200);
        });
        AddUntilStep("passive miss reaches meter", () => meter.Judgements.Lines.Count == 1);
        AddAssert("passive miss stays on the late boundary", () =>
        {
            var windows = BmsHitErrorMeter.CreateWindows(score.EngineChart);
            var domain = BmsHitErrorMeter.CreateDomain(windows);
            var (_, slow) = BmsHitErrorMeter.GetUntimedDisplayOffsets(windows, false);
            Assert.That(meter.Judgements.Lines.Single().X, Is.EqualTo(domain.RelativePosition(slow)));
            Assert.That(meter.Judgements.Lines.Single().BoundaryIsFast, Is.False);
            return true;
        });
        AddStep("rewind and hide misses", () =>
        {
            Player.GameplayClockContainer.Seek(700);
            meter.ShowPoor.Value = false;
        });
        AddUntilStep("rewind clears timing lines", () => meter.Judgements.Lines.Count == 0 && score.JudgementEvents.Count == 0);
        AddStep("replay hidden fast miss", () => SeekReplayInFrames(850));
        AddUntilStep("fast miss judged again", () => score.JudgementEvents.Count == 1);
        AddAssert("miss visibility setting is respected", () => meter.Judgements.Lines.Count == 0);
    }

    private void load(bool lazer)
    {
        AddStep("load Mania tap replay", () =>
        {
            LoadPlayer([new BmsModManiaProfile { OverallDifficulty = { Value = 8 }, UseLazer = { Value = lazer } }]);
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1 && meter.IsLoaded);
        AddStep("stop before first input", () => Player.GameplayClockContainer.Seek(700));
        AddStep("freeze playback", () => Player.GameplayClockContainer.Stop());
    }
}
