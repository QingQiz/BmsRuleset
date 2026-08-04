using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.IO.ResourceStore;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.IO;

[TestFixture]
public class BmsFileResourceStoreTest
{
    private string directory = null!;

    [SetUp]
    public void SetUp()
    {
        directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"bms-resource-store-{Guid.NewGuid()}");
        Directory.CreateDirectory(directory);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(directory, true);
    }

    [Test]
    public void TestResolvesResourceFromMixedEncodingChart()
    {
        const string actual_name = "新宝島 - ハロー、ハッピーワールド!.mp3";
        var actualPath = Path.Combine(directory, actual_name);
        File.WriteAllBytes(actualPath, [1, 2, 3]);

        var shiftJis = CodePagesEncodingProvider.Instance.GetEncoding(932)!;
        var gbk = CodePagesEncodingProvider.Instance.GetEncoding(936)!;
        byte[] chart = [.. shiftJis.GetBytes("#TITLE 新宝島\r\n#WAV02 "), .. gbk.GetBytes(actual_name)];
        var parsedName = BmsChartParser.ReadAllLines(chart).Single(line => line.StartsWith("#WAV02", StringComparison.Ordinal))[7..];

        using var store = new BmsFileResourceStore(directory);

        Assert.Multiple(() =>
        {
            Assert.That(parsedName, Is.Not.EqualTo(actual_name));
            Assert.That(store.TryResolve(parsedName, out var resolvedPath), Is.True);
            Assert.That(resolvedPath, Is.EqualTo(actualPath));
            Assert.That(store.Get(parsedName), Is.EqualTo(new byte[] { 1, 2, 3 }));
        });
    }

    [Test]
    public void TestExactFilenameTakesPriorityOverAlias()
    {
        const string actual_name = "新宝島.mp3";
        var mojibakeName = decodeGbkAsShiftJis(actual_name);
        var actualPath = Path.Combine(directory, actual_name);
        var mojibakePath = Path.Combine(directory, mojibakeName);
        File.WriteAllBytes(actualPath, [1]);
        File.WriteAllBytes(mojibakePath, [2]);

        using var store = new BmsFileResourceStore(directory);

        Assert.Multiple(() =>
        {
            Assert.That(store.TryResolve(mojibakeName, out var resolvedPath), Is.True);
            Assert.That(resolvedPath, Is.EqualTo(mojibakePath));
            Assert.That(store.Get(mojibakeName), Is.EqualTo(new byte[] { 2 }));
        });
    }

    [Test]
    public void TestUnknownFilenameDoesNotResolve()
    {
        File.WriteAllBytes(Path.Combine(directory, "sample.mp3"), [1]);

        using var store = new BmsFileResourceStore(directory);

        Assert.That(store.TryResolve("missing.mp3", out _), Is.False);
    }

    [Test]
    public void TestAmbiguousAliasDoesNotResolve()
    {
        const string first_name = "万.mp3";
        const string second_name = "丸.mp3";
        var alias = decodeGbkAsShiftJis(first_name);
        File.WriteAllBytes(Path.Combine(directory, first_name), [1]);
        File.WriteAllBytes(Path.Combine(directory, second_name), [2]);

        using var store = new BmsFileResourceStore(directory);

        Assert.Multiple(() =>
        {
            Assert.That(decodeGbkAsShiftJis(second_name), Is.EqualTo(alias));
            Assert.That(store.TryResolve(alias, out _), Is.False);
        });
    }

    private static string decodeGbkAsShiftJis(string value)
    {
        var gbk = CodePagesEncodingProvider.Instance.GetEncoding(936)!;
        var shiftJis = CodePagesEncodingProvider.Instance.GetEncoding(932)!;
        return shiftJis.GetString(gbk.GetBytes(value));
    }
}
