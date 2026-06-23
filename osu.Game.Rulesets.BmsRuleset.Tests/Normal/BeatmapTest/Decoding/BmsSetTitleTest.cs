using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.BeatmapTest.Decoding;

[TestFixture]
public class BmsSetTitleTest
{

    // ── All same title ──

    [Test]
    public void TestAllSameTitle()
    {
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["Title", "Title", "Title"]),
            Is.EqualTo("Title"));
    }

    [Test]
    public void TestAllSameWithBracketSuffix()
    {
        // Same suffix on all → not really a difficulty distinction, keep as-is.
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["Title [Remix]", "Title [Remix]"]),
            Is.EqualTo("Title [Remix]"));
    }
    // ── Basic bracket cases ──

    [Test]
    public void TestBracketSuffix()
    {
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["Title [NORMAL]", "Title [HYPER]", "Title [ANOTHER]"]),
            Is.EqualTo("Title"));
    }

    // ── No space between title and suffix ──

    [Test]
    public void TestBracketSuffixNoSpace()
    {
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["Title[NORMAL]", "Title[HYPER]"]),
            Is.EqualTo("Title"));
    }

    [Test]
    public void TestBracketSuffixTwoCharts()
    {
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["Song Name [CS]", "Song Name [AC]"]),
            Is.EqualTo("Song Name"));
    }

    // ── Empty / edge ──

    [Test]
    public void TestEmpty()
    {
        Assert.That(
            BmsChartParser.InferCommonSetTitle([]),
            Is.EqualTo(""));
    }

    // ── Full-width parenthesis ──

    [Test]
    public void TestFullWidthParenSuffix()
    {
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["Title （EASY）", "Title （HARD）"]),
            Is.EqualTo("Title"));
    }

    // ── Mixed separator ──

    [Test]
    public void TestHyphenBracketSuffix()
    {
        // The "-" is before the "[...]" suffix — it's part of the base title.
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["Title - [A]", "Title - [B]"]),
            Is.EqualTo("Title -"));
    }

    // ── Hyphen suffix ──

    [Test]
    public void TestHyphenSuffix()
    {
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["Title -Diff-", "Title -AAA-"]),
            Is.EqualTo("Title"));
    }

    [Test]
    public void TestHyphenSuffixNoSpace()
    {
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["Title-Diff-", "Title-AAA-"]),
            Is.EqualTo("Title"));
    }

    [Test]
    public void TestHyphenSuffixSharedPrefix()
    {
        // LCP bleeds into "Diff" because both suffixes start with "Diff".
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["Title -Diff-", "Title -Diff2-", "Title -Diff2- (XXX)"]),
            Is.EqualTo("Title"));
    }

    // ── No suffix on one chart ──

    [Test]
    public void TestOneChartWithoutSuffix()
    {
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["Title", "Title [Another]"]),
            Is.EqualTo("Title"));
    }

    [Test]
    public void TestOneChartWithoutSuffixHyphen()
    {
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["Title", "Title -Diff-"]),
            Is.EqualTo("Title"));
    }

    // ── Empty / fallback ──

    [Test]
    public void TestOnlyDiffMarkers()
    {
        // Titles with no common base — fall back to first raw title.
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["[NORMAL]", "[HYPER]"]),
            Is.EqualTo("[NORMAL]"));
    }

    // ── Parenthesis ──

    [Test]
    public void TestParenSuffix()
    {
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["Title (EASY)", "Title (HARD)"]),
            Is.EqualTo("Title"));
    }

    [Test]
    public void TestParenSuffixNoSpace()
    {
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["Title(EASY)", "Title(HARD)"]),
            Is.EqualTo("Title"));
    }

    // ── Single chart ──

    [Test]
    public void TestSingleChart()
    {
        // Single chart — can't know if suffix is difficulty or title, keep as-is.
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["Title [NORMAL]"]),
            Is.EqualTo("Title [NORMAL]"));
    }

    [Test]
    public void TestSingleChartNoSuffix()
    {
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["Plain Title"]),
            Is.EqualTo("Plain Title"));
    }

    // ── Tilde ──

    [Test]
    public void TestTildeSuffix()
    {
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["Title ~Easy~", "Title ~Hard~"]),
            Is.EqualTo("Title"));
    }

    [Test]
    public void TestTildeSuffixNoSpace()
    {
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["Title~Easy~", "Title~Hard~"]),
            Is.EqualTo("Title"));
    }

    // ── Title literally ends with opener ──

    [Test]
    public void TestTitleEndsWithBracket()
    {
        // "Title [" is the actual title, not a suffix opener.
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["Song [", "Song [ (Remix)"]),
            Is.EqualTo("Song ["));
    }

    [Test]
    public void TestTitleEndsWithHyphen()
    {
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["Title-", "Title- (Remix)"]),
            Is.EqualTo("Title-"));
    }

    [Test]
    public void TestTitleLikeDiff()
    {
        // Titles with no common base — fall back to first raw title.
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["[NORMAL]"]),
            Is.EqualTo("[NORMAL]"));
    }
}
