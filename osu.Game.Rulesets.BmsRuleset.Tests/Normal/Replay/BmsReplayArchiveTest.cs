#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using osu.Framework.IO.Stores;
using osu.Game.Extensions;
using osu.Game.Models;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Replay;

[TestFixture]
public class BmsReplayArchiveTest
{
    private static Score createScore() => new()
    {
        Replay = { Frames = [new BmsReplayFrame(1234.125, BmsAction.Key1)] },
    };

    [Test]
    public void TestArchiveContainsOnlyOperations()
    {
        var original = createScore();
        original.Replay.Frames =
        [
            new BmsReplayFrame(0),
            new BmsReplayFrame(20),
            new BmsReplayFrame(1000.125, BmsAction.Key1),
            new BmsReplayFrame(1020.125, BmsAction.Key1),
            new BmsReplayFrame(1020.125),
            new BmsReplayFrame(1020.125, BmsAction.Key1),
            new BmsReplayFrame(900, BmsAction.Key1),
            new BmsReplayFrame(950, BmsAction.Key1),
        ];
        var restored = BmsTestReplayArchive.RoundTrip(original, json =>
        {
            var payload = JObject.Parse(json);
            var frames = (JArray)payload["frames"]!;
            Assert.Multiple(() =>
            {
                Assert.That(frames, Has.Count.EqualTo(4));
                Assert.That(payload.Properties().Select(property => property.Name), Is.EquivalentTo(new[] { "algorithm", "frames" }));
                Assert.That(frames.OfType<JObject>().All(frame => frame.Properties().All(property => property.Name is "time" or "actions")), Is.True);
            });
            return json;
        });
        Assert.That(restored.Replay.Frames.Select(frame => frame.Time), Is.EqualTo(new[] { 0, 900, 1020.125, 1020.125 }));
        Assert.That(restored.Replay.Frames.Cast<BmsReplayFrame>().Select(frame => frame.Actions.Count), Is.EqualTo(new[] { 0, 1, 0, 1 }));
    }

    [Test]
    public void TestArchiveReadsOnlyOperationsInTimeOrder()
    {
        var original = createScore();
        original.Replay.Frames =
        [
            new BmsReplayFrame(0),
            new BmsReplayFrame(1000, BmsAction.Key1),
            new BmsReplayFrame(2500, BmsAction.Key1),
            new BmsReplayFrame(1500, BmsAction.Key1),
            new BmsReplayFrame(2000, BmsAction.Key1),
            new BmsReplayFrame(3000),
        ];
        var restored = BmsTestReplayArchive.RoundTrip(original);
        Assert.That(restored.Replay.Frames.Select(frame => frame.Time), Is.EqualTo(new[] { 0, 1000, 3000 }));
    }

