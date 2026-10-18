using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using NUnit.Framework;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.BeatmapTest.Decoding;

[TestFixture]
[NonParallelizable]
public class BmsTimingMapQueryTest
{
    private const string CAUTION_LIFETIMES = "FB282145C2C0AC8E90BBC4C4FFC9F2C9CB2E818D81A0E1EBCBE8342043D0FFFD";
    private const string SCROLLPLUS_LIFETIMES = "13D57E0EB00D932CC736B71494A633B0BB68707A8380B0616B1CADB3F10C570C";

    private static int binarySearches;
    private static BmsTimingMap observedMap;
    private static double[] referenceTimes = [];
    private static long queryCount;
    private static long distantForwardQueries;
    private static long originalWalkSteps;
    private static long boundedWalkSteps;
    private static long statelessQueries;
    private static readonly FieldInfo timingCursor = typeof(BmsTimingMap).GetField("cursor", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [TestCase("99_outlaw_caution.bms", CAUTION_LIFETIMES)]
    [TestCase("99_outlaw_scrollplus.bms", SCROLLPLUS_LIFETIMES)]
    public void RealChartLifetimeRefreshKeepsStatelessSnapshotResults(string chart, string expectedFingerprint)
    {
        // The observed stateless oracle supplies exact double fingerprints, so normal
        // JIT regression does not depend on intercepting possibly inlined methods.
        var (map, selected) = loadChart(chart);
        var actual = refreshLifetimes(map, selected);
        Assert.That(actual, Has.Length.EqualTo(192));
        Assert.That(fingerprint(actual), Is.EqualTo(expectedFingerprint));
    }

    [TestCase("99_outlaw_caution.bms", CAUTION_LIFETIMES)]
    [TestCase("99_outlaw_scrollplus.bms", SCROLLPLUS_LIFETIMES)]
    [Category("JudgementInstrumentation")]
    public void RealChartLifetimeRefreshKeepsIndependentStatelessResults(string chart, string expectedFingerprint)
    {
        var (map, selected) = loadChart(chart);
        var points = readPoints(map);
        var target = typeof(BmsTimingMap).GetMethod("findPoint", BindingFlags.Instance | BindingFlags.NonPublic)!;
        const BindingFlags callbacks = BindingFlags.Static | BindingFlags.NonPublic;
        observedMap = map;
        queryCount = distantForwardQueries = originalWalkSteps = boundedWalkSteps = 0;
        try
        {
            (double Start, double End)[] actual;
            using (new ScopedMethodProbe(target, typeof(BmsTimingMapQueryTest).GetMethod(nameof(beginQuery), callbacks)!,
                       typeof(BmsTimingMapQueryTest).GetMethod(nameof(endQuery), callbacks)!))
                actual = refreshLifetimes(map, selected);
            Assert.That(queryCount, Is.GreaterThan(0));
            TestContext.WriteLine($"TIMING_LIFETIME chart={chart} intervals={points.Length} queries={queryCount} distantForward={distantForwardQueries} originalWalkSteps={originalWalkSteps} boundedWalkSteps={boundedWalkSteps}");

            referenceTimes = points.Select(point => point.Time).ToArray();
            statelessQueries = 0;
            // A test-only stateless interval oracle drives the unchanged real lifetime
            // algorithm, so the comparison covers its entire visibility refinement.
            using (new ScopedMethodProbe(target, typeof(BmsTimingMapQueryTest).GetMethod(nameof(useStatelessPoint), callbacks)!))
            {
                var reference = refreshLifetimes(map, selected);
                Assert.That(statelessQueries, Is.EqualTo(queryCount), "Every reference query must use the independent oracle.");
                Assert.That(reference, Is.EqualTo(actual));
                Assert.That(reference, Has.Length.EqualTo(192));
                Assert.That(fingerprint(reference), Is.EqualTo(expectedFingerprint));
                TestContext.WriteLine($"TIMING_LIFETIME_REFERENCE chart={chart} count={reference.Length} sha256={fingerprint(reference)}");
            }
        }
        finally
        {
            observedMap = null;
            referenceTimes = [];
        }
    }

    private static (BmsTimingMap Map, BmsHitObject[] Selected) loadChart(string chart)
    {
        using var stream = File.OpenRead(chartPath(chart));
        using var reader = new LineBufferedReader(stream);
        var decoded = new BmsBeatmapDecoder(_ => 1).Decode(reader);
        var beatmap = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()) { BranchRandomValueSelector = _ => 1 }.Convert();
        var notes = beatmap.HitObjects.Where(note => note is not BmsLandmine).ToArray();
        var selected = Enumerable.Range(0, Math.Min(64, notes.Length)).Select(i => notes[i * notes.Length / Math.Min(64, notes.Length)]).ToArray();
        Assert.That(selected, Has.Length.EqualTo(64));
        return (beatmap.TimingMap!, selected);
    }

