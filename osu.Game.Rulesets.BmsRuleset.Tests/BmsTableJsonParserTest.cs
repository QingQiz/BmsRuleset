using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public class BmsTableJsonParserTest
{
    private const string sample_header = @"
{
  ""name"": ""Test Table"",
  ""symbol"": ""TT"",
  ""data_url"": ""data.json"",
  ""level_order"": [""☆1"", ""☆2"", ""★1"", ""★2""]
}";

    private const string sample_data = @"
[
  { ""level"": ""★1"", ""md5"": ""6940ad2ab7812fcbc1a26b83035b49f6"", ""title"": ""Song A"", ""artist"": ""Artist A"" },
  { ""level"": ""★2"", ""md5"": ""abc123def456abc123def456abc123de"", ""title"": ""Song B"", ""artist"": ""Artist B"" },
  { ""level"": ""☆1"", ""sha256"": ""130e67bfdbb1e1abd60d74b5a71466bd83c1c07127410114e8e18df572dee10a"", ""title"": ""Song C"", ""artist"": ""Artist C"" },
  { ""level"": ""☆2"", ""md5"": ""#N/A"", ""sha256"": ""aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"", ""title"": ""Song D"", ""artist"": ""Artist D"" },
  { ""level"": ""★1"", ""md5"": ""bad"", ""title"": ""Song E"", ""artist"": ""Artist E"" }
]";

    [Test]
    public void TestParseHeader()
    {
        var parser = new BmsTableJsonParser();
        var header = parser.ParseHeader(sample_header);

        Assert.That(header, Is.Not.Null);
        Assert.That(header!.Name, Is.EqualTo("Test Table"));
        Assert.That(header.Symbol, Is.EqualTo("TT"));
        Assert.That(header.DataUrl, Is.EqualTo("data.json"));
        Assert.That(header.LevelOrder, Is.EquivalentTo(new[] { "☆1", "☆2", "★1", "★2" }));
    }

    [Test]
    public void TestParseData()
    {
        var parser = new BmsTableJsonParser();
        var data = parser.ParseData(sample_data);

        Assert.That(data, Is.Not.Null);
        Assert.That(data, Has.Count.EqualTo(5));
    }

    [Test]
    public void TestMergeWithValidMd5()
    {
        var parser = new BmsTableJsonParser();
        var header = parser.ParseHeader(sample_header);
        var data = parser.ParseData(sample_data);

        var table = parser.Merge("test", TableSource.RemoteUrl, header, data);

        Assert.That(table, Is.Not.Null);
        Assert.That(table!.Name, Is.EqualTo("Test Table"));
        Assert.That(table.Symbol, Is.EqualTo("TT"));

        // Expect 4 entries: md5 valid (2), sha256 fallback (2), invalid md5 + no valid sha256 skipped (1)
        Assert.That(table.Entries, Has.Count.EqualTo(4));

        // Entry 0: valid md5 → stored as-is
        Assert.That(table.Entries[0].Md5Hash, Is.EqualTo("6940ad2ab7812fcbc1a26b83035b49f6"));
        Assert.That(table.Entries[0].Title, Is.EqualTo("Song A"));
        Assert.That(table.Entries[0].Level, Is.EqualTo("★1"));

        // Entry 1: valid md5
        Assert.That(table.Entries[1].Md5Hash, Is.EqualTo("abc123def456abc123def456abc123de"));

        // Entry 2: no md5, sha256 present → stored as sha256
        Assert.That(table.Entries[2].Md5Hash, Is.EqualTo("130e67bfdbb1e1abd60d74b5a71466bd83c1c07127410114e8e18df572dee10a"));
        Assert.That(table.Entries[2].Title, Is.EqualTo("Song C"));

        // Entry 3: md5 "#N/A" (invalid), sha256 valid → stored as sha256
        Assert.That(table.Entries[3].Md5Hash, Is.EqualTo("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
    }

    [Test]
    public void TestMergeWithEmptyDataReturnsNull()
    {
        var parser = new BmsTableJsonParser();
        var header = parser.ParseHeader(sample_header);

        var table = parser.Merge("test", TableSource.RemoteUrl, header, null);

        Assert.That(table, Is.Null);
    }

    [Test]
    public void TestMergeWithNoValidHashesReturnsEmptyTable()
    {
        const string bad_data = @"
[
  { ""level"": ""★1"", ""md5"": ""#N/A"", ""title"": ""A"" },
  { ""level"": ""★2"", ""md5"": """", ""sha256"": ""not-a-hex"", ""title"": ""B"" }
]";

        var parser = new BmsTableJsonParser();
        var header = parser.ParseHeader(sample_header);
        var data = parser.ParseData(bad_data);

        var table = parser.Merge("test", TableSource.RemoteUrl, header, data);

        // Table is created with 0 entries because no valid hashes
        Assert.That(table, Is.Not.Null);
        Assert.That(table!.Entries, Is.Empty);
    }

    [Test]
    public void TestIsValidMd5()
    {
        Assert.That(BmsTableJsonParser.IsValidMd5("6940ad2ab7812fcbc1a26b83035b49f6"), Is.True);
        Assert.That(BmsTableJsonParser.IsValidMd5("ABCDEFabcdef0123456789abcdef0123"), Is.True);
        Assert.That(BmsTableJsonParser.IsValidMd5("#N/A"), Is.False);
        Assert.That(BmsTableJsonParser.IsValidMd5(""), Is.False);
        Assert.That(BmsTableJsonParser.IsValidMd5(null), Is.False);
        Assert.That(BmsTableJsonParser.IsValidMd5("too-short"), Is.False);
        Assert.That(BmsTableJsonParser.IsValidMd5("not-a-valid-md5-hash-at-all-12345678"), Is.False);
    }

    [Test]
    public void TestIsValidSha256()
    {
        Assert.That(BmsTableJsonParser.IsValidSha256("130e67bfdbb1e1abd60d74b5a71466bd83c1c07127410114e8e18df572dee10a"), Is.True);
        Assert.That(BmsTableJsonParser.IsValidSha256("#N/A"), Is.False);
        Assert.That(BmsTableJsonParser.IsValidSha256(null), Is.False);
    }

    [Test]
    public void TestPickHashPrefersMd5OverSha256()
    {
        var hash = BmsTableJsonParser.PickHash(
            "6940ad2ab7812fcbc1a26b83035b49f6",
            "130e67bfdbb1e1abd60d74b5a71466bd83c1c07127410114e8e18df572dee10a");

        Assert.That(hash, Is.EqualTo("6940ad2ab7812fcbc1a26b83035b49f6"));
    }

    [Test]
    public void TestPickHashFallsBackToSha256()
    {
        var hash = BmsTableJsonParser.PickHash(
            "#N/A",
            "130e67bfdbb1e1abd60d74b5a71466bd83c1c07127410114e8e18df572dee10a");

        Assert.That(hash, Is.EqualTo("130e67bfdbb1e1abd60d74b5a71466bd83c1c07127410114e8e18df572dee10a"));
    }

    [Test]
    public void TestPickHashReturnsNullWhenBothInvalid()
    {
        Assert.That(BmsTableJsonParser.PickHash(null, null), Is.Null);
        Assert.That(BmsTableJsonParser.PickHash("#N/A", ""), Is.Null);
    }

    [Test]
    public void TestParseDataWithChartsWrapper()
    {
        const string wrapped = @"
{
  ""charts"": [
    { ""level"": ""★1"", ""md5"": ""aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"", ""title"": ""Wrapped"" }
  ]
}";

        var parser = new BmsTableJsonParser();
        var data = parser.ParseData(wrapped);

        Assert.That(data, Has.Count.EqualTo(1));
        Assert.That(data![0].Title, Is.EqualTo("Wrapped"));
    }

    [Test]
    public void TestMergeWithChartsWrapper()
    {
        const string combined = @"
{
  ""name"": ""Combined"",
  ""symbol"": ""CB"",
  ""level_order"": [""★1""],
  ""charts"": [
    { ""level"": ""★1"", ""md5"": ""aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"", ""title"": ""Combined Song"" }
  ]
}";

        var parser = new BmsTableJsonParser();
        var header = parser.ParseHeader(combined);
        var data = parser.ParseData(combined);

        var table = parser.Merge("combined", TableSource.LocalFile, header, data);

        Assert.That(table, Is.Not.Null);
        Assert.That(table!.Name, Is.EqualTo("Combined"));
        Assert.That(table.Entries, Has.Count.EqualTo(1));
        Assert.That(table.Entries[0].Md5Hash, Is.EqualTo("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
    }
}
