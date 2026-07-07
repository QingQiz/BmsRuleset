using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using osu.Framework.Extensions;
using osu.Framework.IO.Stores;
using osu.Game.Extensions;
using osu.Game.IO.Archives;
using osu.Game.IO.Serialization;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Replays;

public static class BmsReplayArchive
{
    public const string FILENAME = "replay.osr";

    // New archives are gzip-compressed JSON; old archives are plain JSON (which starts with '{').
    // The reader tells them apart by gzip's two magic bytes, so pre-gzip scores/replays still
    // load instead of crashing — no version handshake needed.
    private const byte gzip_magic_1 = 0x1f;
    private const byte gzip_magic_2 = 0x8b;

    public static ArchiveReader Create(Score score)
        => new ByteArrayArchiveReader(createReplayData(score), FILENAME);

    public static string ComputeHash(Score score)
    {
        using var stream = new MemoryStream(createReplayData(score));
        return stream.ComputeSHA2Hash();
    }

    private static byte[] createReplayData(Score score)
    {
        byte[] json = serializePayload(score);

        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(json, 0, json.Length);

        return ms.ToArray();
    }

    private static byte[] serializePayload(Score score)
    {
        var payload = new Payload
        {
            HasReceivedAllFrames = score.Replay.HasReceivedAllFrames,
            Frames = score.Replay.Frames.OfType<BmsReplayFrame>().ToList(),
            // Empty POORs carry a base HitObject (no BmsHitObject), so they must round-trip too —
            // dropping them would silently under-count gauge damage in restored statistics.
            HitEvents = score.ScoreInfo.HitEvents.Select(HitEventData.From).ToList(),
        };

        return Encoding.UTF8.GetBytes(payload.Serialize());
    }

    public static Score ReadScore(ScoreInfo scoreInfo, IResourceStore<byte[]> store)
    {
        var score = new Score
        {
            ScoreInfo = scoreInfo,
        };

        var replayFile = scoreInfo.Files.FirstOrDefault(f => f.Filename == FILENAME);

        if (replayFile == null)
            return score;

        using var stream = store.GetStream(replayFile.File.GetStoragePath());

        if (stream == null)
            return score;

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        byte[] bytes = ms.ToArray();

        if (bytes.Length == 0)
            return score;

        // gzip-wrapped JSON for new archives; plain JSON for archives written before the gzip
        // switch. Falling back keeps old scores/replays working instead of throwing.
        Stream payloadStream = bytes.Length >= 2 && bytes[0] == gzip_magic_1 && bytes[1] == gzip_magic_2
            ? new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress)
            : new MemoryStream(bytes);

        Payload payload;

        using (payloadStream)
        using (var reader = new StreamReader(payloadStream, Encoding.UTF8))
            payload = reader.ReadToEnd().Deserialize<Payload>() ?? new Payload();

        score.Replay = new Replay
        {
            HasReceivedAllFrames = payload.HasReceivedAllFrames,
            // Frames/HitEvents default to [] on the Payload, but osu!'s serializer uses
            // DefaultValueHandling.IgnoreAndPopulate, which populates an absent field with the
            // type's default (null for List<>) — overriding the = [] initialiser. Pre-HitEvents
            // archives omit hit_events entirely, so guard: missing field ⇒ no frames/hit events,
            // not a crash.
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            Frames = (payload.Frames ?? []).Cast<osu.Game.Rulesets.Replays.ReplayFrame>().ToList(),
        };
        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        score.ScoreInfo.HitEvents = (payload.HitEvents ?? []).Select(e => e.ToHitEvent()).ToList();

        return score;
    }

    private class Payload
    {
        // ReSharper disable once UnusedMember.Local
        public int Version { get; set; } = 1;

        public bool HasReceivedAllFrames { get; init; } = true;

        public List<BmsReplayFrame> Frames { get; init; } = [];

        public List<HitEventData> HitEvents { get; init; } = [];
    }

    private class HitEventData
    {
        public double TimeOffset { get; init; }

        public double? GameplayRate { get; init; }

        public HitResult Result { get; init; }

        public HitObjectData HitObject { get; init; } = new();

        public static HitEventData From(HitEvent hitEvent) => new()
        {
            TimeOffset = hitEvent.TimeOffset,
            GameplayRate = hitEvent.GameplayRate,
            Result = hitEvent.Result,
            HitObject = HitObjectData.From(hitEvent.HitObject),
        };

        public HitEvent ToHitEvent() => new(TimeOffset, GameplayRate, Result, HitObject.ToHitObject(), null, null);
    }

    private class HitObjectData
    {
        public double StartTime { get; init; }

        public int Column { get; init; }

        public HitObjectKind Kind { get; init; }

        public double Duration { get; init; }

        public double LandmineDamagePercent { get; init; }

        public static HitObjectData From(HitObject hitObject) => new()
        {
            StartTime = hitObject.StartTime,
            Column = hitObject is BmsHitObject bms ? bms.Column : 0,
            Kind = hitObject switch
            {
                BmsLandmine => HitObjectKind.Landmine,
                BmsLongNote => HitObjectKind.LongNote,
                BmsHitObject => HitObjectKind.Note,
                _ => HitObjectKind.EmptyPoor,
            },
            Duration = hitObject is BmsLongNote longNote ? longNote.Duration : 0,
            LandmineDamagePercent = hitObject is BmsLandmine landmine ? landmine.LandmineDamagePercent : 0,
        };

        public HitObject ToHitObject()
        {
            HitObject hitObject = Kind switch
            {
                HitObjectKind.LongNote => new BmsLongNote { Duration = Duration },
                HitObjectKind.Landmine => new BmsLandmine { LandmineDamagePercent = LandmineDamagePercent },
                HitObjectKind.Note => new BmsNote(),
                // Empty POOR: a synthetic base HitObject with only a press time — no column/kind.
                _ => new HitObject(),
            };

            hitObject.StartTime = StartTime;

            if (hitObject is BmsHitObject bms)
                bms.Column = Column;

            return hitObject;
        }
    }

    private enum HitObjectKind
    {
        Note,
        LongNote,
        Landmine,
        EmptyPoor,
    }
}
