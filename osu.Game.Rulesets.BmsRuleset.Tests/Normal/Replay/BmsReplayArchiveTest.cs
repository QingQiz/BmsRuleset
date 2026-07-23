using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Extensions;
using osu.Game.Models;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Replay;

[TestFixture]
public class BmsReplayArchiveTest
{
    [Test]
    public void TestReplayRoundTripsThroughArchive()
    {
        var original = new Score
        {
            Replay =
            {
                Frames =
                {
                    new BmsReplayFrame(0),
                    new BmsReplayFrame(1234, BmsAction.Key1, BmsAction.Scratch)
                    {
                        BranchDecisions = "2:1",
                    },
                },
            },
        };

        using var archive = BmsReplayArchive.Create(original);
        var replayFile = new RealmFile { Hash = "abcdef" };
        var readTarget = new ScoreInfo();
        readTarget.Files.Add(new RealmNamedFileUsage(replayFile, BmsReplayArchive.FILENAME));

        var restored = BmsReplayArchive.ReadScore(
            readTarget,
            new TestResourceStore(replayFile.GetStoragePath(), archive.Get(BmsReplayArchive.FILENAME)));

        var frames = restored.Replay.Frames.Cast<BmsReplayFrame>().ToArray();

        Assert.That(frames, Has.Length.EqualTo(2));
        Assert.That(frames[0].IsEquivalentTo(original.Replay.Frames[0]), Is.True);
        Assert.That(frames[1].IsEquivalentTo(original.Replay.Frames[1]), Is.True);
    }

    [Test]
    public void TestJudgementLedgerRoundTripsThroughArchive()
    {
        var original = createScore();
        BmsJudgementEventStore.Set(original.ScoreInfo,
        [
            new BmsJudgementEvent(
                BmsJudgementSource.From(new BmsNote { StartTime = 1000, Column = 2 }),
                HitResult.Great,
                [new BmsTimingObservation(BmsTimingObservationKind.Note, 1000, 988, 1, HitResult.Great)]),
            new BmsJudgementEvent(
                BmsJudgementSource.From(new BmsLongNote { StartTime = 2000, Duration = 500, Column = 4 }),
                HitResult.Ok,
                [
                    new BmsTimingObservation(BmsTimingObservationKind.LongNoteHead, 2000, 2034, 1.25, HitResult.Great),
                    new BmsTimingObservation(BmsTimingObservationKind.LongNoteTail, 2500, 2519, 1.25, HitResult.Ok),
                ]),
            new BmsJudgementEvent(
                BmsJudgementSource.From(new BmsLandmine { StartTime = 3000, Column = 1, LandmineDamagePercent = 50 }),
                HitResult.Meh,
                [new BmsTimingObservation(BmsTimingObservationKind.Note, 3000, 3000, 1, HitResult.Meh)]),
            new BmsJudgementEvent(
                BmsJudgementSource.From(new HitObject { StartTime = 4000 }),
                HitResult.Miss,
                [new BmsTimingObservation(BmsTimingObservationKind.Note, 4000, 4000, 1, HitResult.Miss)]),
        ]);

        using var archive = BmsReplayArchive.Create(original);
        var replayFile = new RealmFile { Hash = "abcdef" };
        var readTarget = new ScoreInfo();
        readTarget.Files.Add(new RealmNamedFileUsage(replayFile, BmsReplayArchive.FILENAME));

        var restored = BmsReplayArchive.ReadScore(
            readTarget,
            new TestResourceStore(replayFile.GetStoragePath(), archive.Get(BmsReplayArchive.FILENAME)));

        Assert.That(BmsJudgementEventStore.TryGet(restored.ScoreInfo, out var restoredEvents), Is.True);
        Assert.That(restoredEvents, Has.Count.EqualTo(4));
        Assert.That(restoredEvents[1].Source.Kind, Is.EqualTo(BmsJudgementSourceKind.LongNote));
        Assert.That(restoredEvents[1].Result, Is.EqualTo(HitResult.Ok));
        Assert.That(restoredEvents[1].TimingObservations.Select(o => o.Kind),
            Is.EqualTo(new[] { BmsTimingObservationKind.LongNoteHead, BmsTimingObservationKind.LongNoteTail }));

        Assert.That(restored.ScoreInfo.HitEvents, Has.Count.EqualTo(5));
        assertHitEvent(restored.ScoreInfo.HitEvents[0], -12, 1, HitResult.Great, typeof(BmsNote), 1000, 2);
        assertHitEvent(restored.ScoreInfo.HitEvents[1], 34, 1.25, HitResult.Great, typeof(BmsNote), 2000, 4);
        assertHitEvent(restored.ScoreInfo.HitEvents[2], 19, 1.25, HitResult.Ok, typeof(BmsNote), 2500, 4);
        assertHitEvent(restored.ScoreInfo.HitEvents[3], 0, 1, HitResult.Meh, typeof(BmsLandmine), 3000, 1);
        Assert.That(restoredEvents[1].Source.Duration, Is.EqualTo(500));
        Assert.That(restoredEvents[2].Source.LandmineDamagePercent, Is.EqualTo(50));

        // Empty POOR: round-trips as a base HitObject (no BmsHitObject), preserving press time + Miss.
        var emptyPoor = restored.ScoreInfo.HitEvents[4];
        Assert.That(emptyPoor.TimeOffset, Is.EqualTo(0));
        Assert.That(emptyPoor.GameplayRate, Is.EqualTo(1));
        Assert.That(emptyPoor.Result, Is.EqualTo(HitResult.Miss));
        Assert.That(emptyPoor.HitObject, Is.TypeOf<HitObject>());
        Assert.That(emptyPoor.HitObject, Is.Not.TypeOf<BmsHitObject>());
        Assert.That(emptyPoor.HitObject.StartTime, Is.EqualTo(4000));
    }

