using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public class BmsCommentStripperTest
{
    private static Beatmap decode(string text)
    {
        using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var reader = new LineBufferedReader(memoryStream);
        return new BmsBeatmapDecoder().Decode(reader);
    }

    private static Beatmap decodeWithSelector(string text, Func<int, int> selector)
    {
        using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var reader = new LineBufferedReader(memoryStream);
        return new BmsBeatmapDecoder(selector).Decode(reader);
    }

    [Test]
    public void TestActualExpansionFieldLineDoesNotSwallowMainData()
    {
        // This matches the exact line from [Clue]Random charts that caused the bug.
        var beatmap = decode("""
                             #BPM 120
                             #WAV01 test.wav
                             ////*---------------------- EXPANSION FIELD
                             #BMP01 __bga.mpeg
                             #00004:00000000000000000000000000000000000000000000000000000000000000000000000000000000000100000000000000000000000000000000000000000000

                             *---------------------- MAIN DATA FIELD

                             #00111:01
                             """);
        Assert.That(beatmap.HitObjects.OfType<BmsHitObject>().Count(), Is.EqualTo(1));
        Assert.That(beatmap.Metadata.Source, Is.EqualTo("BMS"));
        Assert.That(beatmap.ControlPointInfo.TimingPoints.Count, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void TestBackslashInFilePathIsUnchanged()
    {
        // File paths with backslashes (e.g. #WAV01) should not have \ consumed
        var beatmap = decode("""
                             #WAV01 C:\samples\kick.wav
                             #BPM 120
                             #00111:01
                             """);
        var hitObject = beatmap.HitObjects.OfType<BmsHitObject>().FirstOrDefault();
        Assert.That(hitObject, Is.Not.Null);
        Assert.That(hitObject.SampleKey, Is.EqualTo("01"));
    }

    [Test]
    public void TestBlockCommentAcrossControlFlow()
    {
        var chart = string.Join("\n", new[]
        {
            "#RANDOM 3",
            "#IF 1",
            "#TITLE branch1",
            "#ENDIF",
            "#ELSEIF 2/*",
            "#TITLE hidden",
            "#ELSEIF ; */3",
            "#TITLE branch3",
            "#ENDIF",
            "#ENDRANDOM",
            "#BPM 120",
            "#00111:01",
        });

        var beatmap = decode(chart);
        // Default selector picks branch 1
        Assert.That(beatmap.Metadata.Title, Is.EqualTo("branch1"));
    }

    [Test]
    public void TestBlockCommentDoesNotNest()
    {
        // First */ closes the block
        var beatmap = decode("""
                             #TITLE foo/* /* */ bar */ baz
                             #BPM 120
                             #00111:01
                             """);
        Assert.That(beatmap.Metadata.Title, Is.EqualTo("foo bar */ baz"));
    }

    [Test]
    public void TestBlockCommentMultiLine()
    {
        var beatmap = decode("""
                             /*
                             #ARTIST hidden
                             */
                             #TITLE shown
                             #BPM 120
                             #00111:01
                             """);
        Assert.That(beatmap.Metadata.Title, Is.EqualTo("shown"));
        // Artist is not set (comment stripped), defaults to framework default
        Assert.That(beatmap.Metadata.Artist, Is.EqualTo("Unknown"));
    }

    [Test]
    public void TestBlockCommentOnChannelLine()
    {
        var beatmap = decode("""
                             #BPM 120
                             #00111:01/*00*/
                             """);
        Assert.That(beatmap.HitObjects.OfType<BmsHitObject>().Count(), Is.EqualTo(1));
    }

    // ---------- /* */ block comments ----------

    [Test]
    public void TestBlockCommentSingleLine()
    {
        var beatmap = decode("""
                             #TITLE foo/*bar*/baz
                             #BPM 120
                             #00111:01
                             """);
        Assert.That(beatmap.Metadata.Title, Is.EqualTo("foobaz"));
    }

    // ---------- // line comments ----------

    [Test]
    public void TestDoubleSlashComment()
    {
        var beatmap = decode("""
                             #TITLE foo // comment
                             #BPM 120
                             #00111:01
                             """);
        // // strips comment; parser command value has no trailing space due to Trim()
        Assert.That(beatmap.Metadata.Title, Is.EqualTo("foo"));
    }

    [Test]
    public void TestDoubleSlashOnChannelLine()
    {
        var beatmap = decode("""
                             #BPM 120
                             #00111:01 // inline comment
                             """);
        Assert.That(beatmap.HitObjects.OfType<BmsHitObject>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void TestDoubleSlashOnlyLine()
    {
        var beatmap = decode("""
                             #TITLE foo
                             // this is a comment line
                             #BPM 120
                             #00111:01
                             """);
        Assert.That(beatmap.HitObjects.OfType<BmsHitObject>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void TestElseIfWithBlockComment()
    {
        // #ELSEIF 1/* */2 → block removes "/* */" → #ELSEIF 12
        var beatmap = decodeWithSelector("""
                                         #RANDOM 12
                                         #IF 1
                                         #TITLE branch1
                                         #ENDIF
                                         #ELSEIF 1/* */2
                                         #TITLE branch12
                                         #ENDIF
                                         #ENDRANDOM
                                         #BPM 120
                                         #00111:01
                                         """, _ => 1);
        Assert.That(beatmap.Metadata.Title, Is.EqualTo("branch1"));

        // With selector returning 12, branch 12 should be active:
        // #ELSEIF 1/* */2 → #ELSEIF 12 → match value 12 → active.
        var beatmap2 = decodeWithSelector("""
                                          #RANDOM 12
                                          #IF 1
                                          #TITLE branch1
                                          #ENDIF
                                          #ELSEIF 1/* */2
                                          #TITLE branch12
                                          #ENDIF
                                          #ENDRANDOM
                                          #BPM 120
                                          #00111:01
                                          """, _ => 12);
        Assert.That(beatmap2.Metadata.Title, Is.EqualTo("branch12"));
    }

    // ---------- Control flow + comments ----------

    [Test]
    public void TestElseWithDoubleSlashComment()
    {
        // #ELSE//IF 4 → line comment strips "//IF 4" → #ELSE (no condition).
        // After #ENDIF closes the #IF 1 scope, #ELSE starts a fresh unconditional
        // branch that sets #TITLE branch2.
        var beatmap = decodeWithSelector("""
                                         #RANDOM 2
                                         #IF 1
                                         #TITLE branch1
                                         #ENDIF
                                         #ELSE//IF 2
                                         #TITLE branch2
                                         #ENDIF
                                         #ENDRANDOM
                                         #BPM 120
                                         #00111:01
                                         """, _ => 1);
        // #ELSE (unconditional) runs after #ENDIF closes #IF 1, so branch2 wins.
        Assert.That(beatmap.Metadata.Title, Is.EqualTo("branch2"));
    }

    [Test]
    public void TestEscapedDoubleSlash()
    {
        var beatmap = decode("""
                             #TITLE foo\/\/bar
                             #BPM 120
                             #00111:01
                             """);
        Assert.That(beatmap.Metadata.Title, Is.EqualTo("foo//bar"));
    }

    // ---------- \ escape sequences ----------

    [Test]
    public void TestEscapedSemicolon()
    {
        var beatmap = decode("""
                             #TITLE foo\; bar
                             #BPM 120
                             #00111:01
                             """);
        Assert.That(beatmap.Metadata.Title, Is.EqualTo("foo; bar"));
    }

    // ---------- Existing chart still works ----------

    [Test]
    public void TestExistingParserStillWorks()
    {
        var beatmap = decode("""
                             #TITLE Test
                             #ARTIST Me
                             #BPM 130
                             #RANK 2
                             #TOTAL 200
                             #00111:01
                             #00112:02
                             #00113:03
                             #00201:00
                             """);
        Assert.That(beatmap.Metadata.Title, Is.EqualTo("Test"));
        Assert.That(beatmap.Metadata.Artist, Is.EqualTo("Me"));
        Assert.That(beatmap.HitObjects.OfType<BmsHitObject>().Count(), Is.EqualTo(3));
    }

    // ---------- IIDXv spec example ----------

    [Test]
    public void TestIidxvSpecExample()
    {
        // #TITLE foo-/*bar-*/baz; :)
        var beatmap = decode("""
                             #TITLE foo-/*bar-*/baz; :)
                             #BPM 120
                             #00111:01
                             """);
        // /*bar-*/ removed, then ; strips " :)" → "foo-baz"
        Assert.That(beatmap.Metadata.Title, Is.EqualTo("foo-baz"));
    }

    [Test]
    public void TestLineCommentBeforeBlockCommentDoesNotStartBlock()
    {
        // // preceding /* should prevent /* from starting a multi-line block comment.
        var beatmap = decode("""
                             #TITLE visible
                             #BPM 120
                             ////*---------------------- FAKE EXPANSION FIELD
                             #00111:01
                             """);
        Assert.That(beatmap.HitObjects.OfType<BmsHitObject>().Count(), Is.EqualTo(1));
        Assert.That(beatmap.Metadata.Title, Is.EqualTo("visible"));
    }

    [Test]
    public void TestLineCommentBeforeBlockCommentKeepsSubsequentData()
    {
        // All lines after a ///* line must remain visible (not consumed by block comment).
        var beatmap = decode("""
                             #BPM 120
                             ////*---------------------- EXPANSION FIELD
                             #WAV01 test.wav
                             #00111:01
                             #00112:02
                             """);
        Assert.That(beatmap.HitObjects.OfType<BmsHitObject>().Count(), Is.EqualTo(2));
    }

    [Test]
    public void TestPercentUrlPreservesHttpsSlashSlash()
    {
        // % lines bypass comment stripping, so // inside URLs is preserved.
        var beatmap = decode("""
                             #TITLE Test
                             #BPM 120
                             %URL https://example.com/path
                             #00111:01
                             """);
        Assert.That(beatmap.Metadata.Tags, Does.Contain("https://example.com/path"));
    }

    [Test]
    public void TestQuotedBlockCommentIsLiteral()
    {
        var beatmap = decode("""
                             #TITLE "foo /* bar */ baz"
                             #BPM 120
                             #00111:01
                             """);
        Assert.That(beatmap.Metadata.Title, Is.EqualTo("\"foo /* bar */ baz\""));
    }

    [Test]
    public void TestQuotedDoubleSlashIsLiteral()
    {
        var beatmap = decode("""
                             #TITLE "foo // bar"
                             #BPM 120
                             #00111:01
                             """);
        Assert.That(beatmap.Metadata.Title, Is.EqualTo("\"foo // bar\""));
    }

    // ---------- "..." quote protection ----------

    [Test]
    public void TestQuotedSemicolonIsLiteral()
    {
        var beatmap = decode("""
                             #TITLE "foo; bar"
                             #BPM 120
                             #00111:01
                             """);
        Assert.That(beatmap.Metadata.Title, Is.EqualTo("\"foo; bar\""));
    }

    [Test]
    public void TestSemicolonBeforeElseIf()
    {
        // #ELSEIF ; */3 → ; strips whole value → #ELSEIF (no value)
        var beatmap = decodeWithSelector("""
                                         #RANDOM 3
                                         #IF 1
                                         #TITLE branch1
                                         #ENDIF
                                         #ELSEIF ; */3
                                         #TITLE branch3
                                         #ENDIF
                                         #ENDRANDOM
                                         #BPM 120
                                         #00111:01
                                         """, _ => 3);
        // #ELSEIF with empty value → tryParseInt("") → false → value = 0 → doesn't match 3
        Assert.That(beatmap.Metadata.Title, Is.Not.EqualTo("branch3"));
    }

    // ---------- ; line comments ----------

    [Test]
    public void TestSemicolonComment()
    {
        var beatmap = decode("""
                             #TITLE foo; bar
                             #BPM 120
                             #00111:01
                             """);
        Assert.That(beatmap.Metadata.Title, Is.EqualTo("foo"));
    }

    [Test]
    public void TestSemicolonOnlyLine()
    {
        var beatmap = decode("""
                             #TITLE foo
                             ; comment line
                             #BPM 120
                             #00111:01
                             """);
        Assert.That(beatmap.HitObjects.OfType<BmsHitObject>().Count(), Is.EqualTo(1));
    }
}
