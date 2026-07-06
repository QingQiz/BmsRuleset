using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using osu.Framework.Extensions;
using osu.Framework.IO.Stores;
using osu.Game.Extensions;
using osu.Game.IO.Archives;
using osu.Game.IO.Serialization;
using osu.Game.Replays;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Replays;

public static class BmsReplayArchive
{
    public const string FILENAME = "replay.osr";

    public static ArchiveReader Create(Score score)
        => new ByteArrayArchiveReader(createReplayData(score), FILENAME);

    public static string ComputeHash(Score score)
    {
        using var stream = new MemoryStream(createReplayData(score));
        return stream.ComputeSHA2Hash();
    }

    private static byte[] createReplayData(Score score)
    {
        var payload = new Payload
        {
            HasReceivedAllFrames = score.Replay.HasReceivedAllFrames,
            Frames = score.Replay.Frames.OfType<BmsReplayFrame>().ToList(),
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

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var payload = reader.ReadToEnd().Deserialize<Payload>();

        score.Replay = new Replay
        {
            HasReceivedAllFrames = payload.HasReceivedAllFrames,
            Frames = payload.Frames.Cast<osu.Game.Rulesets.Replays.ReplayFrame>().ToList(),
        };

        return score;
    }

    private class Payload
    {
        // ReSharper disable once UnusedMember.Local
        public int Version { get; set; } = 1;

        public bool HasReceivedAllFrames { get; init; } = true;

        public List<BmsReplayFrame> Frames { get; init; } = [];
    }
}
