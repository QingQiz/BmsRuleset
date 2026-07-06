using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Extensions;
using osu.Game.Models;
using osu.Game.Rulesets.BmsRuleset.Replays;
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
        original.ScoreInfo.Files.Add(new RealmNamedFileUsage(replayFile, BmsReplayArchive.FILENAME));

        var restored = BmsReplayArchive.ReadScore(
            original.ScoreInfo,
            new TestResourceStore(replayFile.GetStoragePath(), archive.Get(BmsReplayArchive.FILENAME)));

        var frames = restored.Replay.Frames.Cast<BmsReplayFrame>().ToArray();

        Assert.That(frames, Has.Length.EqualTo(2));
        Assert.That(frames[0].IsEquivalentTo(original.Replay.Frames[0]), Is.True);
        Assert.That(frames[1].IsEquivalentTo(original.Replay.Frames[1]), Is.True);
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
