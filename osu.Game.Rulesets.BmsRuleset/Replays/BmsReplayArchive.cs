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
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
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
    private const int current_version = 3;

    public static ArchiveReader Create(Score score)
        => new ByteArrayArchiveReader(createReplayData(score), FILENAME);

    public static ArchiveReader Create(Score score, out string hash)
    {
        var replayData = createReplayData(score);
        hash = computeHash(replayData);
        return new ByteArrayArchiveReader(replayData, FILENAME);
    }

    public static string ComputeHash(Score score) => computeHash(createReplayData(score));

    private static string computeHash(byte[] replayData)
    {
        using var stream = new MemoryStream(replayData);
        return stream.ComputeSHA2Hash();
    }

    private static byte[] createReplayData(Score score)
    {
        var json = serializePayload(score);

        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(json, 0, json.Length);

        return ms.ToArray();
    }

    private static byte[] serializePayload(Score score)
    {
        BmsScoreGaugeHistoryStore.TryGet(score.ScoreInfo, out var gaugeHistory);
        BmsJudgementEventStore.TryGet(score.ScoreInfo, out var judgementEvents);

        var payload = new Payload
        {
            HasReceivedAllFrames = score.Replay.HasReceivedAllFrames,
            Frames = score.Replay.Frames.OfType<BmsReplayFrame>().ToList(),
            JudgementEvents = judgementEvents.Select(JudgementEventData.From).ToList(),
            GaugeHistory = gaugeHistory.Select(GaugeHistoryEventData.From).ToList(),
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
        var bytes = ms.ToArray();

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
            // Frames default to [] on the Payload, but osu!'s serializer uses
            // DefaultValueHandling.IgnoreAndPopulate, which populates an absent field with the
            // type's default (null for List<>) — overriding the = [] initialiser. Older archives
            // may omit frames entirely, so guard against the serializer replacing the
            // collection initialiser with null.
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            Frames = (payload.Frames ?? []).Cast<osu.Game.Rulesets.Replays.ReplayFrame>().ToList(),
        };

        if (payload.Version == current_version)
        {
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            var judgementEvents = (payload.JudgementEvents ?? []).Select(e => e.ToJudgementEvent()).ToArray();
            score.ScoreInfo.HitEvents = BmsJudgementEventProjection.CreateTimingHitEvents(judgementEvents);
            BmsJudgementEventStore.Set(score.ScoreInfo, judgementEvents);

            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            var gaugeHistory = (payload.GaugeHistory ?? []).Select(e => e.ToGaugeHistoryEvent()).ToArray();
            if (gaugeHistory.Length > 0)
                BmsScoreGaugeHistoryStore.Set(score.ScoreInfo, gaugeHistory);
            else
                BmsScoreGaugeHistoryStore.Clear(score.ScoreInfo);
        }
        else
        {
            // Input frames remain useful across schema changes; derived statistics are rebuilt
            // by replay playback instead of guessing at a no-longer-compatible event model.
            score.ScoreInfo.HitEvents = [];
            BmsJudgementEventStore.Clear(score.ScoreInfo);
            BmsScoreGaugeHistoryStore.Clear(score.ScoreInfo);
        }

        return score;
    }

    private class Payload
    {
        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Local
        public int Version { get; set; } = current_version;

        public bool HasReceivedAllFrames { get; init; } = true;

        public List<BmsReplayFrame> Frames { get; init; } = [];

        public List<JudgementEventData> JudgementEvents { get; init; } = [];

        public List<GaugeHistoryEventData> GaugeHistory { get; init; } = [];
    }

    private class JudgementEventData
    {
        public HitResult Result { get; init; }

        public JudgementSourceData Source { get; init; } = new();

        public List<TimingObservationData> TimingObservations { get; init; } = [];

        public static JudgementEventData From(BmsJudgementEvent judgementEvent) => new()
        {
            Result = judgementEvent.Result,
            Source = JudgementSourceData.From(judgementEvent.Source),
            TimingObservations = judgementEvent.TimingObservations.Select(TimingObservationData.From).ToList(),
        };

        public BmsJudgementEvent ToJudgementEvent() => new(
            Source.ToJudgementSource(),
            Result,
            TimingObservations.Select(observation => observation.ToTimingObservation()));
    }

    private class TimingObservationData
    {
        public BmsTimingObservationKind Kind { get; init; }

        public double ExpectedTime { get; init; }

        public double ActualTime { get; init; }

        public double? GameplayRate { get; init; }

        public HitResult Result { get; init; }

        public static TimingObservationData From(BmsTimingObservation observation) => new()
        {
            Kind = observation.Kind,
            ExpectedTime = observation.ExpectedTime,
            ActualTime = observation.ActualTime,
            GameplayRate = observation.GameplayRate,
            Result = observation.Result,
        };

        public BmsTimingObservation ToTimingObservation() => new(
            Kind,
            ExpectedTime,
            ActualTime,
            GameplayRate,
            Result);
    }

    private class GaugeHistoryEventData
    {
        public double Time { get; init; }

        public BmsGaugeType ActiveGaugeType { get; init; }

        public List<GaugeStateData> States { get; init; } = [];

        public static GaugeHistoryEventData From(BmsGaugeHistoryEvent gaugeEvent) => new()
        {
            Time = gaugeEvent.Time,
            ActiveGaugeType = gaugeEvent.ActiveGaugeType,
            States = gaugeEvent.States.Select(GaugeStateData.From).ToList(),
        };

        public BmsGaugeHistoryEvent ToGaugeHistoryEvent() => new(
            Time,
            ActiveGaugeType,
            States.Select(state => state.ToGaugeStateSnapshot()).ToArray());
    }

    private class GaugeStateData
    {
        public BmsGaugeType GaugeType { get; init; }

        public double Health { get; init; }

        public bool Failed { get; init; }

        public static GaugeStateData From(BmsGaugeStateSnapshot state) => new()
        {
            GaugeType = state.GaugeType,
            Health = state.Health,
            Failed = state.Failed,
        };

        public BmsGaugeStateSnapshot ToGaugeStateSnapshot() => new(GaugeType, Health, Failed);
    }

    private class JudgementSourceData
    {
        public double StartTime { get; init; }

        public int Column { get; init; }

        public BmsJudgementSourceKind Kind { get; init; }

        public double Duration { get; init; }

        public double LandmineDamagePercent { get; init; }

        public static JudgementSourceData From(BmsJudgementSource source) => new()
        {
            StartTime = source.StartTime,
            Column = source.Column,
            Kind = source.Kind,
            Duration = source.Duration,
            LandmineDamagePercent = source.LandmineDamagePercent,
        };

        public BmsJudgementSource ToJudgementSource() => new(
            StartTime,
            Column,
            Kind,
            Duration,
            LandmineDamagePercent);
    }

}
