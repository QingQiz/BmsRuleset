using System.Text.Json;
using NUnit.Framework;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Etterna;
using RhythmEngine.Profiles.Mania;
using RhythmEngine.Profiles.Patterns;

namespace RhythmEngine.Tests;

[TestFixture]
public class UpstreamWindowTest
{
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    public void EtternaSearchAtTempoBoundaryKeepsOutOfWindowFutureTarget(int offset)
    {
        var profile = EtternaProfile.Create(new KeyChart(1,
        [
            new KeyNote(0, 0, 1400000, 1400000, KeyNoteKind.Mine),
            new KeyNote(1, 0, 1800000, 1800000),
            new KeyNote(2, 0, 1800000, 1800000),
        ], [new TempoPoint(long.MinValue, 120), new TempoPoint(-1000000, 90), new TempoPoint(1500000, 240)]));
        var session = profile.CreateSession();
        var blocked = session.AdvanceTo(1500000 + offset, [new InputEvent(1500000 + offset, 0, true)]);
        Assert.That(blocked.Transitions.Last().After.Id, Is.EqualTo(1));
        Assert.That(blocked.Transitions.SelectMany(t => t.Judgements), Is.Empty);
        var hit = session.AdvanceTo(1800000, [new InputEvent(1799999, 0, false), new InputEvent(1800000, 0, true)]);
        Assert.That(hit.Transitions.SelectMany(t => t.Judgements).Last().Event.NoteId, Is.EqualTo(1));
        Assert.That(hit.Transitions.SelectMany(t => t.Judgements).Last().Event.Result, Is.EqualTo(EtternaResults.W1));
    }

    public static IEnumerable<TestCaseData> BeatorajaTables()
    {
        foreach (var row in read("beatoraja-windows.json"))
            yield return new TestCaseData(row).SetName($"BeatorajaSource_{row.GetProperty("rank")}_{row.GetProperty("type")}");
    }

    public static IEnumerable<TestCaseData> ManiaTables()
    {
        foreach (var row in read("mania-windows.json"))
        {
            yield return new TestCaseData(row).SetName(
                $"ManiaSource_{row.GetProperty("classic")}_{row.GetProperty("convert")}_OD{row.GetProperty("od")}_Rate{row.GetProperty("rate")}_Difficulty{row.GetProperty("difficulty")}");
        }
    }

    public static IEnumerable<TestCaseData> GaugeTables()
    {
        foreach (var row in read("beatoraja-gauges.json"))
            yield return new TestCaseData(row).SetName($"GaugeSource_LR2{row.GetProperty("lr2")}_{row.GetProperty("gauge")}_TOTAL{row.GetProperty("total")}_Notes{row.GetProperty("count")}");
    }

    public static IEnumerable<TestCaseData> WifeSamples()
    {
        foreach (var row in read("etterna-wife3.json"))
            yield return new TestCaseData(row.GetProperty("offset").GetDouble(), row.GetProperty("scale").GetDouble(), row.GetProperty("points").GetDouble());
    }

    public static IEnumerable<TestCaseData> GaugeFlows()
    {
        foreach (var row in read("beatoraja-gauge-flow.json"))
            yield return new TestCaseData(row).SetName($"BeatorajaFloatGauge_{row.GetProperty("gauge")}_TOTAL{row.GetProperty("total")}");
    }