    [Test]
    public void TestAlgorithmAndCourseGaugeSurviveScoreClone([Values] BmsJudgementAlgorithm algorithm)
    {
        var original = createScore();
        original.Replay.Frames = [new BmsReplayFrame(1234.125, BmsAction.Key1) { Algorithm = algorithm }, new BmsReplayFrame(5000.375, BmsAction.Key1)];
        original.ScoreInfo.Ruleset = new BmsRuleset().RulesetInfo;
        original.ScoreInfo.Mods = [new BmsModClassGauge
        {
            GaugeProfileFamily = { Value = BmsGaugeProfileFamily.Lr2 },
            InitialHealth = { Value = 0.4 },
        }];
        var restored = BmsTestReplayArchive.RoundTrip(original.DeepClone());
        Assert.That(restored.Replay.Frames[0].IsEquivalentTo(original.Replay.Frames[0]), Is.True);
        Assert.That(restored.Replay.Frames[^1].Time, Is.EqualTo(5000.375));
        var gauge = restored.ScoreInfo.Mods.OfType<BmsModClassGauge>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(BmsReplayFrame.GetAlgorithm(restored.Replay), Is.EqualTo(algorithm));
            Assert.That(gauge.GaugeProfileFamily.Value, Is.EqualTo(BmsGaugeProfileFamily.Lr2));
            Assert.That(gauge.InitialHealth.Value, Is.EqualTo(0.4));
        });
    }

    [Test]
    public void TestAllActionsAndSameTimestampSequenceRoundTrip()
    {
        var original = createScore();
        original.Replay.Frames = [new BmsReplayFrame(-1234.375)];
        foreach (var action in Enum.GetValues<BmsAction>())
        {
            original.Replay.Frames.Add(new BmsReplayFrame(1000.125, action));
            original.Replay.Frames.Add(new BmsReplayFrame(1000.125));
        }
        var restored = BmsTestReplayArchive.RoundTrip(original);
        Assert.That(restored.Replay.Frames, Has.Count.EqualTo(original.Replay.Frames.Count));
        Assert.That(restored.Replay.Frames.Zip(original.Replay.Frames).All(pair => pair.First.IsEquivalentTo(pair.Second)), Is.True);
    }

    [TestCase(1, false)]
    [TestCase(2, true)]
    [TestCase(3, true)]
    [TestCase(4, true)]
    public void TestOldArchivesImportOnlyInputs(int version, bool compressed)
    {
        var payload = new JObject
        {
            ["version"] = version,
            ["has_received_all_frames"] = true,
            ["frames"] = new JArray
            {
                new JObject { ["time"] = -1000000000, ["actions"] = new JArray(), ["branch_decisions"] = "2:1" },
                new JObject
                {
                    ["time"] = 0, ["actions"] = new JArray(), ["judgement_algorithm"] = 1,
                    ["header"] = "unused old statistics header",
                    ["includes_generated_inputs"] = true, ["course_gauge_mode"] = true,
                    ["gauge_profile_family"] = 1, ["initial_gauge_states"] = new JArray(),
                },
                new JObject { ["time"] = 20, ["actions"] = new JArray() },
                new JObject { ["time"] = 1000.125, ["actions"] = new JArray((int)BmsAction.Key1) },
                new JObject { ["time"] = 1020.125, ["actions"] = new JArray((int)BmsAction.Key1) },
                new JObject { ["time"] = 1020.125, ["actions"] = new JArray() },
                new JObject { ["time"] = 1020.125, ["actions"] = new JArray((int)BmsAction.Key1) },
            },
            ["judgement_events"] = "deliberately not a judgement ledger",
            ["gauge_history"] = "deliberately not a gauge history",
            ["input_indices"] = "unused old compaction data",
            ["frame_times"] = new JArray(0, 20, 1000.125, 1020.125, 1020.125, 1020.125, 5000.375),
        };
        var restored = read(encode(payload.ToString(), compressed));
        var frames = restored.Replay.Frames.Cast<BmsReplayFrame>().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(frames.Select(frame => frame.Time), Is.EqualTo(new[] { 0, 1000.125, 1020.125, 1020.125, 5000.375 }));
            Assert.That(restored.ScoreInfo.Mods, Is.Empty);
            Assert.That(BmsReplayFrame.GetAlgorithm(restored.Replay), Is.EqualTo(BmsJudgementAlgorithm.Combo));
            Assert.That(restored.ScoreInfo.HitEvents, Is.Empty);
        });
    }

    [Test]
    public void TestEmptyOldArchiveNeedsNoStatisticsOrFrameFields()
    {
        var restored = read("""{"version":1,"has_received_all_frames":true}"""u8.ToArray());
        Assert.That(restored.Replay.Frames, Is.Empty);
        Assert.That(restored.ScoreInfo.HitEvents, Is.Empty);
    }

    [Test]
    public void TestBareInputArraysRemainReadable()
    {
        var restored = read("""[{"time":0,"actions":[]},{"time":1000,"actions":[0]}]"""u8.ToArray());
        Assert.That(restored.Replay.Frames.Select(frame => frame.Time), Is.EqualTo(new double[] { 0, 1000 }));
        Assert.That(BmsReplayFrame.GetAlgorithm(restored.Replay), Is.EqualTo(BmsJudgementAlgorithm.Combo));
    }

    [Test]
    public void TestAlgorithmParticipatesInArchiveHash()
    {
        var original = createScore();
        var hash = BmsReplayArchive.ComputeHash(original);
        var first = (BmsReplayFrame)original.Replay.Frames[0];
        original.Replay.Frames[0] = new BmsReplayFrame(first.Time, first.Actions.ToArray()) { Algorithm = BmsJudgementAlgorithm.Duration };
        Assert.That(BmsReplayArchive.ComputeHash(original), Is.Not.EqualTo(hash));
    }

    [Test]
    public void TestReadScoreClearsStaleDerivedTiming()
    {
        using var archive = BmsReplayArchive.Create(createScore());
        var info = new ScoreInfo
        {
            HitEvents = [new HitEvent(0, 1, HitResult.Perfect, new BmsNote { StartTime = 1000 }, null, null)],
        };
        var restored = read(archive.Get(BmsReplayArchive.FILENAME), info);
        Assert.That(restored.ScoreInfo.HitEvents, Is.Empty);
    }

    [Test]
    public void TestArchiveHashIsItsContentHash()
    {
        var original = createScore();
        using var archive = BmsReplayArchive.Create(original, out var hash);
        var content = archive.Get(BmsReplayArchive.FILENAME);
        Assert.That(hash, Is.EqualTo(Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()));
        Assert.That(BmsReplayArchive.ComputeHash(original), Is.EqualTo(hash));
        ((BmsReplayFrame)original.Replay.Frames[0]).Actions.Add(BmsAction.Key2);
        Assert.That(BmsReplayArchive.ComputeHash(original), Is.Not.EqualTo(hash));
        Assert.That(archive.Filenames.Single(), Is.EqualTo(BmsReplayArchive.FILENAME));
        Assert.That(content.Take(2), Is.EqualTo(new byte[] { 0x1f, 0x8b }));
    }

    private static Score read(byte[] content, ScoreInfo? info = null)
    {
        var file = new RealmFile { Hash = "abcdef" };
        info ??= new ScoreInfo();
        info.Files.Add(new RealmNamedFileUsage(file, BmsReplayArchive.FILENAME));
        using var store = new TestResourceStore(file.GetStoragePath(), content);
        return BmsReplayArchive.ReadScore(info, store);
    }

    private static byte[] encode(string json, bool compressed)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        if (!compressed)
            return bytes;
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(bytes);
        return stream.ToArray();
    }

    private sealed class TestResourceStore(string filename, byte[] content) : IResourceStore<byte[]>
    {
        public byte[] Get(string name) => name == filename ? content : null!;
        public Stream GetStream(string name) => new MemoryStream(Get(name));
        public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Get(name));
        public IEnumerable<string> GetAvailableResources() => [filename];
        public void Dispose()
        {
        }
    }
}
