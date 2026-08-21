#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Replays;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsAutoGaugeScoreAttribution : BmsPlayerTestScene
{
    private const double note_time = 1000;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(createBadReplayFrames);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects =
            {
                new BmsHitObject { StartTime = note_time, Column = 1 },
            },
        };

        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    private static IList<ReplayFrame> createBadReplayFrames(BmsBeatmap beatmap)
    {
        var action = BmsKeyBindingConfiguration.ActionForColumn(beatmap.LayoutVariant, 1)!.Value;
        const double hit_time = note_time + 150;

        return
        [
            new BmsReplayFrame(0),
            new BmsReplayFrame(hit_time, action),
            new BmsReplayFrame(hit_time + BmsTestReplays.RELEASE_PADDING_MS),
        ];
    }

    [Test]
    public void TestAutoGaugeFinalScoreAttributionUsesResolvedGauge()
    {
        this.AddSetupStep("load player with AG mod", () => LoadPlayer([new BmsModAutoGauge()]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);

        AddUntilStep("score completed", () => Player.ScoreProcessor.HasCompleted.Value);

        AddAssert("active gauge resolved to ExHard", () =>
            Player.GameplayState.HealthProcessor is BmsHealthProcessor hp
            && hp.WorstGaugeType == BmsGaugeType.ExHard);

        AddAssert("score mods contain resolved gauge only", () =>
            Player.Score.ScoreInfo.Mods.Any(m => m is BmsModAutoGauge)
            && Player.Score.ScoreInfo.Mods.Any(m => m is BmsModExHardGauge)
            && Player.Score.ScoreInfo.Mods.All(m => m is not BmsModHazardGauge));
    }
}