    [TestCaseSource(nameof(GaugeFlows))]
    public void GaugeAccumulationMatchesExecutedUpstreamFloatState(JsonElement row)
    {
        var count = row.GetProperty("count").GetInt32();
        var notes = Enumerable.Range(1, count - 2).Select(i => new KeyNote(i, 0, i * 1000000L, i * 1000000L));
        var source = new KeyChart(1, notes.Prepend(new KeyNote(0, 0, 0, 200000000, KeyNoteKind.SustainHold)));
        var profile = BmsProfiles.CreateBeatoraja(source, new BmsOptions(Gauge: (BmsGauge)row.GetProperty("gauge").GetInt32(), Total: row.GetProperty("total").GetDouble()));
        var configuration = profile.Configuration.Health;
        var health = configuration.InitialState;
        string[] grades = ["pgreat", "great", "good", "bad", "poor", "empty-poor"];
        var step = 0;

        foreach (var expected in row.GetProperty("states").EnumerateArray())
        {
            var judge = expected.GetProperty("judge").GetInt32();
            var body = expected.GetProperty("rate").GetDouble() == 0.5;
            var part = body ? "body" : judge == 5 ? "empty" : "tap";
            var result = body ? judge == 1 ? "sustain-gain" : "sustain-loss" : grades[judge];
            var time = ++step * 1000000L;
            var judgement = new JudgementEvent(step, body ? 0 : step, 0, new PartId(part), new ResultId(result), time, time, 0);
            health = configuration.Reduce(new HealthContext(judgement, health, profile.Configuration.Score.InitialState, profile.Chart.ScorePlan));
            Assert.That(health.Value, Is.EqualTo(expected.GetProperty("value").GetDouble()), $"step {step}");
            var outcome = configuration.EvaluateOutcome(new SessionOutcomeContext(true, health, profile.Configuration.Score.InitialState, profile.Chart.ScorePlan));
            Assert.That(outcome == SessionOutcome.Cleared, Is.EqualTo(expected.GetProperty("qualified").GetBoolean()), $"qualification {step}");
        }
    }

    [TestCaseSource(nameof(WifeSamples))]
    public void Wife3MatchesExecutedCpp(double offset, double scale, double expected)
    {
        Assert.That(EtternaProfile.Wife3(offset, scale), Is.EqualTo(expected).Within(0.00001));
        Assert.That(EtternaProfile.Wife3(-offset, scale), Is.EqualTo(expected).Within(0.00001));
    }

    [Test]
    public void EtternaSearchUsesTempoRowsAndOriginalPadding()
    {
        var chart = new KeyChart(1, [new KeyNote(0, 0, 1000000, 1000000), new KeyNote(1, 0, 1800000, 1800000)],
            [new TempoPoint(long.MinValue, 120), new TempoPoint(Time.FromMilliseconds(1500), 240)]);
        var profile = EtternaProfile.Create(chart);
        var note = profile.Chart.Notes[1];
        var context = new NoteContext(note, note.Rule.CreateState(note), Time.FromMilliseconds(1500), new LaneInput(true, false));
        var input = new InputEvent(context.Time, 0, true);
        Assert.That(note.Rule.GetCandidate(context, input, 1), Is.Not.Null);
        Assert.That(note.Rule.ApplyInput(context, input).Emissions, Is.Empty);
    }

    [Test]
    public void EtternaLifeMatchesOriginalFloatAccumulationAndFailureLatch()
    {
        using var stream = typeof(UpstreamWindowTest).Assembly.GetManifestResourceStream("RhythmEngine.Tests.Reference.etterna-life.json")!;
        using var document = JsonDocument.Parse(stream);

        foreach (var group in document.RootElement.GetProperty("states").EnumerateArray().GroupBy(r => r.GetProperty("difficulty").GetDouble()))
        {
            var profile = EtternaProfile.Create(new KeyChart(1, [new KeyNote(0, 0, 1000000, 1000000)]), new EtternaOptions(LifeDifficulty: group.Key));
            var configuration = profile.Configuration.Health;
            var health = configuration.InitialState;
            string[] results = ["w1", "w2", "w3", "w4", "w5", "miss", "mine-hit", "held", "let-go", "missed-hold"];

            foreach (var row in group)
            {
                var index = row.GetProperty("index").GetInt32();
                var judgement = new JudgementEvent(index, 0, 0, new PartId("note"), new ResultId(results[row.GetProperty("result").GetInt32()]), index, index, 0);
                health = configuration.Reduce(new HealthContext(judgement, health, profile.Configuration.Score.InitialState, profile.Chart.ScorePlan));
                Assert.That(health.Value, Is.EqualTo(row.GetProperty("hp").GetDouble()), $"state {index}");
                Assert.That(health.HasFailed, Is.EqualTo(row.GetProperty("failed").GetBoolean()), $"failure {index}");
            }
        }
    }

