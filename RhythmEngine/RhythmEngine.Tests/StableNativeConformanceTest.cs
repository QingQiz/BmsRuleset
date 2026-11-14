using System.Text.Json;
using NUnit.Framework;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Mania;

namespace RhythmEngine.Tests;

[TestFixture]
public class StableNativeConformanceTest
{
    public static IEnumerable<TestCaseData> HoldResults()
    {
        foreach (var row in read())
        {
            // The manager blocks input at end+OK before this result method runs.
            if (row.GetProperty("tailOffset").GetInt32() >= 103)
                continue;

            yield return new TestCaseData(row).SetName($"StableCil_Hold_{row.GetProperty("headOffset")}_{row.GetProperty("tailOffset")}_Broken{row.GetProperty("broken")}");
        }
    }

    [TestCaseSource(nameof(HoldResults))]
    public void ReleasedHoldMatchesOriginalCil(JsonElement row)
    {
        var profile = ManiaProfiles.CreateStable(new KeyChart(1, [new KeyNote(0, 0, ms(1000), ms(2000), KeyNoteKind.Hold)]));
        var session = profile.CreateSession();
        var inputs = new List<InputEvent>();
        if (row.GetProperty("broken").GetBoolean())
        {
            inputs.Add(new InputEvent(ms(837), 0, true));
            inputs.Add(new InputEvent(ms(838), 0, false));
        }

        inputs.Add(new InputEvent(ms(1000 + row.GetProperty("headOffset").GetInt32()), 0, true));
        var release = ms(2000 + row.GetProperty("tailOffset").GetInt32());
        inputs.Add(new InputEvent(release, 0, false));
        session.AdvanceTo(release, inputs.ToArray());
        Assert.That(session.GetEventsSince(0).Last(e => e.Result.Value != "hold-tick").Result.Value, Is.EqualTo(row.GetProperty("result").GetString()));
        Assert.That(session.Completed, Is.EqualTo(row.GetProperty("result").GetString() != "hold-break"));
    }

    [Test]
    public void BodyTicksRecoverHealthAndIncrementComboWithoutAccuracyCredit()
    {
        var source = new KeyChart(1, [new KeyNote(0, 0, ms(500), ms(500)), new KeyNote(1, 0, ms(1000), ms(2000), KeyNoteKind.Hold)]);
        var session = ManiaProfiles.CreateStable(source).CreateSession();
        session.AdvanceTo(ms(1900), [new InputEvent(ms(1000), 0, true)]);
        Assert.That(session.Score.Combo, Is.EqualTo(9));
        Assert.That(session.Score.AccuracyMaximum, Is.EqualTo(300));
        Assert.That(session.Health.Value, Is.EqualTo(0.955 + 9 * 0.00125).Within(1e-12));
        session.AdvanceTo(ms(2100), [new InputEvent(ms(2000), 0, false)]);
        Assert.That(session.Score.Combo, Is.EqualTo(10));
        Assert.That(session.Score.AccuracyMaximum, Is.EqualTo(600));
    }

    [Test]
    public void AShortRecoveredHoldCanReachGoodAfterAnEarlyDrop()
    {
        var source = new KeyChart(1, [new KeyNote(0, 0, ms(1000), ms(1200), KeyNoteKind.Hold)]);
        var session = ManiaProfiles.CreateStable(source).CreateSession();
        session.AdvanceTo(ms(1300), [
            new InputEvent(ms(1000), 0, true), new InputEvent(ms(1050), 0, false),
            new InputEvent(ms(1060), 0, true), new InputEvent(ms(1200), 0, false)
        ]);
        Assert.That(session.GetEventsSince(0).Last().Result.Value, Is.EqualTo("good"));
        Assert.That(session.Score.Accuracy, Is.EqualTo(2.0 / 3));
    }

