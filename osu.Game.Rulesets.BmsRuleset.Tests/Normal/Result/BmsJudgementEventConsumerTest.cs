using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Result;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Result;

[TestFixture]
public class BmsJudgementEventConsumerTest
{
    [Test]
    public void TestTimelineUsesCanonicalResultAndBothTimingObservations()
    {
        var score = createStandardLongNoteScore(HitResult.Great);
        var data = BmsTimelineStatistic.CreateData(score, createBeatmap());

        Assert.Multiple(() =>
        {
            Assert.That(data.Judgements.Categories.Sum(category => category.Buckets.Sum()), Is.EqualTo(1));
            Assert.That(data.FastSlow.Categories.Sum(category => category.Buckets.Sum()), Is.EqualTo(2));
        });
    }

    [Test]
    public void TestGaugeFallbackAppliesCanonicalResultOnce()
    {
        var canonicalScore = createStandardLongNoteScore(HitResult.Ok);
        var singleEventScore = new ScoreInfo
        {
            HitEvents = [new HitEvent(19, 1, HitResult.Ok, new BmsLongNote { StartTime = 1000, Duration = 500 }, null, null)],
        };

        var canonicalSeries = BmsGaugeHistoryGraph.CreateSeries(canonicalScore, createBeatmap()).Single();
        var singleEventSeries = BmsGaugeHistoryGraph.CreateSeries(singleEventScore, createBeatmap()).Single();

        Assert.That(canonicalSeries.Points.Last().Health, Is.EqualTo(singleEventSeries.Points.Last().Health));
    }

    private static ScoreInfo createStandardLongNoteScore(HitResult result)
    {
        var longNote = new BmsLongNote { StartTime = 1000, Duration = 500, Column = 1 };
        BmsJudgementEvent[] events =
        [
            new BmsJudgementEvent(BmsJudgementSource.From(longNote), result,
            [
                new BmsTimingObservation(BmsTimingObservationKind.LongNoteHead, 1000, 988, 1, HitResult.Perfect),
                new BmsTimingObservation(BmsTimingObservationKind.LongNoteTail, 1500, 1519, 1, result),
            ]),
        ];
        var score = new ScoreInfo { Passed = true };
        score.HitEvents = BmsJudgementEventProjection.CreateTimingHitEvents(events);
        BmsJudgementEventStore.Set(score, events);
        return score;
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
}