    [TestCaseSource(nameof(GaugeTables))]
    public void GaugeTransitionsMatchExecutedUpstreamModifiers(JsonElement row)
    {
        var count = row.GetProperty("count").GetInt32();
        var source = new KeyChart(1, Enumerable.Range(0, count).Select(i => new KeyNote(i, 0, i * 1000000L, i * 1000000L)));
        var options = new BmsOptions(Gauge: (BmsGauge)row.GetProperty("gauge").GetInt32(), Total: row.GetProperty("total").GetDouble());
        var profile = row.GetProperty("lr2").GetBoolean() ? BmsProfiles.CreateLr2(source, options) : BmsProfiles.CreateBeatoraja(source, options);
        var gauge = profile.Configuration.Health;
        var minimum = row.GetProperty("minimum").GetDouble();
        Assert.That(gauge.InitialState.Value, Is.EqualTo(row.GetProperty("initial").GetDouble()));
        string[] grades = ["pgreat", "great", "good", "bad", "poor", "empty-poor"];

        for (var i = 0; i < grades.Length; i++)
        {
            var judgement = new JudgementEvent(0, 0, 0, new PartId(i == 5 ? "empty" : "tap"), new ResultId(grades[i]), 0, 0, null);
            var health = gauge.Reduce(new HealthContext(judgement, new HealthState(60, false), profile.Configuration.Score.InitialState, profile.Chart.ScorePlan));
            Assert.That(health.Value, Is.EqualTo(Math.Clamp(60 + row.GetProperty("changes")[i].GetDouble(), minimum, 100)).Within(0.0002), grades[i]);
        }

        var poor = new JudgementEvent(0, 0, 0, new PartId("tap"), new ResultId("poor"), 0, 0, null);
        Assert.That(gauge.Reduce(new HealthContext(poor, new HealthState(minimum, false), profile.Configuration.Score.InitialState, profile.Chart.ScorePlan)).Value,
            Is.EqualTo(minimum));
    }

    [TestCase(BmsGauge.Hard, 9.999, 0.4)]
    [TestCase(BmsGauge.Hard, 10, 0.5)]
    [TestCase(BmsGauge.Hard, 19.999, 0.5)]
    [TestCase(BmsGauge.Hard, 20, 0.6)]
    [TestCase(BmsGauge.Hard, 29.999, 0.6)]
    [TestCase(BmsGauge.Hard, 30, 0.7)]
    [TestCase(BmsGauge.Hard, 39.999, 0.7)]
    [TestCase(BmsGauge.Hard, 40, 0.8)]
    [TestCase(BmsGauge.Hard, 49.999, 0.8)]
    [TestCase(BmsGauge.Hard, 50, 1)]
    [TestCase(BmsGauge.Grade, 4.999, 0.4)]
    [TestCase(BmsGauge.Grade, 5, 0.5)]
    [TestCase(BmsGauge.Grade, 9.999, 0.5)]
    [TestCase(BmsGauge.Grade, 10, 0.6)]
    [TestCase(BmsGauge.Grade, 14.999, 0.6)]
    [TestCase(BmsGauge.Grade, 15, 0.7)]
    [TestCase(BmsGauge.Grade, 19.999, 0.7)]
    [TestCase(BmsGauge.Grade, 20, 0.8)]
    [TestCase(BmsGauge.Grade, 24.999, 0.8)]
    [TestCase(BmsGauge.Grade, 25, 1)]
    public void BeatorajaGutsUsesStrictGaugeThresholds(BmsGauge gaugeType, double healthValue, double multiplier)
    {
        var source = new KeyChart(1, [new KeyNote(0, 0, 1_000_000, 1_000_000)]);
        var profile = BmsProfiles.CreateBeatoraja(source, new BmsOptions(Gauge: gaugeType));
        var judgement = new JudgementEvent(0, 0, 0, new PartId("tap"), new ResultId("bad"), 0, 0, null);
        var health = profile.Configuration.Health.Reduce(new HealthContext(judgement, new HealthState((float)healthValue, false),
            profile.Configuration.Score.InitialState, profile.Chart.ScorePlan));
        var baseDamage = gaugeType == BmsGauge.Hard ? -5 : -1.5;

        Assert.That(health.Value, Is.EqualTo(healthValue + baseDamage * multiplier).Within(0.000005));
    }