    [Test]
    public void HeldTailWaitsForMehAndResolvesItsCombinedError()
    {
        var session = ManiaProfiles.CreateStable(new KeyChart(1, [new KeyNote(0, 0, ms(1000), ms(2000), KeyNoteKind.Hold)])).CreateSession();
        session.AdvanceTo(ms(1000), [new InputEvent(ms(1000), 0, true)]);
        foreach (var row in read("held"))
        {
            session.AdvanceTo(ms(2000 + row.GetProperty("tailOffset").GetInt32()));
            var expected = row.GetProperty("result").GetString();
            Assert.That(session.GetEventsSince(0).SingleOrDefault(e => e.Part.Value == "tail")?.Result.Value, Is.EqualTo(expected));
            Assert.That(session.Completed, Is.EqualTo(expected != null));
        }
    }

    [Test]
    public void ManagerExpiresTapAtTheExactLateOkBoundary()
    {
        var source = new KeyChart(1, [new KeyNote(0, 0, ms(1000), ms(1000))]);
        var atBoundary = ManiaProfiles.CreateStable(source).CreateSession();
        atBoundary.AdvanceTo(ms(1103), [new InputEvent(ms(1103), 0, true)]);
        Assert.That(atBoundary.GetEventsSince(0).Single().Result.Value, Is.EqualTo("miss"));
        var before = ManiaProfiles.CreateStable(source).CreateSession();
        before.AdvanceTo(ms(1102), [new InputEvent(ms(1102), 0, true)]);
        Assert.That(before.GetEventsSince(0).Single().Result.Value, Is.EqualTo("ok"));
    }

    [Test]
    public void LiveScoreTruncatesHitIncrementsAndFinalScoreRoundsTheRawTotal()
    {
        var source = new KeyChart(1, Enumerable.Range(0, 4).Select(i => new KeyNote(i, 0, ms((i + 1) * 1000), ms((i + 1) * 1000))));
        var session = ManiaProfiles.CreateStable(source).CreateSession();
        session.AdvanceTo(ms(1100), [new InputEvent(ms(1030), 0, true), new InputEvent(ms(1031), 0, false)]);
        Assert.That(session.Score.Score, Is.EqualTo(242187));
        Assert.That(session.Score.Counters["score-v1-unrounded"], Is.EqualTo(242187.5));
        session.AdvanceTo(ms(4500), [
            new InputEvent(ms(2030), 0, true), new InputEvent(ms(2031), 0, false),
            new InputEvent(ms(3030), 0, true), new InputEvent(ms(3031), 0, false), new InputEvent(ms(4030), 0, true)
        ]);
        Assert.That(session.Score.Score, Is.EqualTo(968750));
    }

    [Test]
    public void BodyComboTriggersTheOriginal384ComboBonusReset()
    {
        var source = new KeyChart(1, [new KeyNote(0, 0, ms(1000), ms(1000)), new KeyNote(1, 0, ms(2000), ms(42000), KeyNoteKind.Hold)]);
        var session = ManiaProfiles.CreateStable(source).CreateSession();
        session.AdvanceTo(ms(40300), [new InputEvent(ms(1060), 0, true), new InputEvent(ms(1061), 0, false), new InputEvent(ms(2000), 0, true)]);
        Assert.That(session.Score.Combo, Is.EqualTo(384));
        Assert.That(session.Score.Bonus, Is.EqualTo(92));
        session.AdvanceTo(ms(40400));
        Assert.That(session.Score.Combo, Is.EqualTo(385));
        Assert.That(session.Score.Bonus, Is.EqualTo(100));
    }

