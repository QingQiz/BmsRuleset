#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsJudgementAlgorithm : BmsPlayerTestScene
{
    private BmsRulesetConfigManager config => (BmsRulesetConfigManager)RulesetConfigs.GetConfigFor(new BmsRuleset())!;

    private bool useReplay;
    private BmsJudgementAlgorithm? replayAlgorithm;

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(useReplay ? createReplay : null);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Rank = 3,
            HitObjects =
            [
                new BmsNote { StartTime = 3000, Column = 1 },
                new BmsNote { StartTime = 3100, Column = 1 },
                new BmsNote { StartTime = 6000, Column = 1 },
                new BmsNote { StartTime = 6450, Column = 1 },
                new BmsNote { StartTime = 10000, Column = 1 },
            ],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    private IList<ReplayFrame> createReplay(BmsBeatmap beatmap) =>
    [
        new BmsReplayFrame(0) { JudgementAlgorithm = replayAlgorithm },
        new BmsReplayFrame(3100, BmsAction.Key1),
        new BmsReplayFrame(3101),
        new BmsReplayFrame(6250, BmsAction.Key1),
        new BmsReplayFrame(6251),
        new BmsReplayFrame(11000),
    ];

    [Test]
    public void TestConfigIsCapturedForEachPlay([Values] BmsJudgementAlgorithm algorithm)
    {
        AddStep("configure algorithm", () =>
        {
            useReplay = false;
            config.SetValue(BmsRulesetSetting.JudgementAlgorithm, algorithm);
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("configured algorithm applied", () => Playfield.JudgementAlgorithm == algorithm);
        AddStep("change setting mid-play", () => config.SetValue(BmsRulesetSetting.JudgementAlgorithm, differentAlgorithm(algorithm)));
        AddAssert("current play keeps its algorithm", () => Playfield.JudgementAlgorithm == algorithm);
        AddStep("load next play", () => LoadPlayer());
        AddUntilStep("next player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("next play uses new setting", () => Playfield.JudgementAlgorithm == differentAlgorithm(algorithm));
        AddStep("restore default", () => config.SetValue(BmsRulesetSetting.JudgementAlgorithm, BmsJudgementAlgorithm.Combo));
    }

    [TestCase(BmsJudgementAlgorithm.Combo, 3000, HitResult.Good, 6000)]
    [TestCase(BmsJudgementAlgorithm.Duration, 3100, HitResult.Perfect, 6450)]
    [TestCase(BmsJudgementAlgorithm.Lowest, 3000, HitResult.Good, 6000)]
    [TestCase(BmsJudgementAlgorithm.Score, 3100, HitResult.Perfect, 6000)]
    [TestCase(null, 3000, HitResult.Good, 6450)]
    public void TestReplayUsesRecordedSelection(BmsJudgementAlgorithm? algorithm, double goodTarget, HitResult goodResult, double badTarget)
    {
        AddStep("load replay with different local setting", () =>
        {
            useReplay = true;
            replayAlgorithm = algorithm;
            config.SetValue(BmsRulesetSetting.JudgementAlgorithm, differentAlgorithm(algorithm));
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("replay algorithm applied", () => Playfield.JudgementAlgorithm == algorithm);
        AddUntilStep("first press judged", () => Player.GameplayClockContainer.CurrentTime > 3150);
        AddAssert("first press selects expected note", () => ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents
            .Any(e => e.Source.StartTime == goodTarget && e.Result == goodResult));
        AddStep("seek before BAD overlap", () => Player.GameplayClockContainer.Seek(6100));
        AddUntilStep("second press judged", () => Player.GameplayClockContainer.CurrentTime > 6300);
        AddAssert("second press selects expected note", () => ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents
            .Any(e => e.Source.StartTime == badTarget && e.Result == HitResult.Ok));
        AddStep("restore default", () => config.SetValue(BmsRulesetSetting.JudgementAlgorithm, BmsJudgementAlgorithm.Combo));
    }

    private static BmsJudgementAlgorithm differentAlgorithm(BmsJudgementAlgorithm? algorithm)
        => algorithm == BmsJudgementAlgorithm.Duration ? BmsJudgementAlgorithm.Lowest : BmsJudgementAlgorithm.Duration;
}