    [TestCase(false, BmsGauge.GradeHard, -6)]
    [TestCase(false, BmsGauge.GradeExHard, -10)]
    [TestCase(true, BmsGauge.GradeHard, -10)]
    [TestCase(true, BmsGauge.GradeExHard, -20)]
    public void CourseHardGaugesUseTheirOwnDamageAndGutsPolicy(bool lr2, BmsGauge gauge, double poorDamage)
    {
        var source = new KeyChart(1, [new KeyNote(0, 0, 1_000_000, 1_000_000)]);
        var options = new BmsOptions(Gauge: gauge, Total: 130);
        var profile = lr2 ? BmsProfiles.CreateLr2(source, options) : BmsProfiles.CreateBeatoraja(source, options);
        var judgement = new JudgementEvent(0, 0, 0, new PartId("tap"), new ResultId("poor"), 0, 0, null);
        var health = profile.Configuration.Health.Reduce(new HealthContext(judgement, new HealthState(10, false),
            profile.Configuration.Score.InitialState, profile.Chart.ScorePlan));
        Assert.That(health.Value, Is.EqualTo(Math.Max(0, 10 + poorDamage * (lr2 && gauge == BmsGauge.GradeHard ? 0.6 : 1))).Within(0.000001));
    }

    [TestCaseSource(nameof(BeatorajaTables))]
    public void BeatorajaMatchesCompiledUpstreamWindows(JsonElement row)
    {
        var rank = (BmsRank)(row.GetProperty("rank").GetInt32() / 25 - 1);
        var type = row.GetProperty("type").GetString()!;
        var tail = type.Contains("END");
        var note = new KeyNote(0, 0, 1000000, tail ? 2000000 : 1000000,
            tail ? KeyNoteKind.ReleaseHold : KeyNoteKind.Tap, type.Contains("SCRATCH"));
        var profile = BmsProfiles.CreateBeatoraja(new KeyChart(1, [note]), new BmsOptions(Rank: rank));
        var windows = tail ? ((HoldRule)profile.Chart.Notes[0].Rule).Configuration.TailWindows : ((TapRule)profile.Chart.Notes[0].Rule).Configuration.Windows;
        var expected = row.GetProperty("windows").EnumerateArray().Select(w => (w[0].GetInt64(), w[1].GetInt64())).ToArray();
        Assert.That(windows.Entries.Select(w => (w.Early, w.Late)), Is.EqualTo(expected));

        foreach (var boundary in expected.SelectMany(w => new[] { w.Item1 - 1, w.Item1, w.Item1 + 1, w.Item2 - 1, w.Item2, w.Item2 + 1 }))
        {
            var expectedIndex = Array.FindIndex(expected, w => boundary >= w.Item1 && boundary <= w.Item2);
            if (!tail && boundary == -500_000)
                expectedIndex = -1;
            Assert.That(windows.ResultFor(boundary)?.Value, Is.EqualTo(expectedIndex < 0 ? null : windows.Entries[expectedIndex].Result.Value), $"offset {boundary}");
        }
    }

    [TestCaseSource(nameof(ManiaTables))]
    public void ManiaMatchesCompiledUpstreamWindows(JsonElement row)
    {
        var source = new KeyChart(1, [new KeyNote(0, 0, 1000000, 1000000)]);
        var options = new ManiaOptions(row.GetProperty("od").GetDouble(), ClockRate: row.GetProperty("rate").GetDouble(),
            DifficultyMultiplier: row.GetProperty("difficulty").GetDouble(), Converted: row.GetProperty("convert").GetBoolean());
        var stable = row.GetProperty("classic").GetBoolean();
        var profile = stable ? ManiaProfiles.CreateStable(source, options) : ManiaProfiles.CreateLazer(source, options);
        var rule = profile.Chart.Notes[0].Rule;
        var windows = ((TapRule)(rule is OrderedNoteRule ordered ? ordered.Inner : rule)).Configuration.Windows;
        Assert.That(windows.Entries.Select(w => Time.ToMilliseconds(w.Late)), Is.EqualTo(row.GetProperty("windows").EnumerateArray().Select(w => w.GetDouble()).ToArray()));
    }

    private static IEnumerable<JsonElement> read(string fixture)
    {
        var assembly = typeof(UpstreamWindowTest).Assembly;
        using var stream = assembly.GetManifestResourceStream($"RhythmEngine.Tests.Reference.{fixture}")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }
}
