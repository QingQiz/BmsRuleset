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

    // ── Outlier charts in the set (typos / unrelated songs dumped in the same folder) ──

    [Test]
    public void TestOutlierTypoAmongMajority()
    {
        // Real "conflict" folder: 56 "conflict …" charts + 1 "congolict" typo.
        // A pure LCP collapses to "con"; the majority-shared prefix must win.
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["conflict [NORMAL]", "conflict[HYPER]", "congolict"]),
            Is.EqualTo("conflict"));
    }

    [Test]
    public void TestUnrelatedSongInDirectory()
    {
        // A stray unrelated chart shouldn't drag the set title below the majority song.
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["SongA [NORMAL]", "SongA [HYPER]", "SongB [ANOTHER]"]),
            Is.EqualTo("SongA"));
    }

    [Test]
    public void TestOutlierWithoutSuffixMustNotPoisonSuffixStrip()
    {
        // The outlier "Titlo" has no bracketed suffix at all. Suffix-stripping must
        // run over the majority core only — otherwise the opener '[' stays dangling.
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["Title[NORMAL]", "Title[HYPER]", "Titlo"]),
            Is.EqualTo("Title"));
    }

    [Test]
    public void TestAlephZeroRealDirectoryTitles()
    {
        // The actual 8 #TITLE values from the Aleph-0 test song folder: 7 charts
        // use "Aleph-0[...]" (no space), one uses "Aleph-0 [Dirty Pattern]" (space).
        // The set title must collapse to the clean common base "Aleph-0".
        Assert.That(
            BmsChartParser.InferCommonSetTitle([
                "Aleph-0[14ANOTHER]",
                "Aleph-0[(^_^;)]",
                "Aleph-0[ANOTHER]",
                "Aleph-0[HYPER]",
                "Aleph-0[INSANE]",
                "Aleph-0[NORMAL]",
                "Aleph-0[ENTER]",
                "Aleph-0 [Dirty Pattern]"
            ]),
            Is.EqualTo("Aleph-0"));
    }

    [Test]
    public void TestTruncatedSuffixInOneChartMustNotKeepOpener()
    {
        // A title imported from an externally normalized source can be truncated
        // mid-suffix. That one truncated chart must not keep the opener attached
        // to the set title when the rest of the set closes the suffix.
        Assert.That(
            BmsChartParser.InferCommonSetTitle([
                "Aleph-0[14ANOTHER]",
                "Aleph-0[(^_^",
                "Aleph-0[ANOTHER]",
                "Aleph-0[HYPER]",
                "Aleph-0[INSANE]",
                "Aleph-0[NORMAL]",
                "Aleph-0[ENTER]",
                "Aleph-0 [Dirty Pattern]"
            ]),
            Is.EqualTo("Aleph-0"));
    }

    // ── Suffix glued to base (no space) with a shared inner prefix ──
    // When per-difficulty suffixes share a prefix INSIDE the brackets
    // ("[SP ANOTHER]" / "[SP HYPER]"), the LCP extends past the opener
    // ("Title[SP "), so the trailing-opener strip can't catch it on its own.
    // The boundary trim must still strip back to the base despite the glue.

    [Test]
    public void TestBracketSuffixNoSpaceSharedPrefix()
    {
        Assert.That(
            BmsChartParser.InferCommonSetTitle([
                "Title[SP ANOTHER]",
                "Title[SP HYPER]",
                "Title[SP NORMAL]"
            ]),
            Is.EqualTo("Title"));
    }

    [Test]
    public void TestParenSuffixNoSpaceSharedPrefix()
    {
        Assert.That(
            BmsChartParser.InferCommonSetTitle([
                "Title(SP ANOTHER)",
                "Title(SP HYPER)",
                "Title(SP NORMAL)"
            ]),
            Is.EqualTo("Title"));
    }

    [Test]
    public void TestTruncatedSuffixMajorityMustNotKeepOpener()
    {
        // When a majority of charts have externally truncated suffixes, the
        // closer-quorum would fail on the intact titles alone; the truncated
        // opener-plus-content must still count as suffix evidence so the set title
        // collapses to the base rather than "Title [".
        Assert.That(
            BmsChartParser.InferCommonSetTitle([
                "Title [7key",
                "Title [14key",
                "Title [5key",
                "Title [32]",
                "Title [FEATHER]",
                "Title"
            ]),
            Is.EqualTo("Title"));
    }

    // ── Empty / whitespace titles are skipped during inference ──

    [Test]
    public void TestEmptyTitlesSkipped()
    {
        // Charts with no #TITLE (empty raw title) must not poison the inference —
        // the one real title still surfaces as the set title.
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["", "", "Title [NORMAL]"]),
            Is.EqualTo("Title [NORMAL]"));
    }

    [Test]
    public void TestEmptyTitlesSkippedAmongMany()
    {
        Assert.That(
            BmsChartParser.InferCommonSetTitle(["Title [NORMAL]", "Title [HYPER]", "", ""]),
            Is.EqualTo("Title"));
    }

    // ── Difficulty delimiter stripping ──

    [TestCase("[NORMAL]", "NORMAL")]
    [TestCase("(AAA)", "AAA")]
    [TestCase("（HARD）", "HARD")]
    [TestCase("[[SPECIAL]]", "[SPECIAL]")]
    [TestCase("[AAA [BBB]]", "AAA [BBB]")]
    [TestCase("[AAA] BBB [CCC]", "[AAA] BBB [CCC]")]
    [TestCase("(AAA) BBB [CCC]", "(AAA) BBB [CCC]")]
    [TestCase("[AAA)", "[AAA)")]
    [TestCase("[INCOMPLETE", "[INCOMPLETE")]
    [TestCase("-Diff-", "Diff")]
    [TestCase("~Hard~", "Hard")]
    [TestCase("-AAA- BBB -CCC-", "-AAA- BBB -CCC-")]
    public void TestStripDifficultyDelimiters(string value, string expected)
    {
        Assert.That(BmsChartParser.StripDifficultyDelimiters(value), Is.EqualTo(expected));
    }

    // ── Mixed bracket groups across a set ──

    [Test]
    public void TestMixedBracketSuffixesInferBaseTitle()
    {
        Assert.That(
            BmsChartParser.InferCommonSetTitle([
                "Title (AAA) BBB [CCC]",
                "Title (DDD) BBB [EEE]"
            ]),
            Is.EqualTo("Title"));
    }

    [Test]
    public void TestRepeatedSquareBracketSuffixesInferBaseTitle()
    {
        Assert.That(
            BmsChartParser.InferCommonSetTitle([
                "Title [AAA] BBB [CCC]",
                "Title [DDD] BBB [EEE]"
            ]),
            Is.EqualTo("Title"));
    }

    [Test]
    public void TestSharedMixedBracketPrefixRemainsInSetTitle()
    {
        Assert.That(
            BmsChartParser.InferCommonSetTitle([
                "Title (AAA) BBB [CCC]",
                "Title (AAA) BBB [DDD]"
            ]),
            Is.EqualTo("Title (AAA) BBB"));
    }

    // ── InferTitle: per-chart title-vs-difficulty splitter (derives DifficultyName) ──

    [TestCase("Title [NORMAL]", "Title")]
    [TestCase("Title[NORMAL]", "Title")]
    [TestCase("Title (EASY)", "Title")]
    [TestCase("Title(EASY)", "Title")]
    [TestCase("Aleph-0 (by LeaF)", "Aleph-0")] // '-' is inside the base title; '(' suffix strips.
    [TestCase("Plain Title", "Plain Title")]
    [TestCase("Title -Diff-", "Title")]
    [TestCase("Title-Diff-", "Title")]
    [TestCase("Title -AAA-", "Title")]
    [TestCase("Title ~Easy~", "Title")]
    [TestCase("Title~Hard~", "Title")]
    public void TestInferTitle(string title, string expected)
    {
        Assert.That(BmsChartParser.InferTitle(title), Is.EqualTo(expected),
            $"InferTitle({title})");
    }

    // ── InferDifficultyName: per-chart difficulty suffix relative to the set title ──

    [TestCase("conflict [NORMAL]", "conflict", "NORMAL")]
    [TestCase("conflict[HYPER]", "conflict", "HYPER")]
    [TestCase("congolict", "conflict", "congolict")] // outlier typo → raw name as diff name
    [TestCase("Aleph-0[NORMAL]", "Aleph-0", "NORMAL")]
    [TestCase("Aleph-0 [Dirty Pattern]", "Aleph-0", "Dirty Pattern")]
    [TestCase("Aleph-0[(^_^", "Aleph-0", "[(^_^")] // malformed wrappers are preserved
    [TestCase("Aleph-0[NORMAL]", "Aleph-0[NORMAL]", "NORMAL")] // single chart → InferTitle fallback
    [TestCase("Destr0yer", "Destr0yer", "")] // single chart, no suffix → empty (caller falls back)
    [TestCase("Title [AAA] BBB [CCC]", "Title", "[AAA] BBB [CCC]")]
    [TestCase("Title [AAA] BBB [CCC]", "Title [AAA] BBB", "CCC")]
    [TestCase("Title (AAA) BBB [CCC]", "Title", "(AAA) BBB [CCC]")]
    [TestCase("Title (AAA) BBB [CCC]", "Title (AAA) BBB", "CCC")]
    public void TestInferDifficultyName(string rawTitle, string setTitle, string expected)
    {
        Assert.That(BmsChartParser.InferDifficultyName(rawTitle, setTitle), Is.EqualTo(expected),
            $"InferDifficultyName({rawTitle}, {setTitle})");
    }
}