    [Test]
    public void ScoreBonusAndComboMatchOriginalCilAcrossMixedResults()
    {
        foreach (var row in read("scores"))
        {
            var count = row.GetProperty("count").GetInt32();
            var source = new KeyChart(4, Enumerable.Range(0, count).Select(i =>
                new KeyNote(i, i % 4, ms(1000 + i / 4 * 100), ms(1000 + i / 4 * 100))));
            var profile = ManiaProfiles.CreateStable(source, new ManiaOptions(DrainRate: row.GetProperty("drain").GetInt32()));
            var configuration = profile.Configuration.Score;
            var state = configuration.InitialState;
            var index = 0;
            foreach (var expected in row.GetProperty("samples").EnumerateArray())
            {
                var result = expected.GetProperty("code").GetInt32() switch
                {
                    268435456 => "perfect", 134217728 => "great", 67108864 => "good", 33554432 => "ok",
                    16777216 => "meh", -65536 => "miss", 7 => "hold-tick", -1048576 => "hold-break", _ => throw new InvalidOperationException(),
                };
                var part = result is "hold-tick" or "hold-break" ? "body" : "tap";
                var opportunity = part == "body" ? null : profile.Chart.ScorePlan.Opportunities[index];
                var time = ms(index * 1000);
                var judgement = new JudgementEvent(index, index, index % 4, new PartId(part), new ResultId(result), time, time, 0);
                state = configuration.Reduce(state, new ScoreContext(judgement, opportunity, profile.Chart.ScorePlan));
                using var scope = Assert.EnterMultipleScope();
                Assert.That(state.Score, Is.EqualTo(expected.GetProperty("displayScore").GetInt32()), $"score {index}");
                Assert.That(state.Counters.GetValueOrDefault("score-v1-unrounded"), Is.EqualTo(expected.GetProperty("rawScore").GetDouble()).Within(1e-7), $"raw score {index}");
                Assert.That(state.Bonus, Is.EqualTo(expected.GetProperty("bonus").GetDouble()), $"bonus {index}");
                Assert.That(state.Combo, Is.EqualTo(expected.GetProperty("combo").GetInt32()), $"combo {index}");
                Assert.That(state.MaximumCombo, Is.EqualTo(expected.GetProperty("maxCombo").GetInt32()), $"max combo {index}");
                index++;
            }
        }
    }

    [Test]
    public void HealthMatchesOriginalCilOnAChartWithUnitRecovery()
    {
        var row = read("scores").Single(r => r.GetProperty("drain").GetInt32() == 10);
        var count = row.GetProperty("count").GetInt32();
        var source = new KeyChart(4, Enumerable.Range(0, count).Select(i =>
            new KeyNote(i, i % 4, ms(1000 + i / 4), ms(1000 + i / 4))));
        Assert.That(ManiaHealthCalibration.ComputeRecoveryMultiplier(source, 10, ms(1000), false), Is.EqualTo(1));
        var profile = ManiaProfiles.CreateStable(source, new ManiaOptions(DrainRate: 10, DrainStartTime: ms(1000)));
        var configuration = profile.Configuration;
        var health = configuration.Health.InitialState;
        var index = 0;
        foreach (var expected in row.GetProperty("samples").EnumerateArray())
        {
            var result = expected.GetProperty("code").GetInt32() switch
            {
                268435456 => "perfect", 134217728 => "great", 67108864 => "good", 33554432 => "ok",
                16777216 => "meh", -65536 => "miss", 7 => "hold-tick", -1048576 => "hold-break", _ => throw new InvalidOperationException(),
            };
            var judgement = new JudgementEvent(index, index, index % 4, new PartId("tap"), new ResultId(result), ms(index), ms(index), 0);
            health = configuration.Health.Reduce(new HealthContext(judgement, health, configuration.Score.InitialState, profile.Chart.ScorePlan));
            Assert.That(health.Value, Is.EqualTo(expected.GetProperty("hp").GetDouble() / 200).Within(1e-12), $"health {index}");
            index++;
        }
    }

    private static IEnumerable<JsonElement> read(string collection = "holds")
    {
        using var stream = typeof(StableNativeConformanceTest).Assembly.GetManifestResourceStream("RhythmEngine.Tests.Reference.stable-holds.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty(collection).EnumerateArray().Select(row => row.Clone()).ToArray();
    }

    private static long ms(int value) => value * 1000L;
}
