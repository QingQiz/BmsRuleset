using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using osu.Framework.Extensions;
using osu.Framework.IO.Stores;
using osu.Game.Extensions;
using osu.Game.IO.Archives;
using osu.Game.IO.Serialization;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.Replays;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;

public static class BmsReplayArchive
{
    public const string FILENAME = "replay.osr";

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
        var frames = operationFrames(score.Replay.Frames.OfType<BmsReplayFrame>()).ToArray();
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(new Payload
            {
                Algorithm = BmsReplayFrame.GetAlgorithm(score.Replay),
                Frames = frames.Select(frame => new InputFrame(frame.Time, frame.Actions.ToArray())).ToArray(),
            }.Serialize()));
        return stream.ToArray();
    }

    public static Score ReadScore(ScoreInfo scoreInfo, IResourceStore<byte[]> store)
    {
        var score = new Score { ScoreInfo = scoreInfo };
        var replayFile = scoreInfo.Files.FirstOrDefault(f => f.Filename == FILENAME);
        if (replayFile == null)
            return score;

        using var source = store.GetStream(replayFile.File.GetStoragePath());
        if (source == null)
            return score;

        using var bytes = new MemoryStream();
        source.CopyTo(bytes);
        if (bytes.Length == 0)
            return score;

        var data = bytes.ToArray();
        using Stream input = data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b
            ? new GZipStream(new MemoryStream(data), CompressionMode.Decompress)
            : new MemoryStream(data);
        using var reader = new StreamReader(input, Encoding.UTF8);
        var json = reader.ReadToEnd();
        BmsReplayFrame[] frames;
        if (json.TrimStart().StartsWith('['))
            frames = (json.Deserialize<List<InputFrame?>>() ?? []).OfType<InputFrame>()
                .Select(frame => new BmsReplayFrame(frame.Time, frame.Actions ?? [])).ToArray();
        else
        {
            var payload = json.Deserialize<Payload>() ?? new Payload();
            // Old archives contribute input only; their result ledgers are never restored.
            frames = (payload.Frames ?? []).OfType<InputFrame>().Where(frame => frame.Time != -1000000000)
                .Select(frame => new BmsReplayFrame(frame.Time, frame.Actions ?? [])).ToArray();
            if (frames.Length > 0)
                frames[0] = new BmsReplayFrame(frames[0].Time, frames[0].Actions.ToArray()) { Algorithm = payload.Algorithm };
            if (frames.Length > 0 && payload.LegacyFrameTimes is { Length: > 0 } times && times[^1] != frames[^1].Time)
                frames = [.. frames, new BmsReplayFrame(times[^1], frames[^1].Actions.ToArray())];
        }

        score.Replay = new Replay
        {
            Frames = operationFrames(frames).Cast<ReplayFrame>().ToList(),
        };
        scoreInfo.HitEvents = [];
        return score;
    }

    private static IEnumerable<BmsReplayFrame> operationFrames(IEnumerable<BmsReplayFrame> frames)
    {
        var inputs = frames.OrderBy(frame => frame.Time).ToArray();
        BmsReplayFrame? previous = null;
        for (var i = 0; i < inputs.Length; i++)
        {
            var frame = inputs[i];
            if (!double.IsFinite(frame.Time))
                throw new InvalidDataException("Invalid BMS replay timestamp.");

            if (previous == null || i + 1 == inputs.Length
                                 || frame.Actions.Count != previous.Actions.Count || !frame.Actions.All(previous.Actions.Contains))
                yield return frame;

            previous = frame;
        }
    }

    private sealed record InputFrame(double Time, BmsAction[]? Actions);

    private class Payload
    {
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
        public BmsJudgementAlgorithm Algorithm { get; init; }

        public InputFrame?[]? Frames { get; init; }

        // Only the old recording's endpoint is needed; intermediate samples are never restored.
        [JsonProperty("frame_times")]
        public double[]? LegacyFrameTimes { get; init; }
    }
}
