using System.Text.Json;
using NUnit.Framework;
using RhythmEngine.Core;
using RhythmEngine.Profiles.O2Jam;

namespace RhythmEngine.Tests;

[TestFixture]
public class O2JamNativeConformanceTest
{
    public static IEnumerable<TestCaseData> Heads()
    {
        foreach (var row in read("heads"))
            yield return new TestCaseData(row).SetName($"O2Jam338_Head_BPM{row.GetProperty("bpm")}_Time{row.GetProperty("time")}");
    }

    public static IEnumerable<TestCaseData> Holds()
    {
        foreach (var row in read("holds"))
            yield return new TestCaseData(row).SetName($"O2Jam338_Hold_{row.GetProperty("bpm")}_{row.GetProperty("headTime")}_{(row.TryGetProperty("release", out var release) ? release : row.GetProperty("advance"))}");
    }

    [TestCaseSource(nameof(Heads))]
    public void ClockAndHeadJudgementMatchOriginalInputLoop(JsonElement row)
    {
        var clock = clockFor(row);
        var time = ms(row.GetProperty("time").GetInt32());
        Assert.That(clock.TickAt(time), Is.EqualTo(row.GetProperty("tick").GetInt32()));
        var session = O2Jam338Profile.Create(1, [new O2JamTickNote(0, 0, 192)], clock).CreateSession();
        session.AdvanceTo(time, [new InputEvent(time, 0, true)]);
        // The native fixture calls the input loop before the separate expiry pass.
        var expected = row.GetProperty("events").EnumerateArray().Select(result).ToArray();
        Assert.That(session.GetEventsSince(0).Where(e => e.Offset != null).Select(e => e.Result), Is.EqualTo(expected));
    }

    [TestCaseSource(nameof(Holds))]
    public void HoldReleaseAndTimeoutMatchOriginalInstructions(JsonElement row)
    {
        var clock = clockFor(row);
        var session = O2Jam338Profile.Create(1, [new O2JamTickNote(0, 0, 192, 288)], clock).CreateSession();
        var inputs = new List<InputEvent>();
        if (row.GetProperty("headTime").ValueKind != JsonValueKind.Null)
            inputs.Add(new InputEvent(ms(row.GetProperty("headTime").GetInt32()), 0, true));
        var release = row.TryGetProperty("release", out var releaseTime);
        var end = ms(release ? releaseTime.GetInt32() : row.GetProperty("advance").GetInt32());
        if (release)
            inputs.Add(new InputEvent(end, 0, false));
        session.AdvanceTo(end, inputs.ToArray());
        Assert.That(session.GetEventsSince(0).Select(e => e.Result), Is.EqualTo(row.GetProperty("events").EnumerateArray().Select(result)));
    }

    [Test]
    public void ExpiryBoundariesMatchTheNativeIntegerClock()
    {
        foreach (var row in read("timeouts"))
        {
            var clock = clockFor(row.GetProperty("bpm").GetInt32());
            var session = O2Jam338Profile.Create(1, [new O2JamTickNote(0, 0, 192, 288)], clock).CreateSession();
            var time = ms(row.GetProperty("time").GetInt32());
            Assert.That(clock.TickAt(time), Is.EqualTo(row.GetProperty("tick").GetInt32()));
            session.AdvanceTo(time, row.GetProperty("heldTail").GetBoolean() ? [new InputEvent(clock.TimeAt(192), 0, true)] : []);
            Assert.That(session.GetEventsSince(0).Select(e => e.Result), Is.EqualTo(row.GetProperty("events").EnumerateArray().Select(result)), row.ToString());
        }
    }

    [Test]
    public void ScorePillsJamComboAndHealthMatchOriginalClientSequence()
    {
        foreach (var row in read("scores"))
        {
            var rows = row.GetProperty("states").EnumerateArray().ToArray();
            var clock = new O2JamClock([new O2JamClockPoint(0, 0), new O2JamClockPoint((rows.Length + 2) * 2000, (rows.Length + 2) * 192)]);
            var profile = O2Jam338Profile.Create(1, Enumerable.Range(0, rows.Length).Select(i => new O2JamTickNote(i, 0, (i + 1) * 192)),
                clock, (O2JamDifficulty)row.GetProperty("difficulty").GetInt32());
            var session = profile.CreateSession();
            for (var i = 0; i < rows.Length; i++)
            {
                var expected = rows[i];
                var code = expected.GetProperty("input").GetInt32();
                var nominal = (i + 1) * 2000;
                var hit = ms(nominal + (code == 32 ? 125 : code == 64 ? 209 : 0));
                session.AdvanceTo(ms(nominal + 500), code == 128 ? [] : [new InputEvent(hit, 0, true), new InputEvent(hit + ms(1), 0, false)]);
                using var scope = Assert.EnterMultipleScope();
                Assert.That(session.Score.Score, Is.EqualTo(expected.GetProperty("score").GetInt32()), $"score {i}");
                Assert.That(session.Health.Value, Is.EqualTo(expected.GetProperty("hp").GetInt32()), $"HP {i}");
                Assert.That(session.Score.Combo, Is.EqualTo(expected.GetProperty("displayCombo").GetInt32()), $"combo {i}");
                Assert.That(session.Score.MaximumCombo, Is.EqualTo(expected.GetProperty("maxCombo").GetInt32()), $"max combo {i}");
                foreach (var (counter, key) in new[]
                         {
                             ("chain", "combo"), ("cool", "cool"), ("good", "good"), ("bad", "bad"), ("miss", "miss"),
                             ("jam", "jam"), ("jam-progress", "jamProgress"), ("pills", "pills"), ("cool-streak", "coolStreak")
                         })
                    Assert.That(session.Score.Counters.GetValueOrDefault(counter), Is.EqualTo(expected.GetProperty(key).GetInt32()), $"{counter} {i}");
            }
        }
    }

    private static string result(JsonElement row) => row.GetProperty("code").GetInt32() switch
    {
        16 => "cool", 32 => "good", 64 => "bad", 128 or 384 => "miss", _ => throw new InvalidOperationException(),
    };

    private static O2JamClock clockFor(int bpm) => new(Enumerable.Range(0, 4).Select(i => new O2JamClockPoint(i * 240000 / bpm, i * 192)));

    private static O2JamClock clockFor(JsonElement row) => row.TryGetProperty("clockPoints", out var points)
        ? new O2JamClock(points.EnumerateArray().Select(p => new O2JamClockPoint(p[0].GetInt32(), p[1].GetInt32())))
        : clockFor(row.GetProperty("bpm").GetInt32());

    private static JsonElement[] read(string collection)
    {
        using var stream = typeof(O2JamNativeConformanceTest).Assembly.GetManifestResourceStream("RhythmEngine.Tests.Reference.o2jam-338.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty(collection).EnumerateArray().Select(row => row.Clone()).ToArray();
    }

    private static long ms(int time) => time * 1000L;
}