    [Test]
    public void TestGaugeHistoryRoundTripsThroughArchive()
    {
        var original = createScore();
        BmsScoreGaugeHistoryStore.Set(original.ScoreInfo,
        [
            new BmsGaugeHistoryEvent(1000, BmsGaugeType.Normal,
            [
                new BmsGaugeStateSnapshot(BmsGaugeType.Hard, 0, true),
                new BmsGaugeStateSnapshot(BmsGaugeType.Normal, 0.42, false),
            ]),
        ]);

        using var archive = BmsReplayArchive.Create(original);
        var replayFile = new RealmFile { Hash = "abcdef" };
        var readTarget = new ScoreInfo();
        readTarget.Files.Add(new RealmNamedFileUsage(replayFile, BmsReplayArchive.FILENAME));

        var restored = BmsReplayArchive.ReadScore(
            readTarget,
            new TestResourceStore(replayFile.GetStoragePath(), archive.Get(BmsReplayArchive.FILENAME)));

        Assert.That(BmsScoreGaugeHistoryStore.TryGet(restored.ScoreInfo, out var history), Is.True);
        Assert.That(history, Has.Count.EqualTo(1));
        Assert.That(history[0].ActiveGaugeType, Is.EqualTo(BmsGaugeType.Normal));
        Assert.That(history[0].States.Single(s => s.GaugeType == BmsGaugeType.Hard).Failed, Is.True);
        Assert.That(history[0].States.Single(s => s.GaugeType == BmsGaugeType.Normal).Health, Is.EqualTo(0.42));
    }

    [Test]
    public void TestSidecarDataSurvivesScoreCloneBeforeArchiveCreation()
    {
        var ruleset = new BmsRuleset();
        var original = createScore();
        original.ScoreInfo.Ruleset = ruleset.RulesetInfo;
        var judgementEvent = new BmsJudgementEvent(
            BmsJudgementSource.From(new BmsNote { StartTime = 1000, Column = 2 }),
            HitResult.Great,
            [new BmsTimingObservation(BmsTimingObservationKind.Note, 1000, 988, 1, HitResult.Great)]);
        var gaugeEvent = new BmsGaugeHistoryEvent(1000, BmsGaugeType.Hard,
        [
            new BmsGaugeStateSnapshot(BmsGaugeType.Hard, 0.75, false),
        ]);
        BmsJudgementEventStore.Set(original.ScoreInfo, [judgementEvent]);
        BmsScoreGaugeHistoryStore.Set(original.ScoreInfo, [gaugeEvent]);

        var clone = original.DeepClone();

        Assert.Multiple(() =>
        {
            Assert.That(BmsJudgementEventStore.TryGet(clone.ScoreInfo, out var judgementEvents), Is.True);
            Assert.That(judgementEvents, Is.EqualTo([judgementEvent]));
            Assert.That(BmsScoreGaugeHistoryStore.TryGet(clone.ScoreInfo, out var gaugeHistory), Is.True);
            Assert.That(gaugeHistory, Has.Count.EqualTo(1));
            Assert.That(gaugeHistory[0].Time, Is.EqualTo(gaugeEvent.Time));
            Assert.That(gaugeHistory[0].ActiveGaugeType, Is.EqualTo(gaugeEvent.ActiveGaugeType));
            Assert.That(gaugeHistory[0].States, Is.EqualTo(gaugeEvent.States));
        });
    }

