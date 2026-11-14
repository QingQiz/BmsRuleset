using System.Text.Json;
using NUnit.Framework;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Malody;

namespace RhythmEngine.Tests;

[TestFixture]
public class MalodyNativeConformanceTest
{
    public static IEnumerable<TestCaseData> NativeHeads()
    {
        foreach (var client in new[] { MalodyClient.ClassicPc, MalodyClient.ClassicMobile })
        {
            foreach (var row in read("heads", client))
                yield return new TestCaseData(client, row).SetName($"{client}437_Head_Rank{row.GetProperty("rank")}_Offset{row.GetProperty("offset")}");
        }
    }

    public static IEnumerable<TestCaseData> NativeReleases()
    {
        foreach (var client in new[] { MalodyClient.ClassicPc, MalodyClient.ClassicMobile })
        {
            foreach (var row in read("releases", client))
                yield return new TestCaseData(client, row).SetName($"{client}437_Release_Rank{row.GetProperty("rank")}_Time{row.GetProperty("time")}");
        }
    }

    public static IEnumerable<TestCaseData> NativeScoreRanks()
    {
        foreach (var client in new[] { MalodyClient.ClassicPc, MalodyClient.ClassicMobile })
        {
            foreach (var judge in Enum.GetValues<MalodyJudge>())
                yield return new TestCaseData(client, judge);
        }
    }

    public static IEnumerable<TestCaseData> NativeRoutingRanks()
    {
        foreach (var client in new[] { MalodyClient.ClassicPc, MalodyClient.ClassicMobile })
        {
            foreach (var group in read("routes", client).GroupBy(row => row.GetProperty("rank").GetInt32()))
                yield return new TestCaseData(client, group.ToArray()).SetName($"{client}437_Routing_Rank{group.Key}");
        }
    }

    [TestCaseSource(nameof(NativeHeads))]
    public void HeadMatchesOriginalInstructions(MalodyClient client, JsonElement row)
    {
        var source = new KeyChart(1, [new KeyNote(0, 0, ms(1000), ms(1000))]);
        var options = new MalodyOptions(Client: client, Judge: (MalodyJudge)row.GetProperty("rank").GetInt32());
        var session = MalodyProfile.Create(source, options).CreateSession();
        var time = ms(1000 + row.GetProperty("offset").GetInt32());
        session.AdvanceTo(time, [new InputEvent(time, 0, true)]);

        var expected = row.GetProperty("result").GetString();
        Assert.That(session.EventCount, Is.EqualTo(expected == null ? 0 : 1));
        if (expected != null)
            Assert.That(session.GetEventsSince(0).Single().Result.Value, Is.EqualTo(expected));
        Assert.That(session.GetNote(0).Completed, Is.EqualTo(row.GetProperty("consumed").GetBoolean()));
    }

    [TestCaseSource(nameof(NativeReleases))]
    public void ReleaseMatchesOriginalInstructions(MalodyClient client, JsonElement row)
    {
        var source = new KeyChart(1, [new KeyNote(0, 0, ms(1000), ms(2000), KeyNoteKind.Hold)]);
        var options = new MalodyOptions(Client: client, Judge: (MalodyJudge)row.GetProperty("rank").GetInt32());
        var session = MalodyProfile.Create(source, options).CreateSession();
        var time = ms(row.GetProperty("time").GetInt32());
        session.AdvanceTo(time, [new InputEvent(ms(1000), 0, true), new InputEvent(time, 0, false)]);

        Assert.That(session.GetEventsSince(0).Any(e => e.Result.Value == "hold-drop"), Is.EqualTo(row.GetProperty("result").GetString() == "hold-drop"));
        Assert.That(session.Completed, Is.EqualTo(row.GetProperty("finished").GetBoolean()));
        Assert.That(session.Score.AccuracyMaximum, Is.EqualTo(1000));
    }

    [TestCase(MalodyClient.ClassicPc)]
    [TestCase(MalodyClient.ClassicMobile)]
    public void TicksMatchOriginalInstructionsIncludingTheExactTail(MalodyClient client)
    {
        var source = new KeyChart(1, [new KeyNote(0, 0, ms(1000), ms(2000), KeyNoteKind.Hold)]);
        var session = MalodyProfile.Create(source, new MalodyOptions(Client: client)).CreateSession();
        session.AdvanceTo(ms(2100), [new InputEvent(ms(1000), 0, true)]);
        var expected = read("ticks", client);
        Assert.That(session.GetEventsSince(0).Where(e => e.Result.Value == "hold-tick").Select(e => e.Time),
            Is.EqualTo(expected.Select(row => ms(row.GetProperty("time").GetInt32()))));
        Assert.That(session.Score.Combo, Is.EqualTo(1 + expected.Length));
        Assert.That(session.Completed, Is.True);
    }