    private static (double Start, double End)[] refreshLifetimes(BmsTimingMap map, BmsHitObject[] selected)
    {
        var scroll = new BmsGameplayScrollController(map);
        var entries = selected.Select(note => new BmsHitObjectLifetimeEntry(note, scroll, () => 0)).ToArray();
        double[] speeds = [8, 12, 6];
        return speeds.SelectMany(speed =>
        {
            scroll.SetConfiguredScrollSpeed(speed);
            foreach (var entry in entries) entry.RefreshLifetime();
            return entries.Select(entry => (entry.LifetimeStart, entry.LifetimeEnd)).ToArray();
        }).ToArray();
    }

    private static string fingerprint((double Start, double End)[] lifetimes)
    {
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true))
        {
            foreach (var (start, end) in lifetimes)
            {
                writer.Write(start);
                writer.Write(end);
            }
        }
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }

    private static string chartPath(string chart, [CallerFilePath] string sourceFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "../../../bms_test_songs/103_outlaw_ogg", chart));

    private static void beginQuery(BmsTimingMap __instance, out int __state)
        => __state = ReferenceEquals(__instance, observedMap) ? (int)timingCursor.GetValue(__instance)! : -1;

    private static void endQuery(int __result, int __state)
    {
        if (__state < 0) return;
        queryCount++;
        var distance = Math.Max(0, __result - __state);
        if (distance > 8) distantForwardQueries++;
        originalWalkSteps += distance;
        boundedWalkSteps += Math.Min(8, distance);
    }

    private static bool useStatelessPoint(BmsTimingMap __instance, double time, ref int __result)
    {
        if (!ReferenceEquals(__instance, observedMap)) return true;
        statelessQueries++;
        __result = Math.Max(0, Array.FindLastIndex(referenceTimes, start => start <= time));
        return false;
    }

    [Test]
    [Category("JudgementInstrumentation")]
    public void DistantForwardQueriesUseBinarySearchWhileNearbyPlaybackKeepsWalking()
    {
        var bpms = Enumerable.Range(0, 4096).Select(i => new BmsBpmEvent(i * 48, 120, i * 500));
        var map = new BmsTimingMap(192, [new(0, 0, 192, 1)], bpms, [], [], [], 120);
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        using var probe = new ScopedMethodProbe(typeof(BmsTimingMap).GetMethod("findPointByBinarySearch", instance)!,
            typeof(BmsTimingMapQueryTest).GetMethod(nameof(countBinarySearch), BindingFlags.Static | BindingFlags.NonPublic)!);
        binarySearches = 0;
        map.GetSpeedFactorAtTime(4095 * 500);
        Assert.That(binarySearches, Is.EqualTo(1), "A jump across thousands of segments must leave the walking path.");
        map.GetScrollPositionAtTime(4095 * 500 + 100);
        Assert.That(binarySearches, Is.EqualTo(1));

        map.GetSpeedFactorAtTime(-1);
        map.GetSpeedFactorAtTime(8 * 500);
        Assert.That(binarySearches, Is.EqualTo(1), "Eight nearby segment transitions retain the walking cursor.");
        map.GetSpeedFactorAtTime(17 * 500);
        Assert.That(binarySearches, Is.EqualTo(2));

        map.GetSpeedFactorAtTime(-1);
        for (var i = 0; i < 4096; i++)
            map.GetSpeedFactorAtTime(i * 500);
        Assert.That(binarySearches, Is.EqualTo(2), "Continuous playback must not introduce repeated binary searches.");
    }

    private static void countBinarySearch() => binarySearches++;

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(8)]
    [TestCase(9)]
    [TestCase(4096)]
    public void InterleavedQueriesKeepStatelessTimelineResults(int count)
    {
        var map = createMap(count);
        assertQueries(map);
        assertQueries(map.ShiftedBy(1234.5));
    }

    private static void assertQueries(BmsTimingMap map)
    {
        var points = readPoints(map);
        var random = new Random(9301);
        // Endpoint precomputation and lifetime refinement interleave nearby playback
        // times with distant or backwards queries on the same map.
        var boundaries = points.SelectMany(point => new[] { point.Time - .00001, point.Time, point.Time + .00001 });
        var distant = Enumerable.Range(0, 128).SelectMany(i => new[]
        {
            points[^1].Time - i * 5, points[0].Time + i * 5,
            points[random.Next(points.Length)].Time + .125,
        });
        var queries = boundaries.Concat(boundaries.Reverse()).Concat(distant).ToArray();
        foreach (var time in queries)
        {
            // Select from immutable intervals independently of the map's walking cursor.
            var point = time < points[0].Time ? points[0] : points.Last(candidate => candidate.Time <= time);
            var advance = (time - point.Time) * (point.Bpm * (map.TickResolution / 4d)) / 60000;
            var expectedScroll = point.IsStop ? point.ScrollPos
                : advance * point.ScrollFactor * point.ScrollDir * (60000 / map.ScrollReferenceBpm)
                  / (map.TickResolution / 4d) + point.ScrollPos;
            Assert.That(map.GetScrollPositionAtTime(time), Is.EqualTo(expectedScroll).Within(1e-7), $"scroll at {time}");
            Assert.That(map.GetSpeedFactorAtTime(time), Is.EqualTo(point.SpeedFactor), $"speed at {time}");
            Assert.That(map.GetScrollFactorAtTime(time), Is.EqualTo(point.ScrollFactor), $"factor at {time}");
        }
        TestContext.WriteLine($"TIMING_QUERY intervals={points.Length} queries={queries.Length} offset={points[0].Time}");
    }

    private static BmsTimingMap createMap(int count)
    {
        var bpms = Enumerable.Range(0, count).Select(i => new BmsBpmEvent(i * 37, i % 3 == 0 ? -150 : 180, i * 500, i));
        var stops = Enumerable.Range(0, count / 2).Select(i => new BmsStopEvent(i * 74, i % 4 == 0 ? 0 : 20, 0, 150, i));
        var scrolls = Enumerable.Range(0, count / 2).Select(i => new BmsScrollEvent(i * 74 + 1, i % 5 * .5 - .5, i));
        var speeds = Enumerable.Range(0, count / 2).Select(i => new BmsSpeedEvent(i * 74 + 2, i % 4 * .5, i));
        return new BmsTimingMap(192, [new(0, 0, 192, 1)], bpms, stops, scrolls, speeds, 150);
    }

    private readonly record struct Point(double Time, double ScrollPos, double Bpm, double ScrollFactor,
                                         double SpeedFactor, int ScrollDir, bool IsStop);

    private static Point[] readPoints(BmsTimingMap map)
    {
        var values = (Array)typeof(BmsTimingMap).GetField("points", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(map)!;
        return values.Cast<object>().Select(value =>
        {
            T field<T>(string name) => (T)value.GetType().GetField(name)!.GetValue(value)!;
            return new Point(field<double>("Time"), field<double>("ScrollPos"), field<double>("Bpm"),
                field<double>("ScrollFactor"), field<double>("SpeedFactor"), field<int>("ScrollDir"), field<bool>("IsStop"));
        }).ToArray();
    }
}