    [Test]
    public void TestArchiveIsGzipCompressed()
    {
        using var archive = BmsReplayArchive.Create(createScore());
        byte[] bytes = archive.Get(BmsReplayArchive.FILENAME);

        Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(2));
        Assert.That(bytes[0], Is.EqualTo((byte)0x1f));
        Assert.That(bytes[1], Is.EqualTo((byte)0x8b));
    }

    [Test]
    public void TestReadScoreHandlesLegacyPlainJsonArchive()
    {
        // Stand in for an archive written before the gzip switch: decompress a current archive
        // back to plain JSON, then hand that plain JSON to ReadScore. Verifies the gzip-magic
        // fallback so old scores/replays load instead of crashing.
        var original = createScore();
        BmsJudgementEventStore.Set(original.ScoreInfo,
        [
            new BmsJudgementEvent(
                BmsJudgementSource.From(new BmsNote { StartTime = 1000, Column = 2 }),
                HitResult.Great,
                [new BmsTimingObservation(BmsTimingObservationKind.Note, 1000, 988, 1, HitResult.Great)]),
            new BmsJudgementEvent(
                BmsJudgementSource.From(new HitObject { StartTime = 2000 }),
                HitResult.Miss,
                [new BmsTimingObservation(BmsTimingObservationKind.Note, 2000, 2000, 1, HitResult.Miss)]),
        ]);

        using var gzipArchive = BmsReplayArchive.Create(original);
        byte[] gzipBytes = gzipArchive.Get(BmsReplayArchive.FILENAME);

        byte[] plainJsonBytes;
        using (var decompressed = new MemoryStream())
        {
            using (var gz = new GZipStream(new MemoryStream(gzipBytes), CompressionMode.Decompress))
                gz.CopyTo(decompressed);
            plainJsonBytes = decompressed.ToArray();
        }

        // Plain JSON starts with '{', not the gzip magic 0x1f 0x8b — confirms the legacy path is taken.
        Assert.That(plainJsonBytes[0], Is.EqualTo((byte)'{'));

        var replayFile = new RealmFile { Hash = "abcdef" };
        var readTarget = new ScoreInfo();
        readTarget.Files.Add(new RealmNamedFileUsage(replayFile, BmsReplayArchive.FILENAME));

        var restored = BmsReplayArchive.ReadScore(
            readTarget,
            new TestResourceStore(replayFile.GetStoragePath(), plainJsonBytes));

        Assert.That(restored.Replay.Frames, Has.Count.EqualTo(1));
        Assert.That(restored.ScoreInfo.HitEvents, Has.Count.EqualTo(2));
        assertHitEvent(restored.ScoreInfo.HitEvents[0], -12, 1, HitResult.Great, typeof(BmsNote), 1000, 2);
        Assert.That(restored.ScoreInfo.HitEvents[1].Result, Is.EqualTo(HitResult.Miss));
        Assert.That(restored.ScoreInfo.HitEvents[1].HitObject, Is.TypeOf<HitObject>());
        Assert.That(restored.ScoreInfo.HitEvents[1].HitObject.StartTime, Is.EqualTo(2000));
    }

    [Test]
    public void TestReadScoreHandlesArchiveMissingHitEventsField()
    {
        // A pre-HitEvents archive (e.g. released 2026.624.3) has no hit_events field at all.
        // osu!'s DefaultValueHandling.IgnoreAndPopulate populates the absent field with the type
        // default (null), so ReadScore must degrade to "no hit events" instead of throwing
        // ArgumentNullException at .Select — this is the regression guard for that crash.
        byte[] legacyBytes = Encoding.UTF8.GetBytes("""{"version":1,"has_received_all_frames":true,"frames":[]}""");

        var replayFile = new RealmFile { Hash = "abcdef" };
        var readTarget = new ScoreInfo();
        readTarget.Files.Add(new RealmNamedFileUsage(replayFile, BmsReplayArchive.FILENAME));

        var restored = BmsReplayArchive.ReadScore(readTarget, new TestResourceStore(replayFile.GetStoragePath(), legacyBytes));

        Assert.That(restored.Replay.Frames, Is.Empty);
        Assert.That(restored.ScoreInfo.HitEvents, Is.Empty);
    }

    [Test]
    public void TestOldJudgementSchemaRestoresFramesWithoutStatistics()
    {
        var original = createScore();
        BmsJudgementEventStore.Set(original.ScoreInfo,
        [
            new BmsJudgementEvent(
                BmsJudgementSource.From(new BmsNote { StartTime = 1000, Column = 2 }),
                HitResult.Great,
                [new BmsTimingObservation(BmsTimingObservationKind.Note, 1000, 988, 1, HitResult.Great)]),
        ]);

        using var archive = BmsReplayArchive.Create(original);
        using var decompressed = new MemoryStream();
        using (var gz = new GZipStream(new MemoryStream(archive.Get(BmsReplayArchive.FILENAME)), CompressionMode.Decompress))
            gz.CopyTo(decompressed);

        var currentJson = Encoding.UTF8.GetString(decompressed.ToArray());
        var oldJson = currentJson.Replace("\"version\": 3", "\"version\": 2");
        Assert.That(oldJson, Is.Not.EqualTo(currentJson));

        var replayFile = new RealmFile { Hash = "abcdef" };
        var readTarget = new ScoreInfo();
        readTarget.Files.Add(new RealmNamedFileUsage(replayFile, BmsReplayArchive.FILENAME));

        var restored = BmsReplayArchive.ReadScore(
            readTarget,
            new TestResourceStore(replayFile.GetStoragePath(), Encoding.UTF8.GetBytes(oldJson)));

        Assert.Multiple(() =>
        {
            Assert.That(restored.Replay.Frames, Has.Count.EqualTo(1));
            Assert.That(restored.ScoreInfo.HitEvents, Is.Empty);
            Assert.That(BmsJudgementEventStore.TryGet(restored.ScoreInfo, out _), Is.False);
        });
    }

    [Test]
    public void TestReadScoreClearsStaleGaugeHistoryWhenArchiveHasNone()
    {
        using var archive = BmsReplayArchive.Create(createScore());
        var replayFile = new RealmFile { Hash = "abcdef" };
        var readTarget = new ScoreInfo();
        readTarget.Files.Add(new RealmNamedFileUsage(replayFile, BmsReplayArchive.FILENAME));
        BmsScoreGaugeHistoryStore.Set(readTarget,
        [
            new BmsGaugeHistoryEvent(1000, BmsGaugeType.Hard,
            [
                new BmsGaugeStateSnapshot(BmsGaugeType.Hard, 0, true),
            ]),
        ]);

        var restored = BmsReplayArchive.ReadScore(
            readTarget,
            new TestResourceStore(replayFile.GetStoragePath(), archive.Get(BmsReplayArchive.FILENAME)));

        Assert.That(BmsScoreGaugeHistoryStore.TryGet(restored.ScoreInfo, out var history) && history.Count > 0, Is.False);
    }

    [Test]
    public void TestReplayArchiveUsesOsrFilename()
    {
        using var archive = BmsReplayArchive.Create(createScore());

        Assert.That(archive.Filenames.Single(), Is.EqualTo("replay.osr"));
    }

    [Test]
    public void TestReplayHashUsesArchiveContent()
    {
        var first = createScore();
        var second = createScore();

        Assert.That(BmsReplayArchive.ComputeHash(first), Is.Not.Empty);
        Assert.That(BmsReplayArchive.ComputeHash(second), Is.EqualTo(BmsReplayArchive.ComputeHash(first)));

        ((BmsReplayFrame)second.Replay.Frames.Single()).Actions.Add(BmsAction.Key2);

        Assert.That(BmsReplayArchive.ComputeHash(second), Is.Not.EqualTo(BmsReplayArchive.ComputeHash(first)));
    }

    private static Score createScore() => new()
    {
        Replay =
        {
            Frames =
            {
                new BmsReplayFrame(1234, BmsAction.Key1),
            },
        },
    };

    private static void assertHitEvent(HitEvent hitEvent, double offset, double gameplayRate, HitResult result, Type hitObjectType, double startTime, int column)
    {
        Assert.That(hitEvent.TimeOffset, Is.EqualTo(offset));
        Assert.That(hitEvent.GameplayRate, Is.EqualTo(gameplayRate));
        Assert.That(hitEvent.Result, Is.EqualTo(result));
        Assert.That(hitEvent.HitObject, Is.TypeOf(hitObjectType));
        Assert.That(hitEvent.HitObject.StartTime, Is.EqualTo(startTime));
        Assert.That(((BmsHitObject)hitEvent.HitObject).Column, Is.EqualTo(column));
    }

    private sealed class TestResourceStore(string filename, byte[] content) : osu.Framework.IO.Stores.IResourceStore<byte[]>
    {
        public byte[] Get(string name) => name == filename ? content : null!;

        public Stream GetStream(string name) => new MemoryStream(Get(name));

        public System.Threading.Tasks.Task<byte[]> GetAsync(string name, System.Threading.CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(Get(name));

        public System.Collections.Generic.IEnumerable<string> GetAvailableResources() => [filename];

        public void Dispose()
        {
        }
    }
}
