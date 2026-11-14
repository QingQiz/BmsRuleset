using System.Text.Json;
using NUnit.Framework;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using RhythmEngine.Profiles.Common;

namespace RhythmEngine.Tests;

[TestFixture]
public class Lr2NativeConformanceTest
{
    public static IEnumerable<TestCaseData> Heads()
    {
        foreach (var row in read("heads"))
            yield return new TestCaseData(row).SetName($"LR2_100201_Head_{row.GetProperty("rank")}_{row.GetProperty("offset")}");
    }

    public static IEnumerable<TestCaseData> Holds()
    {
        foreach (var row in read("holds"))
            yield return new TestCaseData(row).SetName($"LR2_100201_Hold_{row.GetProperty("rank")}_{row.GetProperty("offset")}_{row.GetProperty("release")}");
    }

    [TestCaseSource(nameof(Heads))]
    public void HeadMatchesOriginalMillisecondInput(JsonElement row)
    {
        var profile = Lr2Beta3Profile.Create(new KeyChart(1, [new KeyNote(0, 0, ms(2000), ms(2000))]),
            new Lr2Beta3Options(Rank: (BmsRank)row.GetProperty("rank").GetInt32()));
        var session = profile.CreateSession();
        var time = ms(row.GetProperty("time").GetInt32());
        session.AdvanceTo(time, [new InputEvent(time, 0, true)]);
        Assert.That(session.GetEventsSince(0).Select(e => e.Result.Value), Is.EqualTo(row.GetProperty("events").EnumerateArray().Select(grade)));
        Assert.That(session.Completed, Is.EqualTo(row.GetProperty("index").GetInt32() > 0));
    }

    [TestCaseSource(nameof(Holds))]
    public void LongNoteKeepsHeadGradeWithinOriginalReleaseLenience(JsonElement row)
    {
        var profile = Lr2Beta3Profile.Create(new KeyChart(1, [new KeyNote(0, 0, ms(2000), ms(3000), KeyNoteKind.Hold)]),
            new Lr2Beta3Options(Rank: (BmsRank)row.GetProperty("rank").GetInt32()));
        var session = profile.CreateSession();
        var headTime = ms(2000 + row.GetProperty("offset").GetInt32());
        session.AdvanceTo(headTime, [new InputEvent(headTime, 0, true)]);
        Assert.That(session.GetEventsSince(0).Select(e => e.Result.Value), Is.EqualTo(row.GetProperty("head").GetProperty("events").EnumerateArray().Select(grade)));
        var release = row.GetProperty("release");
        var time = ms(row.GetProperty("tail").GetProperty("time").GetInt32());
        session.AdvanceTo(time, release.ValueKind == JsonValueKind.Null ? [] : [new InputEvent(time, 0, false)]);
        Assert.That(session.GetEventsSince(0).Select(e => e.Result.Value), Is.EqualTo(row.GetProperty("tail").GetProperty("events").EnumerateArray().Select(grade)));
        Assert.That(session.Completed, Is.True);
    }

    [Test]
    public void LateBadChainsThroughTheNextCellOnTheSamePress()
    {
        foreach (var row in read("arbitration"))
        {
            var second = 2000 + row.GetProperty("secondDelta").GetInt32();
            var source = new KeyChart(1, [new KeyNote(0, 0, ms(2000), ms(2000)), new KeyNote(1, 0, ms(second), ms(second))]);
            var session = Lr2Beta3Profile.Create(source).CreateSession();
            var time = ms(row.GetProperty("time").GetInt32());
            session.AdvanceTo(time, [new InputEvent(time, 0, true)]);
            Assert.That(session.GetEventsSince(0).Select(e => e.Result.Value), Is.EqualTo(row.GetProperty("events").EnumerateArray().Select(grade)), row.ToString());
        }
    }

    [Test]
    public void GaugeAndScoreSequencesMatchOriginalBootstrapAndReducer()
    {
        foreach (var row in read("gauges"))
        {
            var count = row.GetProperty("noteCount").GetInt32();
            var source = new KeyChart(1, Enumerable.Range(0, count).Select(i => new KeyNote(i, 0, ms((i + 1) * 2000), ms((i + 1) * 2000))));
            var profile = Lr2Beta3Profile.Create(source, new Lr2Beta3Options(Gauge: (Lr2Beta3Gauge)row.GetProperty("gauge").GetInt32(), Total: row.GetProperty("total").GetInt32()));
            var configuration = profile.Configuration;
            var score = configuration.Score.InitialState;
            var health = configuration.Health.InitialState;
            var index = 0;
            foreach (var expected in row.GetProperty("states").EnumerateArray())
            {
                var result = grade(expected);
                var opportunity = result == "empty-poor" ? null : profile.Chart.ScorePlan.Opportunities[index];
                var time = ms((index + 1) * 2000);
                var judgement = new JudgementEvent(index, index, 0, new PartId(opportunity == null ? "empty" : "note"), new ResultId(result), time, time, 0);
                score = configuration.Score.Reduce(score, new ScoreContext(judgement, opportunity, profile.Chart.ScorePlan));
                health = configuration.Health.Reduce(new HealthContext(judgement, health, score, profile.Chart.ScorePlan));
                using var scope = Assert.EnterMultipleScope();
                Assert.That(health.Value, Is.EqualTo(expected.GetProperty("hp").GetDouble()).Within(1e-11), $"gauge {row.GetProperty("gauge")}, step {index}");
                Assert.That(score.Score, Is.EqualTo(expected.GetProperty("exScore").GetInt32()));
                Assert.That(score.Combo, Is.EqualTo(expected.GetProperty("combo").GetInt32()));
                Assert.That(score.MaximumCombo, Is.EqualTo(expected.GetProperty("maxCombo").GetInt32()));
                index++;
            }
        }
    }

    private static string grade(JsonElement row) => row.GetProperty("grade").GetInt32() switch
    {
        0 => "empty-poor", 1 => "poor", 2 => "bad", 3 => "good", 4 => "great", 5 => "pgreat", _ => throw new InvalidOperationException(),
    };

    private static JsonElement[] read(string collection)
    {
        using var stream = typeof(Lr2NativeConformanceTest).Assembly.GetManifestResourceStream("RhythmEngine.Tests.Reference.lr2-100201.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty(collection).EnumerateArray().Select(row => row.Clone()).ToArray();
    }

    private static long ms(int value) => value * 1000L;
}
