using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Course;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Result.Course;

[TestFixture]
public class BmsCourseResultPresentationTest
{

    private static ScoreInfo createScore(BeatmapInfo beatmap, int maxCombo) => new()
    {
        User = new APIUser(),
        BeatmapInfo = beatmap,
        BeatmapHash = beatmap.Hash,
        Ruleset = beatmap.Ruleset,
        Passed = true,
        MaxCombo = maxCombo,
    };

    [Test]
    public void TestAggregateMaxComboUsesHighestStageCombo()
    {
        var ruleset = new BmsRuleset().RulesetInfo;
        BeatmapInfo[] beatmaps =
        [
            new()
                { Hash = "stage-1", Ruleset = ruleset },
            new()
                { Hash = "stage-2", Ruleset = ruleset },
        ];
        BmsCourseStage[] definitions =
        [
            new("Stage 1", "Normal", BeatmapHash: beatmaps[0].Hash),
            new("Stage 2", "Normal", BeatmapHash: beatmaps[1].Hash),
        ];
        var session = new BmsCourseSession(
            new BmsCourseDefinition("course", "Table", "Course", definitions, []),
            [
                new BmsResolvedCourseStage(definitions[0], beatmaps[0]),
                new BmsResolvedCourseStage(definitions[1], beatmaps[1]),
            ],
            [],
            BmsGaugeType.Class);

        session.BeginCurrentStage();
        session.CompleteCurrentStage(createScore(beatmaps[0], 120), [new BmsGaugeStateSnapshot(BmsGaugeType.Class, 0.8, false)]);
        session.RequestAdvance();
        session.Advance();
        session.BeginCurrentStage();
        session.CompleteCurrentStage(createScore(beatmaps[1], 360), [new BmsGaugeStateSnapshot(BmsGaugeType.Class, 0.7, false)]);

        var aggregate = BmsCourseResultPresentation.CreateAggregateScore(session);

        Assert.That(aggregate.MaxCombo, Is.EqualTo(360));
    }
}
