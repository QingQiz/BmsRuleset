using System.Text.Json;
using NUnit.Framework;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Malody;

namespace RhythmEngine.Tests;

[TestFixture]
public class MalodyVNativeConformanceTest
{
    public static IEnumerable<TestCaseData> Comparisons()
    {
        foreach (var group in read("comparisons").GroupBy(row => (Rank: row.GetProperty("rank").GetInt32(),
                     Pro: row.GetProperty("pro").GetBoolean(), Scale: row.GetProperty("scale").GetSingle(),
                     Suppress: row.GetProperty("suppressEarlyMiss").GetBoolean())))
            yield return new TestCaseData(group.ToArray()).SetName($"V6722_Compare_{group.Key}");
    }

    public static IEnumerable<TestCaseData> Releases()
    {
        foreach (var group in read("holds").GroupBy(row => (Rank: row.GetProperty("rank").GetInt32(),
                     Pro: row.GetProperty("pro").GetBoolean(), End: row.GetProperty("endJudgement").GetBoolean())))
            yield return new TestCaseData(group.ToArray()).SetName($"V6722_Release_{group.Key}");
    }

    public static IEnumerable<TestCaseData> Routes()
    {
        foreach (var group in read("routes").GroupBy(row => (Rank: row.GetProperty("rank").GetInt32(),
                     Pro: row.GetProperty("pro").GetBoolean(), End: row.GetProperty("endJudgement").GetBoolean())))
            yield return new TestCaseData(group.ToArray()).SetName($"V6722_Routing_{group.Key}");
    }

    [TestCaseSource(nameof(Comparisons))]
    public void FloatWindowComparisonsMatchOriginalInstructions(JsonElement[] rows)
    {
        var first = rows[0];
        var windows = MalodyProfile.VWindows((MalodyJudge)first.GetProperty("rank").GetInt32(), first.GetProperty("pro").GetBoolean(),
            !first.GetProperty("suppressEarlyMiss").GetBoolean(), scale: first.GetProperty("scale").GetSingle());
        foreach (var row in rows)
        {
            var offset = row.GetProperty("offset").GetInt32();
            var code = row.GetProperty("code").GetUInt32();
            Assert.That(windows.ResultFor(ms(offset))?.Value, Is.EqualTo(grade(code)), $"offset {offset}, code 0x{code:x}");
        }
    }

    [Test]
    public void ConvertedGradeEdgesMatchAllOriginalWindowTables()
    {
        foreach (var row in read("windows"))
        {
            var windows = MalodyProfile.VWindows((MalodyJudge)row.GetProperty("rank").GetInt32(), row.GetProperty("pro").GetBoolean(),
                conversionPercent: row.GetProperty("convert").GetInt32());
            var bounds = row.GetProperty("windows").EnumerateArray().Select(w => w.GetInt32()).ToArray();
            Assert.That(windows.Entries.Take(3).Select(w => w.Late), Is.EqualTo(bounds.Take(3).Select(v => ms(v))), row.ToString());
            Assert.That(windows.Entries[^1].Late, Is.EqualTo(ms(Math.Min(bounds[3], bounds[2] + 34))), row.ToString());
        }
    }

    [TestCaseSource(nameof(Releases))]
    public void ScheduledHoldResultsMatchOriginalRepeatDraining(JsonElement[] rows)
    {
        foreach (var row in rows)
        {
            var source = new KeyChart(1, [new KeyNote(0, 0, ms(1000), ms(2000), KeyNoteKind.Hold)]);
            var options = new MalodyOptions(Client: row.GetProperty("pro").GetBoolean() ? MalodyClient.VPro : MalodyClient.VStandard,
                Judge: (MalodyJudge)row.GetProperty("rank").GetInt32(), JudgeAtEnd: row.GetProperty("endJudgement").GetBoolean());
            var session = MalodyProfile.Create(source, options).CreateSession();
            var release = row.GetProperty("release").GetInt32();
            session.AdvanceTo(ms(release), [new InputEvent(ms(1000), 0, true), new InputEvent(ms(release), 0, false)]);
            var outputs = row.GetProperty("outputs").EnumerateArray().ToArray();
            var expected = outputs.Select(o => o.GetProperty("code").GetUInt32()).Select(result).Where(r => r != null);
            Assert.That(session.GetEventsSince(0).Where(e => e.Part.Value != "head" && e.Result.Value != "critical").Select(e => e.Result.Value),
                Is.EqualTo(expected), $"release {release}");
            Assert.That(session.Completed, Is.EqualTo(outputs[^1].GetProperty("finished").GetBoolean()), $"release {release}");
            var criticals = outputs.Count(o => (o.GetProperty("code").GetUInt32() & 0x4000000) != 0) + 1;
            Assert.That(session.Score.Counters.GetValueOrDefault("critical"), Is.EqualTo(criticals), $"release {release}");
        }
    }

