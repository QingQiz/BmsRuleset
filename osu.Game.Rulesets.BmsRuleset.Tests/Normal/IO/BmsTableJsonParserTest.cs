using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.IO;

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

    private const string combined_json = @"
{
  ""name"": ""Combined"",
  ""symbol"": ""CB"",
  ""level_order"": [""★1""],
  ""charts"": [
    { ""level"": ""★1"", ""md5"": ""aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"", ""title"": ""Combined Song"" }
  ]
}";

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
    public void TestMergeWithChartsWrapper()
    {
        var result = BmsTableJsonParser.Parse(combined_json);

        Assert.That(result, Is.Not.Null);
        var table = BmsTableJsonParser.Merge("combined", TableSource.LocalFile, result!.Header, result.Charts);

        Assert.That(table, Is.Not.Null);
        Assert.That(table!.Name, Is.EqualTo("Combined"));
        Assert.That(table.Entries, Has.Count.EqualTo(1));
        Assert.That(table.Entries[0].Md5Hash, Is.EqualTo("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
        string[] expectedOrder = ["★1"];
        Assert.That(table.LevelOrder, Is.EqualTo(expectedOrder));
    }

    [Test]
    public void TestMergeInfersMissingLevelOrder()
    {
        var table = BmsTableJsonParser.Merge("fallback", TableSource.LocalFile,
            new RawTableData { Name = "Fallback" },
            [
                new RawChartItem { Level = "beta", Md5 = "00000000000000000000000000000001" },
                new RawChartItem { Level = "st10", Md5 = "00000000000000000000000000000002" },
                new RawChartItem { Level = "2", Md5 = "00000000000000000000000000000003" },
                new RawChartItem { Level = "st2", Md5 = "00000000000000000000000000000004" },
                new RawChartItem { Level = "alpha", Md5 = "00000000000000000000000000000005" },
                new RawChartItem { Level = "1", Md5 = "00000000000000000000000000000006" },
                new RawChartItem { Level = "ST2", Md5 = "00000000000000000000000000000007" },
                new RawChartItem { Level = "1.5", Md5 = "00000000000000000000000000000008" },
                new RawChartItem { Level = "st1.25", Md5 = "00000000000000000000000000000009" },
                new RawChartItem { Level = "st-2", Md5 = "0000000000000000000000000000000a" },
                new RawChartItem { Level = "st.5", Md5 = "0000000000000000000000000000000b" },
                new RawChartItem { Level = "ignored", Md5 = "invalid" },
            ]);

        Assert.That(table, Is.Not.Null);
        string[] expectedOrder = ["st-2", "st.5", "1", "st1.25", "1.5", "2", "st2", "st10", "alpha", "beta"];
        Assert.That(table!.LevelOrder, Is.EqualTo(expectedOrder));
    }

    [Test]
    public void TestMergeWithEmptyDataReturnsNull()
    {
        var headerResult = BmsTableJsonParser.Parse(sample_header);

        var table = BmsTableJsonParser.Merge("test", TableSource.RemoteUrl, headerResult!.Header, null);

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

        var headerResult = BmsTableJsonParser.Parse(sample_header);
        var dataResult = BmsTableJsonParser.Parse(bad_data);

        var table = BmsTableJsonParser.Merge("test", TableSource.RemoteUrl, headerResult!.Header, dataResult!.Charts);

        // Table is created with 0 entries because no valid hashes
        Assert.That(table, Is.Not.Null);
        Assert.That(table!.Entries, Is.Empty);
    }

    [Test]
    public void TestMergeWithValidMd5()
    {
        var headerResult = BmsTableJsonParser.Parse(sample_header);
        var dataResult = BmsTableJsonParser.Parse(sample_data);

        var table = BmsTableJsonParser.Merge("test", TableSource.RemoteUrl, headerResult!.Header, dataResult!.Charts);

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
    public void TestParseChartsFromCombined()
    {
        var result = BmsTableJsonParser.Parse(combined_json);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Charts, Is.Not.Null);
        Assert.That(result.Charts, Has.Count.EqualTo(1));
        Assert.That(result.Charts![0].Title, Is.EqualTo("Combined Song"));
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

        var result = BmsTableJsonParser.Parse(wrapped);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Charts, Is.Not.Null);
        Assert.That(result.Charts, Has.Count.EqualTo(1));
        Assert.That(result.Charts![0].Title, Is.EqualTo("Wrapped"));
    }

    [Test]
    public void TestParseHeaderFromCombined()
    {
        var result = BmsTableJsonParser.Parse(combined_json);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Header, Is.Not.Null);
        Assert.That(result.Header!.Name, Is.EqualTo("Combined"));
        Assert.That(result.Header.Symbol, Is.EqualTo("CB"));
        Assert.That(result.Header.LevelOrder, Is.EquivalentTo(["★1"]));
    }

    [Test]
    public void TestParseInvalidJsonReturnsNull()
    {
        var result = BmsTableJsonParser.Parse("not json");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TestParseOnlyDataReturnsNoHeader()
    {
        var result = BmsTableJsonParser.Parse(sample_data);

        // Plain array JSON: header is null, charts are parsed
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Header, Is.Null);
        Assert.That(result.Charts, Is.Not.Null);
        Assert.That(result.Charts, Has.Count.EqualTo(5));
    }

    [Test]
    public void TestParseOnlyHeaderReturnsNoCharts()
    {
        var result = BmsTableJsonParser.Parse(sample_header);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Header, Is.Not.Null);
        Assert.That(result.Header!.Name, Is.EqualTo("Test Table"));
        // No charts array, header has data_url -> charts null is expected
        Assert.That(result.Charts, Is.Null);
    }

    [Test]
    public void TestParseResultProperties()
    {
        var result = BmsTableJsonParser.Parse(combined_json);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Header, Is.Not.Null);
        Assert.That(result.Charts, Is.Not.Null);
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
    public void TestPickHashPrefersMd5OverSha256()
    {
        var hash = BmsTableJsonParser.PickHash(
            "6940ad2ab7812fcbc1a26b83035b49f6",
            "130e67bfdbb1e1abd60d74b5a71466bd83c1c07127410114e8e18df572dee10a");

        Assert.That(hash, Is.EqualTo("6940ad2ab7812fcbc1a26b83035b49f6"));
    }

    [Test]
    public void TestPickHashReturnsNullWhenBothInvalid()
    {
        Assert.That(BmsTableJsonParser.PickHash(null, null), Is.Null);
        Assert.That(BmsTableJsonParser.PickHash("#N/A", ""), Is.Null);
    }
}
