using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.IO.ResourceStore;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal;

[TestFixture]
public class BmsAudioResourceStoreTest
{
    private string directory = null!;

    [SetUp]
    public void SetUp() => directory = Directory.CreateTempSubdirectory("bms-audio-resources-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(directory, true);

    [Test]
    public void ExtensionsFollowFallbackOrder()
    {
        string[] expected = ["wav", "flac", "ogg", "mp3", "m4a"];
        Assert.That(BmsAudioResourceStore.Extensions, Is.EqualTo(expected));
    }

    [TestCase("samples/note.ogg", "samples/note.ogg")]
    [TestCase("samples/note.mp3", "samples/note.wav")]
    [TestCase("samples/note", "samples/note")]
    [TestCase("missing/note.ogg", "note.ogg")]
    public void DeclaredPathAndFolderFallbacksKeepTheirPriority(string definition, string expected)
    {
        Directory.CreateDirectory(Path.Combine(directory, "samples"));
        foreach (var path in new[] { "samples/note.ogg", "samples/note.wav", "samples/note", "note.ogg", "note.wav" })
            File.WriteAllBytes(Path.Combine(directory, path), [1]);

        using var store = new BmsAudioResourceStore(directory);
        Assert.That(store.ResolveSamplePath(definition), Is.EqualTo(Path.GetFullPath(Path.Combine(directory, expected))));
    }

    [Test]
    public void MixedEncodingFilenameStillResolvesWithAnAlternateExtension()
    {
        const string actual_name = "新宝島.flac";
        var actualPath = Path.Combine(directory, actual_name);
        File.WriteAllBytes(actualPath, [1]);
        var gbk = CodePagesEncodingProvider.Instance.GetEncoding(936)!;
        var shiftJis = CodePagesEncodingProvider.Instance.GetEncoding(932)!;
        var definition = shiftJis.GetString(gbk.GetBytes("新宝島.wav"));

        using var store = new BmsAudioResourceStore(directory);
        Assert.That(store.ResolveSamplePath(definition), Is.EqualTo(actualPath));
    }

    [Test]
    public void EachAudioStoreBuildsItsOwnDirectorySnapshot()
    {
        var original = Path.Combine(directory, "original.wav");
        var added = Path.Combine(directory, "added.wav");
        File.WriteAllBytes(original, [1]);
        using var previous = new BmsAudioResourceStore(directory);
        Assert.That(previous.ResolveSamplePath("original.wav"), Is.EqualTo(original));
        Assert.That(previous.ResolveSamplePath("added.wav"), Is.Null);

        File.Delete(original);
        File.WriteAllBytes(added, [2]);
        using var next = new BmsAudioResourceStore(directory);
        Assert.Multiple(() =>
        {
            Assert.That(previous.ResolveSamplePath("added.wav"), Is.Null);
            Assert.That(next.ResolveSamplePath("original.wav"), Is.Null);
            Assert.That(next.ResolveSamplePath("added.wav"), Is.EqualTo(added));
        });
    }

    [TestCase("samples/../note.wav", "note.wav")]
    [TestCase("samples/note.wav", "samples/note.wav")]
    public void IndexedPathsRemainDistinctAndNormalised(string definition, string expected)
    {
        Directory.CreateDirectory(Path.Combine(directory, "samples"));
        File.WriteAllBytes(Path.Combine(directory, "note.wav"), [1]);
        File.WriteAllBytes(Path.Combine(directory, "samples", "note.wav"), [2]);
        using var store = new BmsAudioResourceStore(directory);
        var expectedPath = Path.GetFullPath(Path.Combine(directory, expected));
        Assert.That(store.ResolveSamplePath(definition), Is.EqualTo(expectedPath));
        Assert.That(store.ResolveSamplePath(expectedPath), Is.EqualTo(expectedPath));
        Assert.That(store.ResolveSamplePath(Path.Combine(directory, "..", "outside.wav")), Is.Null);
    }

    [Test]
    public void ExactFilenameStillTakesPriorityOverAnEncodingAlias()
    {
        const string original_name = "新宝島.wav";
        var alias = decodeGbkAsShiftJis(original_name);
        File.WriteAllBytes(Path.Combine(directory, original_name), [1]);
        File.WriteAllBytes(Path.Combine(directory, alias), [2]);
        using var store = new BmsAudioResourceStore(directory);
        Assert.That(store.ResolveSamplePath(alias), Is.EqualTo(Path.Combine(directory, alias)));
    }

    [Test]
    public void AmbiguousEncodingAliasRemainsUnavailable()
    {
        File.WriteAllBytes(Path.Combine(directory, "万.wav"), [1]);
        File.WriteAllBytes(Path.Combine(directory, "丸.wav"), [2]);
        using var store = new BmsAudioResourceStore(directory);
        var alias = decodeGbkAsShiftJis("万.wav");
        Assert.That(alias, Is.EqualTo(decodeGbkAsShiftJis("丸.wav")));
        Assert.That(store.ResolveSamplePath(alias), Is.Null);
    }

    [Test]
    public void FilenameCaseMatchesTheFilesystem()
    {
        var path = Path.Combine(directory, "note.wav");
        File.WriteAllBytes(path, [1]);
        using var store = new BmsAudioResourceStore(directory);
        var variant = Path.Combine(directory, "NOTE.WAV");
        Assert.That(store.ResolveSamplePath("NOTE.WAV"), Is.EqualTo(File.Exists(variant) ? variant : null));
    }

    [Test]
    public void CaseDistinctFilesRetainTheirOwnContents()
    {
        var lower = Path.Combine(directory, "note.wav");
        var upper = Path.Combine(directory, "NOTE.WAV");
        File.WriteAllBytes(lower, [1]);
        File.WriteAllBytes(upper, [2]);
        using var store = new BmsAudioResourceStore(directory);
        Assert.That(File.ReadAllBytes(store.ResolveSamplePath("note.wav")!), Is.EqualTo(File.ReadAllBytes(lower)));
        Assert.That(File.ReadAllBytes(store.ResolveSamplePath("NOTE.WAV")!), Is.EqualTo(File.ReadAllBytes(upper)));
    }

    [Test]
    public async Task ConcurrentAudioRequestsSeeTheCompletedIndex()
    {
        var path = Path.Combine(directory, "note.wav");
        File.WriteAllBytes(path, [1]);
        using var store = new BmsAudioResourceStore(directory);
        var paths = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => store.ResolveSamplePath("note.wav"))));
        Assert.That(paths, Is.All.EqualTo(path));
    }

    [Test]
    public void CancelledPreparationDoesNotPublishAnIndex()
    {
        File.WriteAllBytes(Path.Combine(directory, "note.wav"), [1]);
        using var cancellation = new CancellationTokenSource();
        using var store = new BmsAudioResourceStore(directory, cancellation.Token);
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => store.ResolveSamplePath("note.wav"));
    }

    [Test]
    public void LinkedDirectoryResourcesRemainReadable()
    {
        var samples = Directory.CreateDirectory(Path.Combine(directory, "samples")).FullName;
        File.WriteAllBytes(Path.Combine(samples, "note.wav"), [1]);
        var linked = Path.Combine(directory, "linked");
        try
        {
            Directory.CreateSymbolicLink(linked, samples);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore($"Symbolic links are unavailable: {exception.Message}");
        }

        using var store = new BmsAudioResourceStore(directory);
        Assert.That(File.ReadAllBytes(store.ResolveSamplePath(Path.Combine("linked", "note.wav"))!), Is.EqualTo(new byte[] { 1 }));
    }

    private static string decodeGbkAsShiftJis(string name) => CodePagesEncodingProvider.Instance.GetEncoding(932)!
        .GetString(CodePagesEncodingProvider.Instance.GetEncoding(936)!.GetBytes(name));
}
