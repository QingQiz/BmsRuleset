using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Result.Statistic;

[TestFixture]
public class BmsJudgementEventConsumerTest
{

    private static (ScoreInfo Score, BmsJudgementEvent[] Events) createStandardLongNoteScore(HitResult result)
    {
        var longNote = new BmsLongNote { StartTime = 1000, Duration = 500, Column = 1 };
        BmsJudgementEvent[] events =
        [
            new(BmsJudgementSource.From(longNote), result,
            [
                new BmsTimingObservation(BmsTimingObservationKind.LongNoteHead, 1000, 988, 1, HitResult.Perfect),
                new BmsTimingObservation(BmsTimingObservationKind.LongNoteTail, 1500, 1519, 1, result),
            ]),
        ];
        var score = new ScoreInfo
        {
            Passed = true, HitEvents = BmsJudgementEventProjection.CreateTimingHitEvents(events)
        };
        return (score, events);
    }

    private static BmsBeatmap createBeatmap() => new()
    {
        LayoutVariant = BmsLayoutVariant.Bme7K,
        TotalColumns = 8,
        Total = 200,
        HitObjects =
        {
            new BmsLongNote { StartTime = 1000, Duration = 500, Column = 1 },
            new BmsNote { StartTime = 2000, Column = 2 },
        },
    };

    [Test]
    public void TestGaugeFallbackAppliesCanonicalResultOnce()
    {
        var canonicalScore = createStandardLongNoteScore(HitResult.Ok);
        var singleEventScore = new ScoreInfo
        {
            HitEvents = [new HitEvent(19, 1, HitResult.Ok, new BmsLongNote { StartTime = 1000, Duration = 500 }, null, null)],
        };

        var canonicalSeries = BmsGaugeHistoryGraph.CreateSeries(canonicalScore.Score, createBeatmap(),
            BmsJudgementEventProjection.CreateScoringHitEvents(canonicalScore.Events), []).Single();
        var singleEventSeries = BmsGaugeHistoryGraph.CreateSeries(singleEventScore, createBeatmap()).Single();

        Assert.That(canonicalSeries.Points.Last().Health, Is.EqualTo(singleEventSeries.Points.Last().Health));
    }

    [Test]
    public void TestTimelineUsesCanonicalResultAndBothTimingObservations()
    {
        var score = createStandardLongNoteScore(HitResult.Great);
        var data = BmsTimelineStatistic.CreateData(createBeatmap(), score.Score.HitEvents,
            BmsJudgementEventProjection.CreateScoringHitEvents(score.Events));

        Assert.Multiple(() =>
        {
            Assert.That(data.Judgements.Categories.Sum(category => category.Buckets.Sum()), Is.EqualTo(1));
            Assert.That(data.FastSlow.Categories.Sum(category => category.Buckets.Sum()), Is.EqualTo(2));
        });
    }
}