    [TestCaseSource(nameof(NativeScoreRanks))]
    public void ScoreHealthAndBonusMatchOriginalInstructions(MalodyClient client, MalodyJudge judge)
    {
        var rows = read("scores", client).Where(row => row.GetProperty("rank").GetInt32() == (int)judge).ToArray();
        var source = new KeyChart(1, rows.Select((_, i) => new KeyNote(i, 0, ms((i + 1) * 1000), ms((i + 1) * 1000))));
        var session = MalodyProfile.Create(source, new MalodyOptions(Client: client, Judge: judge, FailOnEmpty: false)).CreateSession();

        foreach (var row in rows)
        {
            var index = row.GetProperty("index").GetInt32();
            var time = (index + 1) * 1000;
            var hitTime = ms(time + row.GetProperty("offset").GetInt32());
            session.AdvanceTo(ms(time + 500), [new InputEvent(hitTime, 0, true), new InputEvent(hitTime + ms(1), 0, false)]);
            Assert.Multiple(() =>
            {
                Assert.That(session.GetEventsSince(index).Single().Result.Value, Is.EqualTo(row.GetProperty("result").GetString()), $"result {index}");
                Assert.That(session.Score.Score, Is.EqualTo(row.GetProperty("score").GetDouble()), $"score {index}");
                Assert.That(session.Score.Accuracy, Is.EqualTo(row.GetProperty("accuracy").GetDouble()).Within(1e-6), $"accuracy {index}");
                Assert.That(session.Health.Value, Is.EqualTo(row.GetProperty("health").GetDouble()).Within(1e-7), $"health {index}");
                Assert.That(session.Score.Bonus, Is.EqualTo(row.GetProperty("bonus").GetDouble() / 100).Within(1e-12), $"bonus {index}");
                Assert.That(session.Score.Combo, Is.EqualTo(row.GetProperty("combo").GetInt32()), $"combo {index}");
                Assert.That(session.Score.MaximumCombo, Is.EqualTo(row.GetProperty("max_combo").GetInt32()), $"max combo {index}");
            });
        }
    }

    [TestCaseSource(nameof(NativeRoutingRanks))]
    public void ClassicRoutingAndExpiryMatchOriginalKeyLoops(MalodyClient client, JsonElement[] rows)
    {
        foreach (var row in rows)
        {
            var source = new KeyChart(1, row.GetProperty("notes").EnumerateArray().Select((note, i) =>
            {
                var start = ms(note.GetProperty("start").GetInt32());
                var end = ms(note.GetProperty("end").GetInt32());
                return new KeyNote(i, 0, start, end, end > start ? KeyNoteKind.Hold : KeyNoteKind.Tap);
            }));
            var session = MalodyProfile.Create(source, new MalodyOptions(Client: client,
                Judge: (MalodyJudge)row.GetProperty("rank").GetInt32())).CreateSession();
            var inputs = row.GetProperty("inputs").EnumerateArray().Select(input =>
                new InputEvent(ms(input.GetProperty("time").GetInt32()), 0, input.GetProperty("pressed").GetBoolean())).ToArray();
            session.AdvanceTo(ms(row.GetProperty("until").GetInt32()), inputs);
            var expected = row.GetProperty("events").EnumerateArray().Select(e =>
            {
                var code = e.GetProperty("code").GetUInt32();
                var resultId = (code & 15) switch { 1 => "miss", 2 => "best", 4 => "cool", 8 => "good", _ => code == 128 ? "hold-tick" : code == 0x40000 ? "hold-drop" : null };
                return (e.GetProperty("note").GetInt32(), resultId);
            }).Where(e => e.Item2 != null);
            Assert.That(session.GetEventsSince(0).Select(e => (e.NoteId, e.Result.Value)), Is.EqualTo(expected), row.GetProperty("scenario").GetString());
        }
    }

    private static JsonElement[] read(string collection, MalodyClient client)
    {
        var name = client == MalodyClient.ClassicPc ? "malody-pc-437.json" : "malody-android-437.json";
        using var stream = typeof(MalodyNativeConformanceTest).Assembly.GetManifestResourceStream($"RhythmEngine.Tests.Reference.{name}")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty(collection).EnumerateArray().Select(row => row.Clone()).ToArray();
    }

    private static long ms(double value) => Time.FromMilliseconds(value);
}