    [TestCaseSource(nameof(Routes))]
    public void KeyRoutingExpiryAndRepeatedDispatchMatchOriginalInstructions(JsonElement[] rows)
    {
        foreach (var row in rows)
        {
            var source = new KeyChart(1, row.GetProperty("notes").EnumerateArray().Select((note, i) =>
            {
                var start = ms(note.GetProperty("start").GetInt32());
                var end = ms(note.GetProperty("end").GetInt32());
                return new KeyNote(i, 0, start, end, end > start ? KeyNoteKind.Hold : KeyNoteKind.Tap);
            }));
            var options = new MalodyOptions(Client: row.GetProperty("pro").GetBoolean() ? MalodyClient.VPro : MalodyClient.VStandard,
                Judge: (MalodyJudge)row.GetProperty("rank").GetInt32(), JudgeAtEnd: row.GetProperty("endJudgement").GetBoolean());
            var session = MalodyProfile.Create(source, options).CreateSession();
            var inputs = row.GetProperty("inputs").EnumerateArray().Select(input =>
                new InputEvent(ms(input.GetProperty("time").GetInt32()), 0, input.GetProperty("pressed").GetBoolean())).ToArray();
            session.AdvanceTo(ms(row.GetProperty("until").GetInt32()), inputs);
            var expected = row.GetProperty("events").EnumerateArray().Select(e =>
            {
                var code = e.GetProperty("code").GetUInt32();
                var tail = (code & 0x800) != 0 || e.GetProperty("input").GetInt32() == 4;
                var part = grade(code) != null ? tail ? "tail" : "head" : "body";
                return (e.GetProperty("note").GetInt32(), part, result(code));
            }).Where(e => e.Item3 != null);
            Assert.That(session.GetEventsSince(0).Where(e => e.Result.Value != "critical").Select(e => (e.NoteId, e.Part.Value, e.Result.Value)),
                Is.EqualTo(expected), row.GetProperty("scenario").GetString());
        }
    }

    [Test]
    public void ScoreReducerMatchesNativeFloatArithmeticAndSeparateHealth()
    {
        foreach (var row in read("scores"))
        {
            var source = new KeyChart(1, [new KeyNote(0, 0, ms(1000), ms(2000), KeyNoteKind.Hold)]);
            var profile = MalodyProfile.Create(source, new MalodyOptions(Client: MalodyClient.VPro,
                Judge: (MalodyJudge)row.GetProperty("rank").GetInt32(), JudgeAtEnd: true));
            var configuration = profile.Configuration;
            var state = configuration.Score.InitialState;
            var health = configuration.Health.InitialState;
            var sequence = 0;
            var criticals = 0;
            foreach (var expected in row.GetProperty("states").EnumerateArray())
            {
                var code = expected.GetProperty("code").GetUInt32();
                var resultId = result(code)!;
                var part = grade(code) != null ? (code & 0x800) != 0 ? "tail" : "head" : "body";
                var time = ms(++sequence * 1000);
                var emission = new JudgementEvent(sequence, 0, 0, new PartId(part), new ResultId(resultId), time, time, 0);
                ScoringOpportunity? opportunity = part == "body" ? null : profile.Chart.ScorePlan.Opportunities.Single(o => o.Part.Value == part);
                state = configuration.Score.Reduce(state, new ScoreContext(emission, opportunity, profile.Chart.ScorePlan));
                health = configuration.Health.Reduce(new HealthContext(emission, health, state, profile.Chart.ScorePlan));
                if ((code & 0x4000000) != 0)
                {
                    criticals++;
                    var critical = emission with { Part = MalodyParts.CRITICAL, Result = MalodyResults.CRITICAL };
                    state = configuration.Score.Reduce(state, new ScoreContext(critical, null, profile.Chart.ScorePlan));
                }

                Assert.Multiple(() =>
                {
                    Assert.That(state.Score, Is.EqualTo(expected.GetProperty("score").GetDouble()), $"score {sequence}");
                    Assert.That(state.Counters.GetValueOrDefault("malody-raw-score"), Is.EqualTo(expected.GetProperty("rawScore").GetDouble()).Within(1e-8), $"raw score {sequence}");
                    Assert.That(state.Accuracy * 100, Is.EqualTo(expected.GetProperty("accuracy").GetDouble()).Within(1e-5), $"accuracy {sequence}");
                    Assert.That(state.Bonus * 100, Is.EqualTo(expected.GetProperty("bonus").GetDouble()).Within(1e-10), $"bonus {sequence}");
                    Assert.That(health.Value, Is.EqualTo(expected.GetProperty("hp").GetDouble()).Within(1e-7), $"health {sequence}");
                    Assert.That(state.Combo, Is.EqualTo(expected.GetProperty("combo").GetInt32()), $"combo {sequence}");
                    Assert.That(state.MaximumCombo, Is.EqualTo(expected.GetProperty("maxCombo").GetInt32()), $"maximum combo {sequence}");
                    Assert.That(state.Counters.GetValueOrDefault("critical"), Is.EqualTo(criticals), $"criticals {sequence}");
                });
            }
        }
    }

    private static string? result(uint code) => (code & 128) != 0 ? "hold-tick" : (code & 0x40000) != 0 ? "hold-drop" : grade(code);

    private static string? grade(uint code) => (code & 15) switch { 1 => "miss", 2 => "best", 4 => "cool", 8 => "good", _ => null };

    private static JsonElement[] read(string collection)
    {
        using var stream = typeof(MalodyVNativeConformanceTest).Assembly.GetManifestResourceStream("RhythmEngine.Tests.Reference.malody-v-6722.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty(collection).EnumerateArray().Select(row => row.Clone()).ToArray();
    }

    private static long ms(double value) => Time.FromMilliseconds(value);
}
