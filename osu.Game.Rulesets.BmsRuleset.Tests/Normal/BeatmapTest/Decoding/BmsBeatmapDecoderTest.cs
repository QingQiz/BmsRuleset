using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using Decoder = osu.Game.Beatmaps.Formats.Decoder;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.BeatmapTest.Decoding;

[TestFixture]
public class BmsBeatmapDecoderTest
{
    private static Beatmap decode(string text)
    {
        using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var reader = new LineBufferedReader(memoryStream);

        return new BmsBeatmapDecoder().Decode(reader);
    }

    private static Beatmap decode(string text, Func<int, int> randomValueSelector)
    {
        using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var reader = new LineBufferedReader(memoryStream);

        return new BmsBeatmapDecoder(randomValueSelector).Decode(reader);
    }

    private static Beatmap decodeResource(string resourceName)
    {
        using var stream = typeof(BmsBeatmapDecoderTest).Assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Missing embedded resource: {resourceName}");
        using var reader = new LineBufferedReader(stream);

        return new BmsBeatmapDecoder().Decode(reader);
    }

    [Test]
    public void DiagnoseLnAutoplayTimingForCautionChart()
    {
        // Find the actual resource name
        var allResources = typeof(BmsBeatmapDecoderTest).Assembly.GetManifestResourceNames();
        var resourceName = allResources.FirstOrDefault(n => n.EndsWith("99_outlaw_caution.bms", StringComparison.OrdinalIgnoreCase))
                           ?? throw new InvalidOperationException("Resource not found in: " + string.Join(", ", allResources.Where(n => n.Contains("outlaw"))));

        using var stream = typeof(BmsBeatmapDecoderTest).Assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Missing: {resourceName}");
        using var reader = new LineBufferedReader(stream);

        var decoded = new BmsBeatmapDecoder().Decode(reader);
        var beatmap = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()).Convert();
        var hitObjects = beatmap.HitObjects.OfType<BmsHitObject>().ToList();

        var lns = hitObjects.Where(h => h.IsLongNote).ToList();
        var table = BmsJudgementProfileProvider.GetTable(beatmap.LayoutVariant, column: 1, beatmap.Rank, tail: false);
        var pgreat = table.FrameworkWindowFor(HitResult.Perfect);
        var great = table.FrameworkWindowFor(HitResult.Great);
        var good = table.FrameworkWindowFor(HitResult.Good);
        var bad = table.LateWindowFor(HitResult.Ok);

        var issues = new List<string>();

        foreach (var ln in lns)
        {
            var autoplayPress = ln.StartTime;
            var autoplayRelease = ln.EndTime; // calculateReleaseTime for LN with Duration>0

            // Check press timing
            var pressOffset = autoplayPress - ln.StartTime; // should be 0
            var pressJudgement = table.ResultForOffset(pressOffset);
            var pressOk = pressOffset >= -pgreat && pressOffset <= pgreat;

            // Check release timing
            var releaseOffset = autoplayRelease - ln.EndTime; // should be 0
            var releaseJudgement = table.ResultForOffset(releaseOffset);
            var releaseOk = releaseOffset >= -pgreat && releaseOffset <= pgreat;

            if (!pressOk || !releaseOk || pressJudgement != HitResult.Perfect || releaseJudgement != HitResult.Perfect)
            {
                var issue = $"LN tick={ln.TickInfo.Tick}→{ln.TickInfo.EndTick} col={ln.Column} " +
                            $"Duration={ln.Duration:F3}ms " +
                            $"pressOffset={pressOffset:F3}ms→{pressJudgement} " +
                            $"releaseOffset={releaseOffset:F3}ms→{releaseJudgement}";
                issues.Add(issue);
            }
        }

        var summary = $"Chart: {beatmap.Metadata.Title}  Rank: {beatmap.Rank}  " +
                      $"PGREAT={pgreat}ms GREAT={great}ms GOOD={good}ms BAD={bad}ms  " +
                      $"Total LNs: {lns.Count}  Notes: {hitObjects.Count(h => !h.IsMine && !h.IsLongNote)}  " +
                      $"Mines: {hitObjects.Count(h => h.IsMine)}";

        if (issues.Count > 0)
            Assert.Fail($"{summary}\n=== LNs with timing issues ===\n{string.Join("\n", issues)}");

        // Find short LNs and LNs with potential issues
        var shortLns = lns.Where(ln => ln.Duration <= great * 2).OrderBy(ln => ln.Duration).ToList();
        var shortLnsReport = string.Join("\n  ", shortLns.Take(20).Select(ln =>
            $"tick={ln.TickInfo.Tick}→{ln.TickInfo.EndTick} col={ln.Column} dur={ln.Duration:F1}ms " +
            $"start={ln.StartTime:F1}ms end={ln.EndTime:F1}ms"));

        // Also show LNs around combo 190 (roughly 190 notes in)
        var normalNotes = hitObjects.Where(h => !h.IsMine).ToList();
        var aroundCombo190 = lns.Skip(Math.Max(0, normalNotes.Take(190).Count(h => h.IsLongNote) - 3)).Take(7)
            .Select(ln => $"tick={ln.TickInfo.Tick}→{ln.TickInfo.EndTick} col={ln.Column} dur={ln.Duration:F1}ms " +
                          $"start={ln.StartTime:F0}ms end={ln.EndTime:F0}ms");
        var around190 = string.Join("\n  ", aroundCombo190);

        var outputPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "caution_ln_diag.txt");

        File.WriteAllText(outputPath,
            $"{summary}\nAll LNs PGREAT.\n" +
            $"=== Shortest LNs (dur < {great * 2:F0}ms, {shortLns.Count} total, showing first 20) ===\n  {shortLnsReport}\n" +
            $"=== LNs around combo ~190 ===\n  {around190}");

        Assert.Pass($"Diagnostic written to {outputPath}");
    }

    [Test]
    public void TestAutoplayExtensionPathCarriesBranchDecisionFrame()
    {
        var beatmap = new BmsBeatmap
        {
            BranchDecisions = [new BmsBranchDecision(3, 1)],
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1 },
            },
        };
        ICreateReplayData autoplay = new BmsModAutoplay();

        var score = autoplay.CreateScoreFromReplayData(beatmap, [(Mod)autoplay]);

        Assert.That(score.Replay.Frames.OfType<BmsReplayFrame>().First().BranchDecisions, Is.EqualTo("3:1"));
    }

    [Test]
    public void TestAutoplayReplayCarriesBranchDecisions()
    {
        var beatmap = new BmsBeatmap
        {
            BranchDecisions = [new BmsBranchDecision(2, 2)],
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1 },
            },
        };
        var autoplay = new BmsModAutoplay();

        var replayData = autoplay.CreateReplayData(beatmap, [autoplay]);
        var score = BmsModAutoplay.CreateScoreWithBranchDecisions(beatmap, replayData);

        Assert.That(score.ScoreInfo.Mods.OfType<BmsModBranchReplay>().Single().Decisions.Value, Is.EqualTo("2:2"));
        Assert.That(replayData.Replay.Frames.OfType<BmsReplayFrame>().First().BranchDecisions, Is.EqualTo("2:2"));
    }

    [Test]
    public void TestBase62BaseHeaderWithout62IsIgnored()
    {
        // #BASE with a value other than "62" should not activate base-62 mode.
        var beatmap = decode("""
                             #BASE 36
                             #BPM 120
                             #WAVaa lowercase.wav
                             #WAVAA uppercase.wav
                             #00111:aa
                             """);
        var hitObject = beatmap.HitObjects.OfType<BmsHitObject>().Single();

        // Should still be case-insensitive.
        Assert.That(hitObject.SamplePath, Is.EqualTo("uppercase.wav"));
    }

    [Test]
    public void TestBase62BpmDefinitionsAreCaseSensitive()
    {
        // With #BASE 62, #BPMaa (150) and #BPMAa (200) are distinct keys.
        // Cell "aa" → #BPMaa = 150; cell "Aa" → #BPMAa = 200.
        var beatmap = decode("""
                             #BASE 62
                             #BPM 120
                             #BPMaa 150
                             #BPMAa 200
                             #00108:aaAa
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        var bpmEvents = converted.TimingMap!.BpmEvents;
        Assert.That(bpmEvents.Any(e => Math.Abs(e.Bpm - 150) < 0.001), Is.True, "BPM 150 from 'aa' should exist");
        Assert.That(bpmEvents.Any(e => Math.Abs(e.Bpm - 200) < 0.001), Is.True, "BPM 200 from 'Aa' should exist");
        Assert.That(bpmEvents.Count(e => e.Bpm > 0), Is.EqualTo(3)); // 120 + 150 + 200
    }

    [Test]
    public void TestBase62BpmDefinitionsKeyCollisionWithoutBase62()
    {
        // Without #BASE 62, #BPMaa and #BPMAa collide (case-insensitive).
        // The later definition (#BPMAa = 200) overwrites #BPMaa = 150.
        var beatmap = decode("""
                             #BPM 120
                             #BPMaa 150
                             #BPMAa 200
                             #00108:aa
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        // "aa" and "AA" encode to the same key → BPM should be 200 (later wins)
        Assert.That(converted.TimingMap!.BpmEvents.Any(e => Math.Abs(e.Bpm - 200) < 0.001), Is.True);
        Assert.That(converted.TimingMap.BpmEvents.Any(e => Math.Abs(e.Bpm - 150) < 0.001), Is.False);
    }

    // ── #BASE 62 (case-sensitive encoding) ─────────────────────────────

    [Test]
    public void TestBase62CaseInsensitiveByDefault()
    {
        // Without #BASE 62, lowercase and uppercase sample keys collide (traditional BMS).
        var beatmap = decode("""
                             #BPM 120
                             #WAVaa lowercase.wav
                             #WAVAA uppercase.wav
                             #00111:aa
                             """);
        var hitObject = beatmap.HitObjects.OfType<BmsHitObject>().Single();

        // "aa" and "AA" should encode to the same key (case-insensitive default).
        // The second definition (#WAVAA) overwrites the first.
        Assert.That(hitObject.SamplePath, Is.EqualTo("uppercase.wav"));
    }

    [Test]
    public void TestBase62CaseSensitiveWhenDeclared()
    {
        // With #BASE 62, lowercase and uppercase are distinct keys.
        var beatmap = decode("""
                             #BASE 62
                             #BPM 120
                             #WAVaa lowercase.wav
                             #WAVAA uppercase.wav
                             #00111:aa
                             """);
        var hitObject = beatmap.HitObjects.OfType<BmsHitObject>().Single();

        // "aa" maps to the lowercase definition only.
        Assert.That(hitObject.SamplePath, Is.EqualTo("lowercase.wav"));
    }

    [Test]
    public void TestBase62CellValuesPreserveCase()
    {
        // Cell values in base-62 mode distinguish case.
        var beatmap = decode("""
                             #BASE 62
                             #BPM 120
                             #WAVaa lower.wav
                             #WAVAA upper.wav
                             #00111:aaAA
                             """);
        var hitObjects = beatmap.HitObjects.OfType<BmsHitObject>().OrderBy(h => h.StartTime).ToList();

        Assert.That(hitObjects, Has.Count.EqualTo(2));
        Assert.That(hitObjects[0].SamplePath, Is.EqualTo("lower.wav"));
        Assert.That(hitObjects[1].SamplePath, Is.EqualTo("upper.wav"));
    }

    [Test]
    public void TestBase62Channel03HexWithLowercaseIsUnaffected()
    {
        // Channel 03 (BPM changes) uses hex — unaffected by #BASE 62.
        // Both "1a" (lowercase) and "1A" (uppercase) must decode to 0x1A = 26 BPM.
        var beatmap = decode("""
                             #BASE 62
                             #BPM 120
                             #00003:1a1A
                             #00111:01
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        var bpmEvents = converted.TimingMap!.BpmEvents;
        // Both 26 BPM events from "1a" and "1A" (plus initial 120)
        var bpm26Events = bpmEvents.Where(e => Math.Abs(e.Bpm - 26) < 0.001).ToList();
        Assert.That(bpm26Events, Has.Count.EqualTo(2), "Both '1a' and '1A' should decode to BPM 26");
        Assert.That(bpmEvents[0].Bpm, Is.EqualTo(120).Within(0.001));
    }

    [Test]
    public void TestBase62ChannelIdCaseInsensitiveWithBase62()
    {
        // Channel IDs are hex and always case-insensitive, even with #BASE 62.
        // Lowercase #001d3 should be recognized as the same channel as #001D3.
        var beatmap = decode("""
                             #BASE 62
                             #BPM 120
                             #WAV00 bomb.wav
                             #001d3:0a
                             """);
        var mines = beatmap.HitObjects.OfType<BmsHitObject>().Where(h => h.IsMine).ToList();

        Assert.That(mines, Has.Count.EqualTo(1), "Lowercase channel 'd3' should be recognized as mine channel");
        Assert.That(mines[0].LandmineDamagePercent, Is.EqualTo(5));
    }

    [Test]
    public void TestBase62ChannelIdCaseInsensitiveWithoutBase62()
    {
        // Channel IDs are always case-insensitive per BMS spec, regardless of #BASE 62.
        var beatmap = decode("""
                             #BPM 120
                             #WAV00 bomb.wav
                             #001d3:0A
                             """);
        var mines = beatmap.HitObjects.OfType<BmsHitObject>().Where(h => h.IsMine).ToList();

        Assert.That(mines, Has.Count.EqualTo(1), "Lowercase channel 'd3' should be recognized as mine channel");
        Assert.That(mines[0].LandmineDamagePercent, Is.EqualTo(5));
    }

    [Test]
    public void TestBase62LandmineChannelWithLowercaseIsUnaffected()
    {
        // Mine damage channels (D*, E*) use 36-base — unaffected by #BASE 62.
        // Both "0a" (lowercase) and "0A" (uppercase) must decode to 5% damage.
        var beatmap = decode("""
                             #BASE 62
                             #BPM 120
                             #WAV00 bomb.wav
                             #001D3:0a0A
                             """);
        var mines = beatmap.HitObjects.OfType<BmsHitObject>().Where(h => h.IsMine).OrderBy(h => h.StartTime).ToList();

        Assert.That(mines, Has.Count.EqualTo(2));
        Assert.That(mines[0].LandmineDamagePercent, Is.EqualTo(5), "'0a' should decode to 5%");
        Assert.That(mines[1].LandmineDamagePercent, Is.EqualTo(5), "'0A' should decode to 5%");
    }

    [Test]
    public void TestBase62LandmineChannelZzIsInstantDeath()
    {
        // ZZ in 36-base = 35×36+35 = 1295 → exceeds max → instant death.
        var beatmap = decode("""
                             #BASE 62
                             #BPM 120
                             #WAV00 bomb.wav
                             #001D3:ZZ
                             """);
        var mine = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(mine.IsMine, Is.True);
        Assert.That(mine.LandmineDamagePercent, Is.EqualTo(647.5));
        // (At 647.5, the health processor triggers instant death.)
    }

    [Test]
    public void TestBaseBpmOverridesScrollReference()
    {
        // #BASEBPM should override the scroll reference BPM without affecting note timing.
        var beatmap = decode("""
                             #BPM 120
                             #BASEBPM 200
                             #00111:01
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var timingMap = converted.TimingMap!;
        var note = (BmsHitObject)beatmap.HitObjects.Single();

        // Scroll reference uses #BASEBPM, not the header #BPM
        Assert.That(timingMap.ScrollReferenceBpm, Is.EqualTo(200).Within(0.000001));

        // Note timing is still driven by #BPM 120
        Assert.That(note.StartTime, Is.EqualTo(2000).Within(0.001));
    }

    [Test]
    public void TestBaseBpmScrollDistance()
    {
        // Verify scroll distance at #BASEBPM 200 vs standard #BPM 120.
        // A measure (192 ticks) at base BPM 200 → scroll = 192 * 60000/200 / (192/4) = 192 * 300 / 48 = 1200
        // A measure (192 ticks) at base BPM 120 → scroll = 192 * 60000/120 / (192/4) = 192 * 500 / 48 = 2000
        var beatmap = decode("""
                             #BPM 120
                             #BASEBPM 200
                             #00111:01
                             #00211:01
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var timingMap = converted.TimingMap!;

        var scrollDistance = timingMap.GetScrollPositionAtTick(192) - timingMap.GetScrollPositionAtTick(0);

        // With base BPM 200, 192 ticks → 1200 scroll units
        Assert.That(scrollDistance, Is.EqualTo(1200).Within(0.001));
    }

    [Test]
    public void TestBaseBpmWithNoScrollOverride()
    {
        // Without #BASEBPM, scroll reference falls back to header #BPM.
        var beatmap = decode("""
                             #BPM 120
                             #00111:01
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var timingMap = converted.TimingMap!;

        Assert.That(timingMap.ScrollReferenceBpm, Is.EqualTo(120).Within(0.000001));
    }

    [Test]
    public void TestBaseBpmWithZeroValueIsIgnored()
    {
        // #BASEBPM with an invalid / zero value should be ignored.
        var beatmap = decode("""
                             #BPM 150
                             #BASEBPM 0
                             #00111:01
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var timingMap = converted.TimingMap!;

        Assert.That(timingMap.ScrollReferenceBpm, Is.EqualTo(150).Within(0.000001));
    }

    [Test]
    public void TestBmsDecoderRegisteredWithoutRulesetInstantiation()
    {
        using var memoryStream = new MemoryStream("""
                                                  #TITLE Global Decoder Registration
                                                  #BPM 120
                                                  #00111:01
                                                  """u8.ToArray());
        using var reader = new LineBufferedReader(memoryStream);

        var decoded = Decoder.GetDecoder<Beatmap>(reader).Decode(reader);

        Assert.That(decoded.HitObjects.OfType<BmsHitObject>().Count(), Is.EqualTo(1));
        Assert.That(decoded.Metadata.Title, Is.EqualTo("Global Decoder Registration"));
    }

    [Test]
    public void TestBmsePseudoSpeedSampleDecodesWithScrollEvents()
    {
        var beatmap = decodeResource("osu.Game.Rulesets.BmsRuleset.Tests.Resources.bms_test_songs.bmse_speed_samples.pseudo_speed_sample1.bms");
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var timingMap = converted.TimingMap!;

        // #SCROLL01 through #SCROLL75 = 257 definitions, stepped 1.0 → 10.0
        // 8 measures × 32 cells + 1 cell at measure 9 = 257 events
        Assert.That(timingMap.ScrollEvents, Has.Count.EqualTo(257));
        Assert.That(timingMap.ScrollEvents[0].Factor, Is.EqualTo(1.0));
        Assert.That(timingMap.ScrollEvents[^1].Factor, Is.EqualTo(10.0));
        Assert.That(timingMap.SpeedEvents, Is.Empty);

        // Notes should still decode correctly
        var objects = converted.HitObjects;
        Assert.That(objects, Has.Count.GreaterThan(100));
        Assert.That(objects, Is.Ordered.By(nameof(BmsHitObject.StartTime)));
    }

    [Test]
    public void TestBmseSpeedGradualSampleDecodesWithSpeedEvents()
    {
        var beatmap = decodeResource("osu.Game.Rulesets.BmsRuleset.Tests.Resources.bms_test_songs.bmse_speed_samples.speed_sample1.bms");
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var timingMap = converted.TimingMap!;

        // #SPEED01 through #SPEED75 = 257 definitions, stepped 1.0 → 10.0
        Assert.That(timingMap.SpeedEvents, Has.Count.EqualTo(257));
        Assert.That(timingMap.SpeedEvents[0].Factor, Is.EqualTo(1.0));
        Assert.That(timingMap.SpeedEvents[^1].Factor, Is.EqualTo(10.0));
        Assert.That(timingMap.ScrollEvents, Is.Empty);

        var objects = converted.HitObjects;
        Assert.That(objects, Has.Count.GreaterThan(100));
        Assert.That(objects, Is.Ordered.By(nameof(BmsHitObject.StartTime)));
    }

    [Test]
    public void TestBmseSpeedSampleSpeedFactorChange()
    {
        var beatmap = decodeResource("osu.Game.Rulesets.BmsRuleset.Tests.Resources.bms_test_songs.bmse_speed_samples.speed_sample2.bms");
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var timingMap = converted.TimingMap!;

        Assert.That(timingMap.SpeedEvents, Has.Count.EqualTo(2));
        Assert.That(timingMap.SpeedEvents[0].Factor, Is.EqualTo(1.0));
        Assert.That(timingMap.SpeedEvents[1].Factor, Is.EqualTo(10.0));

        // #001SP:01 at measure 1 (tick 192, time 2000ms).
        // #009SP:02 at measure 9 (tick 1728, time 18000ms).
        Assert.That(timingMap.GetSpeedFactorAtTime(1000), Is.EqualTo(1.0).Within(0.001));
        Assert.That(timingMap.GetSpeedFactorAtTime(10000), Is.EqualTo(1.0).Within(0.001));
        Assert.That(timingMap.GetSpeedFactorAtTime(19000), Is.EqualTo(10.0).Within(0.001));
    }

    [Test]
    public void TestBmseSpeedTwoStepSampleDecodesWithSpeedEvents()
    {
        var beatmap = decodeResource("osu.Game.Rulesets.BmsRuleset.Tests.Resources.bms_test_songs.bmse_speed_samples.speed_sample2.bms");
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var timingMap = converted.TimingMap!;

        // #SPEED01 = 1 at measure 1, #SPEED02 = 10 at measure 9
        Assert.That(timingMap.SpeedEvents, Has.Count.EqualTo(2));
        Assert.That(timingMap.SpeedEvents[0].Factor, Is.EqualTo(1.0));
        Assert.That(timingMap.SpeedEvents[1].Factor, Is.EqualTo(10.0));
        Assert.That(timingMap.ScrollEvents, Is.Empty);

        var objects = converted.HitObjects;
        Assert.That(objects, Has.Count.GreaterThan(100));
        Assert.That(objects, Is.Ordered.By(nameof(BmsHitObject.StartTime)));
    }

    [Test]
    public void TestBranchReplayModAppliesDecisionsToConverter()
    {
        var converter = new BmsBeatmapConverter(decode("#BPM 120\n#00111:01"), new BmsRuleset());
        var mod = new BmsModBranchReplay
        {
            Decisions = { Value = "2:1" },
        };

        mod.ApplyToBeatmapConverter(converter);

        Assert.That(converter.BranchReplayDecisions, Is.EqualTo("2:1"));
    }

    [Test]
    public void TestChannelInsideRandomButOutsideIfRemainsActive()
    {
        var beatmap = decode("""
                             #BPM 120
                             #RANDOM 2
                             #00111:01
                             #IF 2
                             #00112:02
                             #ENDIF
                             #00113:03
                             #ENDRANDOM
                             """, _ => 1);

        Assert.That(beatmap.HitObjects.Cast<BmsHitObject>().Select(h => h.SourceChannel), Is.EqualTo([BmsChartParser.Enc("11"), BmsChartParser.Enc("13")]));
    }

    [Test]
    public void TestCommentGoesToTags()
    {
        var beatmap = decode("""
                             #TITLE Test
                             #BPM 120
                             #COMMENT A comment about the chart
                             #00111:01
                             """);
        Assert.That(beatmap.Metadata.Tags, Does.Contain("A comment about the chart"));
    }

    [Test]
    public void TestConverterMaterialisesBranchDecisionsAtPlayConversion()
    {
        var decoded = decode("""
                             #BPM 120
                             #RANDOM 2
                             #IF 1
                             #00111:01
                             #ENDIF
                             #IF 2
                             #00112:02
                             #ENDIF
                             #ENDRANDOM
                             """, _ => 1);
        var converter = new BmsBeatmapConverter(decoded, new BmsRuleset())
        {
            BranchRandomValueSelector = _ => 2,
        };
        var converted = (BmsBeatmap)converter.Convert();
        var note = converted.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo(BmsChartParser.Enc("12")));
        Assert.That(converted.BranchDecisions, Is.EqualTo([new BmsBranchDecision(2, 2)]));
    }

    [Test]
    public void TestConverterPreservesNativeTimingMap()
    {
        var beatmap = decode("""
                             #BPM 120
                             #BPM01 240
                             #00108:01
                             #00211:01
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(converted.TimingMap, Is.Not.Null);
        Assert.That(converted.TimingMap!.BpmEvents.Select(e => e.Bpm), Is.EqualTo([120, 240]));
    }

    [Test]
    public void TestConverterStoresInferredKeyCountMetadata()
    {
        var beatmap = new Beatmap
        {
            HitObjects =
            {
                new BmsHitObject { SourceChannel = BmsChartParser.Enc("19") },
            },
        };

        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(converted.TotalColumns, Is.EqualTo(8));
        Assert.That(converted.Difficulty.CircleSize, Is.EqualTo(8));
        Assert.That(converted.BeatmapInfo.Difficulty.CircleSize, Is.EqualTo(8));
    }

    [Test]
    public void TestConverterUsesReplayBranchDecisions()
    {
        var decoded = decode("""
                             #BPM 120
                             #RANDOM 2
                             #IF 1
                             #00111:01
                             #ENDIF
                             #IF 2
                             #00112:02
                             #ENDIF
                             #ENDRANDOM
                             """, _ => 1);
        var converter = new BmsBeatmapConverter(decoded, new BmsRuleset())
        {
            BranchReplayDecisions = "2:2",
        };

        var converted = (BmsBeatmap)converter.Convert();
        var note = converted.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo(BmsChartParser.Enc("12")));
        Assert.That(note.SampleKey, Is.EqualTo(BmsChartParser.Enc("02")));
        Assert.That(converted.BranchDecisions, Is.EqualTo([new BmsBranchDecision(2, 2)]));
    }

    [Test]
    public void TestDecoderAndConverterPreserveBmsSampleDefinitionsAndBgmEvents()
    {
        var beatmap = decode("""
                             #BPM 120
                             #WAV01 kick.wav
                             #WAV02 bgm.ogg
                             #00101:02
                             #00111:01
                             """);
        var hitObject = (BmsHitObject)beatmap.HitObjects.Single();
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(hitObject.SamplePath, Is.EqualTo("kick.wav"));
        Assert.That(converted.SampleDefinitions[BmsChartParser.Enc("01")], Is.EqualTo("kick.wav"));
        Assert.That(converted.SampleDefinitions[BmsChartParser.Enc("02")], Is.EqualTo("bgm.ogg"));
        Assert.That(converted.BackgroundSampleEvents, Has.Count.EqualTo(1));
        Assert.That(converted.BackgroundSampleEvents[0].SampleKey, Is.EqualTo(BmsChartParser.Enc("02")));
        Assert.That(converted.BackgroundSampleEvents[0].Time, Is.EqualTo(2000).Within(0.001));
    }

    [Test]
    public void TestDecoderParsesLandmineChannels()
    {
        var beatmap = decode("""
                             #BPM 120
                             #WAV00 bomb.wav
                             #001D3:0000001E
                             """);

        var mine = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(mine.IsMine, Is.True);
        Assert.That(mine.Column, Is.EqualTo(3));
        Assert.That(mine.SourceChannel, Is.EqualTo(BmsChartParser.Enc("D3")));
        Assert.That(mine.SampleKey, Is.EqualTo(BmsChartParser.Enc("1E")));
        Assert.That(mine.SamplePath, Is.Empty);
        Assert.That(mine.LandmineDamagePercent, Is.EqualTo(25));
        Assert.That(mine.LandmineExplosionSamplePath, Is.EqualTo("bomb.wav"));
    }

    [Test]
    public void TestDecoderParsesSecondPlayerLandmineChannels()
    {
        var beatmap = decode("""
                             #BPM 120
                             #00129:01
                             #001E1:0A
                             """);

        var mine = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(beatmap.HitObjects, Has.Count.EqualTo(1));
        Assert.That(mine.IsMine, Is.True);
        Assert.That(mine.Column, Is.EqualTo(6));
        Assert.That(mine.SourceChannel, Is.EqualTo(BmsChartParser.Enc("E1")));
        Assert.That(mine.LandmineDamagePercent, Is.EqualTo(5));
    }

    [Test]
    public void TestDecoderPreservesSamplePathWithSubdirectory()
    {
        // BMS charts may use relative paths with subdirectories in #WAV definitions
        // (e.g. #WAV01 wav/kick.wav). The parser must preserve the full path as-is.
        var beatmap = decode("""
                             #BPM 120
                             #WAV01 kick.wav
                             #WAV02 wav/kick.wav
                             #WAV03 subdir/sample.wav
                             #WAV04 a/b/c.wav
                             #00111:01
                             #00112:02
                             #00113:03
                             #00114:04
                             """);
        var hitObjects = beatmap.HitObjects.OfType<BmsHitObject>().OrderBy(h => h.StartTime).ToList();
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        // Flat filename (no subdir) — baseline.
        Assert.That(hitObjects[0].SamplePath, Is.EqualTo("kick.wav"));
        Assert.That(converted.SampleDefinitions[BmsChartParser.Enc("01")], Is.EqualTo("kick.wav"));

        // Single subdirectory level.
        Assert.That(hitObjects[1].SamplePath, Is.EqualTo("wav/kick.wav"));
        Assert.That(converted.SampleDefinitions[BmsChartParser.Enc("02")], Is.EqualTo("wav/kick.wav"));

        // Single subdirectory, different path.
        Assert.That(hitObjects[2].SamplePath, Is.EqualTo("subdir/sample.wav"));
        Assert.That(converted.SampleDefinitions[BmsChartParser.Enc("03")], Is.EqualTo("subdir/sample.wav"));

        // Nested subdirectories.
        Assert.That(hitObjects[3].SamplePath, Is.EqualTo("a/b/c.wav"));
        Assert.That(converted.SampleDefinitions[BmsChartParser.Enc("04")], Is.EqualTo("a/b/c.wav"));
    }

    [Test]
    public void TestDefaultScrollAndSpeedAreUnity()
    {
        var beatmap = decode("""
                             #BPM 120
                             #00111:01
                             #00211:02
                             """);
        var bmsBeatmap = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var timingMap = bmsBeatmap.TimingMap!;

        Assert.That(timingMap.ScrollEvents, Is.Empty);
        Assert.That(timingMap.SpeedEvents, Is.Empty);
    }

    [Test]
    public void TestEndIfVariantsWithElseFlow()
    {
        // All three typo variants should correctly close an #IF block
        // so that a subsequent #ELSEIF/#ELSE belongs to the next #IF, not to the closed one.
        var beatmap = decode("""
                             #BPM 120
                             #RANDOM 2
                             #IF 1
                             #00111:01
                             #END
                             #ELSEIF 2
                             #00112:02
                             #IFEND
                             #END IF
                             #ENDRANDOM
                             """, _ => 2);

        // With selector=2, #IF 1 is skipped → lines inside it are inactive.
        // #ELSEIF with #END (from line 6) → closes a non-existent #IF, no-op.
        // Then #ELSEIF 2 matches → note 12:02 is active.
        // #IFEND closes that. #END IF is another no-op (no open #IF).
        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo(BmsChartParser.Enc("12")));
        Assert.That(note.SampleKey, Is.EqualTo(BmsChartParser.Enc("02")));
    }

    [Test]
    public void TestEndIfWithHashEnd()
    {
        // #END should be treated as #ENDIF
        var beatmap = decode("""
                             #BPM 120
                             #RANDOM 2
                             #IF 1
                             #00111:01
                             #END
                             #IF 2
                             #00112:02
                             #END
                             #ENDRANDOM
                             """, _ => 1);

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo(BmsChartParser.Enc("11")));
        Assert.That(note.SampleKey, Is.EqualTo(BmsChartParser.Enc("01")));
    }

    [Test]
    public void TestEndIfWithHashEndSpaceIf()
    {
        // #END IF (with space) should be treated as #ENDIF
        var beatmap = decode("""
                             #BPM 120
                             #RANDOM 2
                             #IF 1
                             #00111:01
                             #END IF
                             #IF 2
                             #00112:02
                             #END IF
                             #ENDRANDOM
                             """, _ => 1);

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo(BmsChartParser.Enc("11")));
        Assert.That(note.SampleKey, Is.EqualTo(BmsChartParser.Enc("01")));
    }

    [Test]
    public void TestEndIfWithHashIfEnd()
    {
        // #IFEND should be treated as #ENDIF
        var beatmap = decode("""
                             #BPM 120
                             #RANDOM 2
                             #IF 1
                             #00111:01
                             #IFEND
                             #IF 2
                             #00112:02
                             #IFEND
                             #ENDRANDOM
                             """, _ => 1);

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo(BmsChartParser.Enc("11")));
        Assert.That(note.SampleKey, Is.EqualTo(BmsChartParser.Enc("01")));
    }

    [Test]
    public void TestExtendedBpmChangesProjectTimes()
    {
        var beatmap = decode("""
                             #BPM 120
                             #BPM01 240
                             #00108:01
                             #00211:01
                             """);

        var note = (BmsHitObject)beatmap.HitObjects.Single();
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var timingMap = converted.TimingMap!;

        Assert.That(beatmap.ControlPointInfo.TimingPoints, Has.Count.EqualTo(2));
        Assert.That(beatmap.ControlPointInfo.TimingPoints[0].BPM, Is.EqualTo(120).Within(0.001));
        Assert.That(beatmap.ControlPointInfo.TimingPoints[1].BPM, Is.EqualTo(240).Within(0.001));
        Assert.That(timingMap.BpmEvents.Select(e => e.Bpm), Is.EqualTo([120, 240]));
        Assert.That(note.StartTime, Is.EqualTo(3000).Within(0.001));
    }

    [Test]
    public void TestGenleIsGenreTypoFallback()
    {
        // #GENLE should be treated as #GENRE (song genre → Tags)
        var beatmap = decode("""
                             #TITLE Test
                             #ARTIST Me
                             #GENLE Some Genre
                             #BPM 120
                             #00111:01
                             """);
        Assert.That(beatmap.Metadata.Tags, Does.Contain("Some Genre"));
        Assert.That(beatmap.Metadata.Source, Is.EqualTo("BMS"));
    }

    [Test]
    public void TestGetScrollPositionAtTimeContinuousAtScrollBoundary()
    {
        // Verify GetScrollPositionAtTime has NO discontinuity at SCROLL boundaries.
        // SCROLL 0.5 from tick 0, changes to 2.0 at tick 4800 (time ~18461.5 ms at BPM 130).
        var timingMap = new BmsTimingMap(
            480,
            [],
            [new BmsBpmEvent(0, 130, 0)],
            [],
            [new BmsScrollEvent(0, 0.5, 0), new BmsScrollEvent(4800, 2.0, 0)],
            []);

        var boundaryTime = timingMap.ProjectTickToTime(4800);

        // Just before and just after the boundary — scroll position must be continuous.
        var justBefore = timingMap.GetScrollPositionAtTime(boundaryTime - 0.001);
        var atBoundary = timingMap.GetScrollPositionAtTime(boundaryTime);
        var justAfter = timingMap.GetScrollPositionAtTime(boundaryTime + 0.001);

        Assert.That(atBoundary, Is.EqualTo(justBefore).Within(0.1),
            $"Discontinuity before boundary: before={justBefore:F3}, at={atBoundary:F3}");
        Assert.That(justAfter, Is.EqualTo(atBoundary).Within(0.1),
            $"Discontinuity after boundary: at={atBoundary:F3}, after={justAfter:F3}");
    }

    [Test]
    public void TestInactiveNestedRandomDoesNotConsumeDecision()
    {
        var decisions = new Queue<int>([1]);

        var beatmap = decode("""
                             #BPM 120
                             #RANDOM 2
                             #IF 1
                             #00111:01
                             #ENDIF
                             #IF 2
                             #RANDOM 2
                             #IF 1
                             #00112:02
                             #ENDIF
                             #IF 2
                             #00113:03
                             #ENDIF
                             #ENDRANDOM
                             #ENDIF
                             #ENDRANDOM
                             """, _ => decisions.Dequeue());

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo(BmsChartParser.Enc("11")));
        Assert.That(decisions, Is.Empty);
    }

    [Test]
    public void TestInterMeasureTimeIntervalsAreConsistent()
    {
        // Verify that the time between consecutive measures is correct
        // at constant BPM 120 with TickResolution=192 (each measure = 4 beats = 2000ms).
        var timingMap = new BmsTimingMap(
            192,
            Enumerable.Range(0, 10).Select(i => new BmsMeasureInfo(i, i * 192, 192, 1.0)),
            [new BmsBpmEvent(0, 120, 0)],
            []);

        for (var i = 1; i < 10; i++)
        {
            var prevMeasureTime = timingMap.ProjectTickToTime((i - 1) * 192);
            var currMeasureTime = timingMap.ProjectTickToTime(i * 192);
            var interval = currMeasureTime - prevMeasureTime;

            Assert.That(interval, Is.EqualTo(2000).Within(0.001),
                $"Measure {i - 1} → {i}: expected 2000 ms, got {interval:F3} ms");
        }
    }

    [Test]
    public void TestInterMeasureTimeIntervalsUnaffectedByScroll()
    {
        // SCROLL changes must NOT affect inter-measure time intervals.
        // Timing (ProjectTickToTime) ignores SCROLL entirely.
        var timingMapWithScroll = new BmsTimingMap(
            192,
            Enumerable.Range(0, 5).Select(i => new BmsMeasureInfo(i, i * 192, 192, 1.0)),
            [new BmsBpmEvent(0, 120, 0)],
            [],
            [new BmsScrollEvent(0, 0.5, 0), new BmsScrollEvent(384, 2.0, 0)],
            []);

        var timingMapNoScroll = new BmsTimingMap(
            192,
            Enumerable.Range(0, 5).Select(i => new BmsMeasureInfo(i, i * 192, 192, 1.0)),
            [new BmsBpmEvent(0, 120, 0)],
            []);

        for (var i = 0; i < 4; i++)
        {
            var timeWithScroll = timingMapWithScroll.ProjectTickToTime((i + 1) * 192)
                                 - timingMapWithScroll.ProjectTickToTime(i * 192);
            var timeNoScroll = timingMapNoScroll.ProjectTickToTime((i + 1) * 192)
                               - timingMapNoScroll.ProjectTickToTime(i * 192);

            Assert.That(timeWithScroll, Is.EqualTo(timeNoScroll).Within(0.001),
                $"Measure {i} → {i + 1}: SCROLL must not affect time interval (with={timeWithScroll:F3}, without={timeNoScroll:F3})");
        }
    }

    [Test]
    public void TestInterMeasureTimeIntervalsWithBpmChange()
    {
        // BPM changes from 120 to 240 at tick 384 (start of measure 2).
        // TickRes=192, measure=192 ticks.
        // Measure 0 (tick 0-192):   2000ms at BPM 120
        // Measure 1 (tick 192-384): 2000ms at BPM 120
        // Measure 2 (tick 384-576): 1000ms at BPM 240
        // Measure 3 (tick 576-768): 1000ms at BPM 240
        //
        // BPM event at tick 384: time = 0 + (384-0)*60000/120/48 = 4000 ms
        const double tick384_time = 4000d;
        var timingMap = new BmsTimingMap(
            192,
            Enumerable.Range(0, 5).Select(i => new BmsMeasureInfo(i, i * 192, 192, 1.0)),
            [new BmsBpmEvent(0, 120, 0), new BmsBpmEvent(384, 240, tick384_time)],
            []);

        var intervals = new[] { 2000d, 2000, 1000, 1000 };

        for (var i = 0; i < 4; i++)
        {
            var prevTime = timingMap.ProjectTickToTime(i * 192);
            var currTime = timingMap.ProjectTickToTime((i + 1) * 192);
            var interval = currTime - prevTime;

            Assert.That(interval, Is.EqualTo(intervals[i]).Within(0.001),
                $"Measure {i} → {i + 1}: expected {intervals[i]} ms, got {interval:F3} ms");
        }
    }

    [Test]
    public void TestInterMeasureTimeIntervalsWithStop()
    {
        // STOP at tick 192 (measure boundary) with duration 2000ms freeze.
        // The STOP takes effect after tick 192 is reached, so ProjectTickToTime(192)
        // does NOT include it, but ProjectTickToTime(193) does.
        // At constant BPM 120: ProjectTickToTime(192) = 2000ms (play time, no stop)
        //                     ProjectTickToTime(193) = 2000 + 10.417 + 2000 = 4010.417ms
        var timingMap = new BmsTimingMap(
            192,
            [
                new BmsMeasureInfo(0, 0, 192, 1.0),
                new BmsMeasureInfo(1, 192, 192, 1.0),
            ],
            [new BmsBpmEvent(0, 120, 0)],
            [new BmsStopEvent(192, 2000, 192, 120, 0)]);

        // Time from tick 0 to tick 192: 2000ms play, STOP not included (at the boundary)
        var measure0Time = timingMap.ProjectTickToTime(192) - timingMap.ProjectTickToTime(0);
        Assert.That(measure0Time, Is.EqualTo(2000).Within(0.001),
            $"Tick 0 → 192: expected 2000 ms (STOP is at tick 192, not before), got {measure0Time:F3} ms");

        // Time from tick 192 to tick 384 (includes the 2000ms STOP):
        // play: (384-192) * 60000/120 / 48 = 2000ms, plus STOP: 2000ms = 4000ms total
        var measure1Time = timingMap.ProjectTickToTime(384) - timingMap.ProjectTickToTime(192);
        Assert.That(measure1Time, Is.EqualTo(4000).Within(0.001),
            $"Tick 192 → 384 with STOP: expected 4000 ms (2000 play + 2000 stop), got {measure1Time:F3} ms");
    }

    [Test]
    public void TestInterMeasureTimeIntervalsWithVariableLengthRatios()
    {
        // Measure 1 has 0.5× length (96 ticks at BPM 120 = 1000 ms).
        // Other measures have 1.0× length (192 ticks at BPM 120 = 2000 ms).
        var timingMap = new BmsTimingMap(
            192,
            [
                new BmsMeasureInfo(0, 0, 192, 1.0),
                new BmsMeasureInfo(1, 192, 96, 0.5),
                new BmsMeasureInfo(2, 288, 192, 1.0),
                new BmsMeasureInfo(3, 480, 192, 1.0),
            ],
            [new BmsBpmEvent(0, 120, 0)],
            []);

        // Measure 0: 192 ticks at BPM 120 = 2000ms
        Assert.That(timingMap.ProjectTickToTime(192) - timingMap.ProjectTickToTime(0),
            Is.EqualTo(2000).Within(0.001));
        // Measure 1: 96 ticks at BPM 120 = 1000ms
        Assert.That(timingMap.ProjectTickToTime(288) - timingMap.ProjectTickToTime(192),
            Is.EqualTo(1000).Within(0.001));
        // Measure 2: 192 ticks at BPM 120 = 2000ms
        Assert.That(timingMap.ProjectTickToTime(480) - timingMap.ProjectTickToTime(288),
            Is.EqualTo(2000).Within(0.001));
    }

    [Test]
    public void TestInvalidLnModeValueLeavesUndefined()
    {
        var beatmap = decode("""
                             #LNMODE 4
                             #BPM 120
                             #LNTYPE 1
                             #00151:0102
                             """);
        var decoded = (BmsDecodedBeatmap)beatmap;
        var note = (BmsHitObject)beatmap.HitObjects[0];

        Assert.That(decoded.LockedLongNoteMode, Is.EqualTo(BmsLongNoteMode.Undefined));
        Assert.That(note.IsLongNote, Is.True);
        Assert.That(note.LongNoteMode, Is.EqualTo(BmsLongNoteMode.Undefined));
    }

    [Test]
    public void TestLnMode1ParsesAsLongNote()
    {
        var beatmap = decode("""
                             #LNMODE 1
                             #BPM 120
                             #LNTYPE 1
                             #00151:0102
                             """);
        var decoded = (BmsDecodedBeatmap)beatmap;
        var note = (BmsHitObject)beatmap.HitObjects[0];

        Assert.That(decoded.LockedLongNoteMode, Is.EqualTo(BmsLongNoteMode.LongNote));
        Assert.That(note.IsLongNote, Is.True);
        Assert.That(note.LongNoteMode, Is.EqualTo(BmsLongNoteMode.LongNote));
    }

    [Test]
    public void TestLnMode2ParsesAsChargeNote()
    {
        var beatmap = decode("""
                             #LNMODE 2
                             #BPM 120
                             #LNTYPE 1
                             #00151:0102
                             """);
        var decoded = (BmsDecodedBeatmap)beatmap;
        var note = (BmsHitObject)beatmap.HitObjects[0];

        Assert.That(decoded.LockedLongNoteMode, Is.EqualTo(BmsLongNoteMode.ChargeNote));
        Assert.That(note.IsLongNote, Is.True);
        Assert.That(note.LongNoteMode, Is.EqualTo(BmsLongNoteMode.ChargeNote));
    }

    [Test]
    public void TestLnMode3ParsesAsHellChargeNote()
    {
        var beatmap = decode("""
                             #LNMODE 3
                             #BPM 120
                             #LNTYPE 1
                             #00151:0102
                             """);
        var decoded = (BmsDecodedBeatmap)beatmap;
        var note = (BmsHitObject)beatmap.HitObjects[0];

        Assert.That(decoded.LockedLongNoteMode, Is.EqualTo(BmsLongNoteMode.HellChargeNote));
        Assert.That(note.IsLongNote, Is.True);
        Assert.That(note.LongNoteMode, Is.EqualTo(BmsLongNoteMode.HellChargeNote));
    }

    [Test]
    public void TestLnModeValueZeroLeavesUndefined()
    {
        var beatmap = decode("""
                             #LNMODE 0
                             #BPM 120
                             #LNTYPE 1
                             #00151:0102
                             """);
        var decoded = (BmsDecodedBeatmap)beatmap;

        Assert.That(decoded.LockedLongNoteMode, Is.EqualTo(BmsLongNoteMode.Undefined));
    }

    [Test]
    public void TestLnObjPairsVisibleTerminator()
    {
        var beatmap = decode("""
                             #BPM 120
                             #LNOBJ ZZ
                             #00111:2200
                             #00211:00ZZ
                             """);

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.IsLongNote, Is.True);
        Assert.That(note.SampleKey, Is.EqualTo(BmsChartParser.Enc("22")));
        Assert.That(note.TickInfo.Tick, Is.EqualTo(192));
        Assert.That(note.TickInfo.EndTick, Is.EqualTo(480));
        Assert.That(note.Duration, Is.EqualTo(3000).Within(0.001));
    }

    [Test]
    public void TestLnType1PairsLongNoteChannelObjects()
    {
        var beatmap = decode("""
                             #BPM 120
                             #LNTYPE 1
                             #00151:0102
                             """);

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.IsLongNote, Is.True);
        Assert.That(note.Column, Is.EqualTo(1));
        Assert.That(note.TickInfo.Tick, Is.EqualTo(192));
        Assert.That(note.TickInfo.EndTick, Is.EqualTo(288));
        Assert.That(note.Duration, Is.EqualTo(1000).Within(0.001));
    }

    [Test]
    public void TestLnType2ClosesRunOnZeroCell()
    {
        var beatmap = decode("""
                             #BPM 120
                             #LNTYPE 2
                             #00151:11110000
                             """);

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.IsLongNote, Is.True);
        Assert.That(note.TickInfo.Tick, Is.EqualTo(192));
        Assert.That(note.TickInfo.EndTick, Is.EqualTo(288));
        Assert.That(note.Duration, Is.EqualTo(1000).Within(0.001));
    }

    [Test]
    public void TestLongNoteTailNoSampleEventForLnType2()
    {
        var beatmap = decode("""
                             #BPM 120
                             #WAV01 head.wav
                             #LNTYPE 2
                             #00151:0100
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        // LNTYPE 2 terminates with "00" (control value, no sample) → no tail sample event.
        Assert.That(converted.LongNoteTailSampleEvents, Is.Empty);
    }

    [Test]
    public void TestLongNoteTailSameSampleKeyWithTail()
    {
        var beatmap = decode("""
                             #BPM 120
                             #WAV01 ln.wav
                             #LNTYPE 1
                             #00151:0101
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        // For LNTYPE 1 with payload "0101", both head and tail have value "01",
        // so the tail sample key should not be played.
        Assert.That(converted.HitObjects[0].IsLongNote, Is.True);
        Assert.That(converted.HitObjects[0].SampleKey, Is.EqualTo(BmsChartParser.Enc("01")));
        Assert.That(converted.LongNoteTailSampleEvents, Has.Count.EqualTo(0));
    }

    [Test]
    public void TestLongNoteTailSampleEventWithDistinctTailSample()
    {
        var beatmap = decode("""
                             #BPM 120
                             #WAV01 head.wav
                             #WAV02 tail.wav
                             #LNTYPE 1
                             #00151:0102
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        // Terminating cell has value "02" → tail sample key is "02", NOT head's "01".
        Assert.That(converted.LongNoteTailSampleEvents, Has.Count.EqualTo(1));
        Assert.That(converted.LongNoteTailSampleEvents[0].SampleKey, Is.EqualTo(BmsChartParser.Enc("02")));
    }

    [Test]
    public void TestLongNoteTailSamplePathOnHitObject()
    {
        var beatmap = decode("""
                             #BPM 120
                             #WAV01 head.wav
                             #WAV02 tail.wav
                             #LNTYPE 1
                             #00151:0102
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var note = converted.HitObjects.Single();

        Assert.That(note.IsLongNote, Is.True);
        Assert.That(note.TailSampleKey, Is.EqualTo(BmsChartParser.Enc("02")));
        Assert.That(note.TailSamplePath, Is.EqualTo("tail.wav"));
    }

    [Test]
    public void TestLongNoteTailSamplePathWithNoWavForTailValue()
    {
        var beatmap = decode("""
                             #BPM 120
                             #WAV01 head.wav
                             #LNTYPE 1
                             #00151:0103
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var note = converted.HitObjects.Single();

        Assert.That(note.IsLongNote, Is.True);
        // Terminating value "03" has no #WAV definition → TailSamplePath should be empty
        Assert.That(note.TailSampleKey, Is.EqualTo(BmsChartParser.Enc("03")));
        Assert.That(note.TailSamplePath, Is.Empty);
        // No tail sample event either since the sample can't be resolved
        Assert.That(converted.LongNoteTailSampleEvents, Is.Empty);
    }

    [Test]
    public void TestLongNoteWithoutLnModeGetsUndefined()
    {
        // Non-long notes should not be affected
        var beatmap = decode("""
                             #LNMODE 2
                             #BPM 120
                             #00111:01
                             """);
        var note = (BmsHitObject)beatmap.HitObjects[0];

        Assert.That(note.IsLongNote, Is.False);
        Assert.That(note.LongNoteMode, Is.EqualTo(BmsLongNoteMode.Undefined));
    }

    [Test]
    public void TestMakerSetsAuthor()
    {
        var beatmap = decode("""
                             #TITLE Test
                             #MAKER ChartCreator
                             #BPM 120
                             #00111:01
                             """);
        Assert.That(beatmap.Metadata.Author.Username, Is.EqualTo("ChartCreator"));
    }

    [Test]
    public void TestMeasureZeroStartsAtTimeZeroAndMeasureOneStartsAfterOneMeasure()
    {
        var beatmap = decode("""
                             #BPM 120
                             #00011:01
                             #00112:02
                             """);
        var first = (BmsHitObject)beatmap.HitObjects[0];
        var second = (BmsHitObject)beatmap.HitObjects[1];

        Assert.That(first.TickInfo.Tick, Is.EqualTo(0));
        Assert.That(first.StartTime, Is.EqualTo(0).Within(0.001));
        Assert.That(second.TickInfo.Tick, Is.EqualTo(192));
        Assert.That(second.StartTime, Is.EqualTo(2000).Within(0.001));
    }

    [Test]
    public void TestMissingLnModeLeavesUndefined()
    {
        var beatmap = decode("""
                             #BPM 120
                             #LNTYPE 1
                             #00151:0102
                             """);
        var decoded = (BmsDecodedBeatmap)beatmap;
        var note = (BmsHitObject)beatmap.HitObjects[0];

        Assert.That(decoded.LockedLongNoteMode, Is.EqualTo(BmsLongNoteMode.Undefined));
        Assert.That(note.IsLongNote, Is.True);
        Assert.That(note.LongNoteMode, Is.EqualTo(BmsLongNoteMode.Undefined));
    }

    [Test]
    public void TestMultiLnObjWithDistinctTailSamples()
    {
        var beatmap = decode("""
                             #BPM 120
                             #WAVaa onkeydown1.wav
                             #WAVbb onkeyup1.wav
                             #WAVcc onkeydown2.wav
                             #WAVdd onkeyup2.wav
                             #LNOBJ BB
                             #LNOBJ DD
                             #00111:00aa00bb
                             #00213:00cc00dd
                             """);

        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var notes = converted.HitObjects.OrderBy(h => h.StartTime).ToList();

        Assert.That(notes, Has.Count.EqualTo(2));

        // First LN: head=aa, tail=bb
        Assert.That(notes[0].IsLongNote, Is.True);
        Assert.That(notes[0].SampleKey, Is.EqualTo(BmsChartParser.Enc("aa")));
        Assert.That(notes[0].TailSampleKey, Is.EqualTo(BmsChartParser.Enc("bb")));
        Assert.That(notes[0].TailSamplePath, Is.EqualTo("onkeyup1.wav"));

        // Second LN: head=cc, tail=dd
        Assert.That(notes[1].IsLongNote, Is.True);
        Assert.That(notes[1].SampleKey, Is.EqualTo(BmsChartParser.Enc("cc")));
        Assert.That(notes[1].TailSampleKey, Is.EqualTo(BmsChartParser.Enc("dd")));
        Assert.That(notes[1].TailSamplePath, Is.EqualTo("onkeyup2.wav"));
    }

    [Test]
    public void TestNativeTimingMapBpmChangesScrollSpeed()
    {
        var timingMap = new BmsTimingMap(
            192,
            [],
            [new BmsBpmEvent(0, 130, 0), new BmsBpmEvent(192, 260, 0)],
            []);

        const double one_measure_at130 = 60000d / 130 * 4;
        var halfMeasureAt130Scroll = timingMap.GetScrollPositionAtTime(one_measure_at130 / 2) - timingMap.GetScrollPositionAtTime(0);
        var oneMeasureAt260Scroll = timingMap.GetScrollPositionAtTime(one_measure_at130 + one_measure_at130 / 2) - timingMap.GetScrollPositionAtTime(one_measure_at130);

        Assert.That(oneMeasureAt260Scroll, Is.EqualTo(halfMeasureAt130Scroll * 2).Within(0.001));
    }

    [Test]
    public void TestNativeTimingMapHandlesExtremeHighBpmScrollSpeed()
    {
        var timingMap = new BmsTimingMap(
            192,
            [],
            [new BmsBpmEvent(0, 1_000_000, 0)],
            []);

        const double one_measure_at_extreme_bpm = 60000d / 1_000_000 * 4;

        Assert.That(timingMap.GetScrollPositionAtTime(one_measure_at_extreme_bpm), Is.EqualTo(timingMap.GetScrollPositionAtTick(192)).Within(0.001));
    }

    [Test]
    public void TestNativeTimingMapIgnoresZeroBpmEventForScrollSpeed()
    {
        var timingMap = new BmsTimingMap(
            192,
            [],
            [new BmsBpmEvent(0, 120, 0), new BmsBpmEvent(192, 0, 2000)],
            []);

        var scrollBeforeZero = timingMap.GetScrollPositionAtTime(3000) - timingMap.GetScrollPositionAtTime(2000);
        var expectedAt120Bpm = timingMap.GetScrollPositionAtTick(288) - timingMap.GetScrollPositionAtTick(192);

        Assert.That(scrollBeforeZero, Is.EqualTo(expectedAt120Bpm).Within(0.001));
    }

    [Test]
    public void TestNativeTimingMapKeepsTickSpacingAcrossBpmChanges()
    {
        var timingMap = new BmsTimingMap(
            192,
            [],
            [new BmsBpmEvent(0, 130, 0), new BmsBpmEvent(192, 260, 0)],
            []);

        var firstMeasureDistance = timingMap.GetScrollPositionAtTick(192) - timingMap.GetScrollPositionAtTick(0);
        var secondMeasureDistance = timingMap.GetScrollPositionAtTick(384) - timingMap.GetScrollPositionAtTick(192);

        Assert.That(secondMeasureDistance, Is.EqualTo(firstMeasureDistance).Within(0.001));
    }

    [Test]
    public void TestNativeTimingMapPreservesStopsAndMeasureLengths()
    {
        var beatmap = decode("""
                             #BPM 120
                             #STOP01 192
                             #00102:0.5
                             #00109:01
                             #00211:01
                             """);
        var note = (BmsHitObject)beatmap.HitObjects.Single();
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var timingMap = converted.TimingMap!;

        Assert.That(timingMap.TickResolution, Is.EqualTo(192));
        Assert.That(timingMap.Measures.Single(m => m.Index == 1).LengthRatio, Is.EqualTo(0.5));
        Assert.That(timingMap.Measures.Single(m => m.Index == 1).LengthTicks, Is.EqualTo(96));
        Assert.That(timingMap.StopEvents, Has.Count.EqualTo(1));
        Assert.That(timingMap.StopEvents[0].Tick, Is.EqualTo(192));
        Assert.That(timingMap.StopEvents[0].StopValue, Is.EqualTo(192));
        Assert.That(timingMap.StopEvents[0].Bpm, Is.EqualTo(120));
        Assert.That(timingMap.StopEvents[0].Duration, Is.EqualTo(2000).Within(0.001));
        Assert.That(note.TickInfo.Tick, Is.EqualTo(288));
        Assert.That(note.StartTime, Is.EqualTo(5000).Within(0.001));
    }

    [Test]
    public void TestNativeTimingMapPreservesSubOneBpmScrollSpeed()
    {
        var timingMap = new BmsTimingMap(
            192,
            [],
            [new BmsBpmEvent(0, 130, 0), new BmsBpmEvent(0, 0.25, 0, 1)],
            []);

        const double one_beat_at_quarter_bpm = 60000d / 0.25;

        Assert.That(timingMap.ScrollReferenceBpm, Is.EqualTo(130).Within(0.000001));
        Assert.That(timingMap.GetScrollPositionAtTime(one_beat_at_quarter_bpm), Is.EqualTo(timingMap.GetScrollPositionAtTick(48)).Within(0.001));
    }

    [Test]
    public void TestNativeTimingMapStopFreezesScrollPosition()
    {
        var timingMap = new BmsTimingMap(
            192,
            [],
            [new BmsBpmEvent(0, 120, 0)],
            [new BmsStopEvent(192, 2000, 192, 120, 0)]);

        var stoppedPosition = timingMap.GetScrollPositionAtTick(192);

        Assert.That(timingMap.GetScrollPositionAtTime(2500), Is.EqualTo(stoppedPosition).Within(0.001));
        Assert.That(timingMap.GetScrollPositionAtTime(3999), Is.EqualTo(stoppedPosition).Within(0.001));
        Assert.That(timingMap.GetScrollPositionAtTime(4500), Is.GreaterThan(stoppedPosition));
    }

    [Test]
    public void TestNativeTimingMapUsesHeaderBpmAsScrollReference()
    {
        var timingMap = new BmsTimingMap(
            192,
            [],
            [new BmsBpmEvent(0, 120, 0), new BmsBpmEvent(0, 240, 0, 1)],
            []);

        Assert.That(timingMap.ScrollReferenceBpm, Is.EqualTo(120));
        Assert.That(timingMap.GetScrollPositionAtTick(192), Is.EqualTo(2000).Within(0.001));
    }

    [Test]
    public void TestNestedRandomUsesIndependentBranchDecisions()
    {
        var decisions = new Queue<int>([2, 1]);

        var beatmap = decode("""
                             #BPM 120
                             #RANDOM 2
                             #IF 1
                             #00111:01
                             #ENDIF
                             #IF 2
                             #RANDOM 2
                             #IF 1
                             #00112:02
                             #ENDIF
                             #IF 2
                             #00113:03
                             #ENDIF
                             #ENDRANDOM
                             #ENDIF
                             #ENDRANDOM
                             """, _ => decisions.Dequeue());

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo(BmsChartParser.Enc("12")));
        Assert.That(note.SampleKey, Is.EqualTo(BmsChartParser.Enc("02")));
        Assert.That(decisions, Is.Empty);
    }

    [Test]
    public void TestPmsDoublePlayerMetadataUsesEighteenKeys()
    {
        var beatmap = new Beatmap
        {
            Difficulty = { CircleSize = 18 },
            HitObjects =
            {
                new BmsHitObject { SourceChannel = BmsChartParser.Enc("16") },
                new BmsHitObject { SourceChannel = BmsChartParser.Enc("21") },
                new BmsHitObject { SourceChannel = BmsChartParser.Enc("29") },
            },
        };

        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(converted.TotalColumns, Is.EqualTo(18));
        Assert.That(converted.HitObjects.Single(h => h.SourceChannel == BmsChartParser.Enc("16")).Column, Is.EqualTo(5));
        Assert.That(converted.HitObjects.Single(h => h.SourceChannel == BmsChartParser.Enc("21")).Column, Is.EqualTo(9));
        Assert.That(converted.HitObjects.Single(h => h.SourceChannel == BmsChartParser.Enc("29")).Column, Is.EqualTo(17));
    }

    [Test]
    public void TestPmsMetadataUsesNineKeysWithoutScratch()
    {
        var beatmap = new Beatmap
        {
            Difficulty = { CircleSize = 9 },
            HitObjects =
            {
                new BmsHitObject { SourceChannel = BmsChartParser.Enc("16") },
                new BmsHitObject { SourceChannel = BmsChartParser.Enc("17") },
                new BmsHitObject { SourceChannel = BmsChartParser.Enc("18") },
                new BmsHitObject { SourceChannel = BmsChartParser.Enc("19") },
            },
        };

        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(converted.TotalColumns, Is.EqualTo(9));
        Assert.That(converted.HitObjects.Single(h => h.SourceChannel == BmsChartParser.Enc("18")).Column, Is.EqualTo(5));
        Assert.That(converted.HitObjects.Single(h => h.SourceChannel == BmsChartParser.Enc("19")).Column, Is.EqualTo(6));
        Assert.That(converted.HitObjects.Single(h => h.SourceChannel == BmsChartParser.Enc("16")).Column, Is.EqualTo(7));
        Assert.That(converted.HitObjects.Single(h => h.SourceChannel == BmsChartParser.Enc("17")).Column, Is.EqualTo(8));
    }

    [Test]
    public void TestPreviewHeaderIsPreservedInBmsData()
    {
        var beatmap = decode("""
                             #TITLE Preview Header
                             #ARTIST Tester
                             #PREVIEW audio/preview.ogg
                             #WAV01 kick.wav
                             #00111:01
                             """);

        Assert.That(beatmap, Is.InstanceOf<IBmsBeatmap>());
        Assert.That(((IBmsBeatmap)beatmap).PreviewFile, Is.EqualTo("audio/preview.ogg"));

        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(converted.PreviewFile, Is.EqualTo("audio/preview.ogg"));
    }

    [Test]
    public void TestPreviewHeaderTrimsQuotes()
    {
        var beatmap = decode("""
                             #TITLE Preview Header
                             #ARTIST Tester
                             #PREVIEW "audio/preview.ogg"
                             #WAV01 kick.wav
                             #00111:01
                             """);

        Assert.That(((IBmsBeatmap)beatmap).PreviewFile, Is.EqualTo("audio/preview.ogg"));
    }

    [Test]
    public void TestRandomDecisionsCanVaryBetweenDecodes()
    {
        const string chart = """
                             #BPM 120
                             #RANDOM 2
                             #IF 1
                             #00111:01
                             #ENDIF
                             #IF 2
                             #00112:02
                             #ENDIF
                             #ENDRANDOM
                             """;

        var first = decode(chart, _ => 1);
        var second = decode(chart, _ => 2);

        Assert.That(((BmsHitObject)first.HitObjects.Single()).SourceChannel, Is.EqualTo(BmsChartParser.Enc("11")));
        Assert.That(((BmsHitObject)second.HitObjects.Single()).SourceChannel, Is.EqualTo(BmsChartParser.Enc("12")));
    }

    [Test]
    public void TestRandomIfMaterialisesSelectedBranchOnly()
    {
        var beatmap = decode("""
                             #BPM 120
                             #RANDOM 2
                             #IF 1
                             #00111:01
                             #ENDIF
                             #IF 2
                             #00112:02
                             #ENDIF
                             #ENDRANDOM
                             """, _ => 2);

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo(BmsChartParser.Enc("12")));
        Assert.That(note.SampleKey, Is.EqualTo(BmsChartParser.Enc("02")));
    }

    [Test]
    public void TestRandomInsideSwitchUsesBothBranchDecisions()
    {
        var decisions = new Queue<int>([2, 1]);

        var beatmap = decode("""
                             #BPM 120
                             #SWITCH 2
                             #CASE 1
                             #00111:01
                             #SKIP
                             #CASE 2
                             #RANDOM 2
                             #IF 1
                             #00112:02
                             #ENDIF
                             #IF 2
                             #00113:03
                             #ENDIF
                             #ENDRANDOM
                             #SKIP
                             #ENDSW
                             """, _ => decisions.Dequeue());

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo(BmsChartParser.Enc("12")));
        Assert.That(note.SampleKey, Is.EqualTo(BmsChartParser.Enc("02")));
        Assert.That(decisions, Is.Empty);
    }

    [Test]
    public void TestRankDefaultsToNormalWhenAbsent()
    {
        var beatmap = decode("""
                             #TITLE Test
                             #BPM 130
                             #00111:01
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(converted.Rank, Is.EqualTo(2)); // NORMAL
        Assert.That(converted.HitObjects[0].BmsRank, Is.EqualTo(2));
    }

    [Test]
    public void TestRankParsedFromChart()
    {
        var beatmap = decode("""
                             #RANK 1
                             #TITLE Test
                             #BPM 130
                             #00111:01
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(converted.Rank, Is.EqualTo(1));
        Assert.That(converted.HitObjects[0].BmsRank, Is.EqualTo(1));
    }

    [Test]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    [TestCase(4)]
    public void TestRankPreservedForAllValidValues(int rank)
    {
        var beatmap = decode($"#RANK {rank}\n#BPM 130\n#00111:01");
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(converted.Rank, Is.EqualTo(rank));
    }

    [Test]
    public void TestScrollAndSpeedFactorsIndependent()
    {
        // SCROLL affects scroll position rate. SPEED does NOT — it's a display multiplier.
        var timingMap = new BmsTimingMap(
            192,
            [],
            [new BmsBpmEvent(0, 120, 0)],
            [],
            [new BmsScrollEvent(0, 2.0, 0)],
            [new BmsSpeedEvent(0, 3.0, 0)]);

        Assert.That(timingMap.GetScrollFactorAtTime(1000), Is.EqualTo(2.0));
        Assert.That(timingMap.GetSpeedFactorAtTime(1000), Is.EqualTo(3.0));

        // SCROLL 2× affects rate; SPEED does not.
        var advance = timingMap.GetScrollPositionAtTime(3000) - timingMap.GetScrollPositionAtTime(1000);
        var timingMapBase = new BmsTimingMap(192, [], [new BmsBpmEvent(0, 120, 0)], [], [], []);
        var baseAdvance = timingMapBase.GetScrollPositionAtTime(3000) - timingMapBase.GetScrollPositionAtTime(1000);
        Assert.That(advance, Is.EqualTo(baseAdvance * 2.0).Within(0.001));
    }

    [Test]
    public void TestScrollCommandDefinesFactor()
    {
        var beatmap = decode("""
                             #BPM 120
                             #SCROLL01 0.5
                             #00102:1
                             #002SC:01
                             #00311:01
                             """);
        var bmsBeatmap = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var timingMap = bmsBeatmap.TimingMap!;

        Assert.That(timingMap.ScrollEvents, Has.Count.EqualTo(1));
        Assert.That(timingMap.ScrollEvents[0].Factor, Is.EqualTo(0.5));
        Assert.That(timingMap.ScrollEvents[0].Tick, Is.GreaterThan(0));
    }

    [Test]
    public void TestScrollFactorAffectsScrollPosition()
    {
        // SCROLL changes the scroll coordinate rate (unlike SPEED which is a display multiplier).
        var timingMap = new BmsTimingMap(
            192,
            [],
            [new BmsBpmEvent(0, 120, 0)],
            [],
            [new BmsScrollEvent(0, 2.0, 0)],
            []);

        var advanceWithScroll = timingMap.GetScrollPositionAtTime(3000) - timingMap.GetScrollPositionAtTime(1000);

        var timingMapNoScroll = new BmsTimingMap(192, [], [new BmsBpmEvent(0, 120, 0)], [], [], []);
        var advanceNoScroll = timingMapNoScroll.GetScrollPositionAtTime(3000) - timingMapNoScroll.GetScrollPositionAtTime(1000);

        // Scroll factor 2× doubles the scroll coordinate advance rate.
        Assert.That(advanceWithScroll, Is.EqualTo(advanceNoScroll * 2.0).Within(0.001));
        Assert.That(timingMap.GetScrollFactorAtTime(1000), Is.EqualTo(2.0).Within(0.001));
    }

    [Test]
    public void TestScrollFactorStoredAndQueryable()
    {
        var timingMap = new BmsTimingMap(
            192,
            [],
            [new BmsBpmEvent(0, 120, 0)],
            [],
            [new BmsScrollEvent(192, 0.5, 0), new BmsScrollEvent(384, -1.0, 0)],
            []);

        // Tick 192 at BPM 120 → time 2000ms. Before that, default 1.0.
        Assert.That(timingMap.GetScrollFactorAtTime(500), Is.EqualTo(1.0).Within(0.001));
        Assert.That(timingMap.GetScrollFactorAtTime(2500), Is.EqualTo(0.5).Within(0.001));
        // Tick 384 at BPM 120 → time 4000ms.
        Assert.That(timingMap.GetScrollFactorAtTime(5000), Is.EqualTo(-1.0).Within(0.001));
    }

    [Test]
    public void TestScrollPositionAtTimeMatchesVisualScrollPositionAtTick()
    {
        // When SCROLL != 1.0, GetScrollPositionAtTime(ProjectTickToTime(t))
        // must equal GetVisualScrollPositionAtTick(t) for every tick,
        // otherwise CurrentScrollPosition has discontinuities at SCROLL boundaries.
        // This chart has SCROLL 2× from tick 0, with BPM 120.
        var timingMap = new BmsTimingMap(
            192,
            [],
            [new BmsBpmEvent(0, 120, 0)],
            [],
            [new BmsScrollEvent(0, 2.0, 0)],
            []);

        // verify at several tick positions
        foreach (var tick in new long[] { 0, 192, 384, 960, 1920 })
        {
            var time = timingMap.ProjectTickToTime(tick);
            var fromTime = timingMap.GetScrollPositionAtTime(time);
            var fromTick = timingMap.GetVisualScrollPositionAtTick(tick);
            Assert.That(fromTime, Is.EqualTo(fromTick).Within(0.001),
                $"Mismatch at tick {tick}: GetScrollPositionAtTime({time:F3})={fromTime:F3} vs GetVisualScrollPositionAtTick({tick})={fromTick:F3}");
        }
    }

    [Test]
    public void TestScrollPositionAtTimeMatchesVisualScrollPositionAtTickWithScrollChange()
    {
        // SCROLL changes at tick 4800 from 0.5 to 2.0, BPM 130.
        // This exercises the case where previous segments have SCROLL != 1.0,
        // which previously caused a discontinuity because StartTick was raw BMS ticks.
        var timingMap = new BmsTimingMap(
            480,
            [],
            [new BmsBpmEvent(0, 130, 0)],
            [],
            [new BmsScrollEvent(0, 0.5, 0), new BmsScrollEvent(4800, 2.0, 0)],
            []);

        foreach (var tick in new long[] { 0, 1200, 2400, 4800, 6000, 7200, 9600 })
        {
            var time = timingMap.ProjectTickToTime(tick);
            var fromTime = timingMap.GetScrollPositionAtTime(time);
            var fromTick = timingMap.GetVisualScrollPositionAtTick(tick);
            Assert.That(fromTime, Is.EqualTo(fromTick).Within(0.001),
                $"Mismatch at tick {tick}: GetScrollPositionAtTime({time:F3})={fromTime:F3} vs GetVisualScrollPositionAtTick({tick})={fromTick:F3}");
        }
    }

    [Test]
    public void TestSetRandomAndElseIfElse()
    {
        var beatmap = decode("""
                             #BPM 120
                             #SETRANDOM 3
                             #IF 1
                             #00111:01
                             #ELSEIF 3
                             #00112:02
                             #ELSE
                             #00113:03
                             #ENDIF
                             #ENDRANDOM
                             """);

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo(BmsChartParser.Enc("12")));
        Assert.That(note.SampleKey, Is.EqualTo(BmsChartParser.Enc("02")));
    }

    [Test]
    public void TestSparseSevenKeyChartStoresKeyCountMetadata()
    {
        var beatmap = decode("""
                             #BPM 120
                             #00119:01
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(beatmap.Difficulty.CircleSize, Is.EqualTo(8));
        Assert.That(beatmap.BeatmapInfo.Difficulty.CircleSize, Is.EqualTo(8));
        Assert.That(converted.TotalColumns, Is.EqualTo(8));
        Assert.That(converted.LayoutVariant, Is.EqualTo(BmsLayoutVariant.Bme7K));
        Assert.That(converted.HitObjects.Single().Column, Is.EqualTo(7));
        Assert.That(converted.Difficulty.CircleSize, Is.EqualTo(8));
        Assert.That(converted.BeatmapInfo.Difficulty.CircleSize, Is.EqualTo(8));
    }

    [Test]
    public void TestSpeedCommandDefinesFactor()
    {
        var beatmap = decode("""
                             #BPM 120
                             #SPEED01 2.5
                             #00102:1
                             #002SP:01
                             #00311:01
                             """);
        var bmsBeatmap = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var timingMap = bmsBeatmap.TimingMap!;

        Assert.That(timingMap.SpeedEvents, Has.Count.EqualTo(1));
        Assert.That(timingMap.SpeedEvents[0].Factor, Is.EqualTo(2.5));
    }

    [Test]
    public void TestSpeedFactorDoesNotAffectScrollPosition()
    {
        // SPEED is a ScrollSpeedMultiplier — it does NOT change scroll position coordinates.
        var timingMap = new BmsTimingMap(
            192,
            [],
            [new BmsBpmEvent(0, 120, 0)],
            [],
            [],
            [new BmsSpeedEvent(0, 3.0, 0)]);

        var advance = timingMap.GetScrollPositionAtTime(3000) - timingMap.GetScrollPositionAtTime(1000);

        var timingMapNoSpeed = new BmsTimingMap(192, [], [new BmsBpmEvent(0, 120, 0)], [], [], []);
        var advanceNoSpeed = timingMapNoSpeed.GetScrollPositionAtTime(3000) - timingMapNoSpeed.GetScrollPositionAtTime(1000);

        // Speed does NOT affect scroll position rate.
        Assert.That(advance, Is.EqualTo(advanceNoSpeed).Within(0.001));

        // But GetSpeedFactorAtTime returns the active factor.
        Assert.That(timingMap.GetSpeedFactorAtTime(1000), Is.EqualTo(3.0).Within(0.001));
    }

    [Test]
    public void TestSubArtistAppendedToArtist()
    {
        var beatmap = decode("""
                             #TITLE Test
                             #ARTIST Main
                             #SUBARTIST Feat
                             #BPM 120
                             #00111:01
                             """);
        Assert.That(beatmap.Metadata.Artist, Is.EqualTo("Main (Feat)"));
    }

    [Test]
    public void TestSubtitleAppendedToTitle()
    {
        var beatmap = decode("""
                             #TITLE Main
                             #SUBTITLE Sub
                             #BPM 120
                             #00111:01
                             """);
        Assert.That(beatmap.Metadata.Title, Is.EqualTo("Main - Sub"));
    }

    [Test]
    public void TestSwitchDefaultRunsWhenNoCaseMatches()
    {
        var beatmap = decode("""
                             #BPM 120
                             #SETSWITCH 4
                             #CASE 1
                             #00111:01
                             #SKIP
                             #CASE 2
                             #00112:02
                             #SKIP
                             #DEF
                             #00113:03
                             #ENDSW
                             """);

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo(BmsChartParser.Enc("13")));
        Assert.That(note.SampleKey, Is.EqualTo(BmsChartParser.Enc("03")));
    }

    [Test]
    public void TestSwitchFallsThroughUntilSkip()
    {
        var beatmap = decode("""
                             #BPM 120
                             #SETSWITCH 2
                             #CASE 1
                             #00111:01
                             #SKIP
                             #CASE 2
                             #00112:02
                             #CASE 3
                             #00113:03
                             #SKIP
                             #DEF
                             #00114:04
                             #ENDSW
                             """);

        Assert.That(beatmap.HitObjects.Cast<BmsHitObject>().Select(h => h.SourceChannel), Is.EqualTo([BmsChartParser.Enc("12"), BmsChartParser.Enc("13")]));
    }

    [Test]
    public void TestSwitchInsideRandomUsesBothBranchDecisions()
    {
        var decisions = new Queue<int>([2, 3]);

        var beatmap = decode("""
                             #BPM 120
                             #RANDOM 2
                             #IF 1
                             #00111:01
                             #ENDIF
                             #IF 2
                             #SWITCH 3
                             #CASE 1
                             #00112:02
                             #SKIP
                             #CASE 3
                             #00113:03
                             #SKIP
                             #DEF
                             #00114:04
                             #ENDSW
                             #ENDIF
                             #ENDRANDOM
                             """, _ => decisions.Dequeue());

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo(BmsChartParser.Enc("13")));
        Assert.That(note.SampleKey, Is.EqualTo(BmsChartParser.Enc("03")));
        Assert.That(decisions, Is.Empty);
    }

    [Test]
    public void TestTickResolutionExpandsForPayloadDivisions()
    {
        var beatmap = decode("""
                             #BPM 120
                             #00111:0102030405
                             """);

        var first = (BmsHitObject)beatmap.HitObjects[0];
        var last = (BmsHitObject)beatmap.HitObjects[^1];
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(converted.TickResolution, Is.EqualTo(960));
        Assert.That(converted.TimingMap!.TickResolution, Is.EqualTo(960));
        Assert.That(first.TickInfo.Tick, Is.EqualTo(960));
        Assert.That(last.TickInfo.Tick, Is.EqualTo(1728));
    }

    [Test]
    public void TestTotalDefaultsToZeroWhenAbsent()
    {
        var beatmap = decode("""
                             #BPM 130
                             #00111:01
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(converted.Total, Is.EqualTo(0).Within(0.001));
    }

    [Test]
    public void TestTotalParsedFromChart()
    {
        var beatmap = decode("""
                             #TOTAL 250
                             #BPM 130
                             #00111:01
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(converted.Total, Is.EqualTo(250).Within(0.001));
    }

    [Test]
    public void TestTotalPreservesDecimalValue()
    {
        var beatmap = decode("#TOTAL 160.5\n#BPM 130\n#00111:01");
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(converted.Total, Is.EqualTo(160.5).Within(0.001));
    }

    [Test]
    public void TestUrlAndEmailGoToTags()
    {
        var beatmap = decode("""
                             #TITLE Test
                             #BPM 120
                             %URL https://example.com
                             %EMAIL author@example.com
                             #00111:01
                             """);
        Assert.That(beatmap.Metadata.Tags, Does.Contain("https://example.com"));
        Assert.That(beatmap.Metadata.Tags, Does.Contain("author@example.com"));
    }

    [Test]
    public void TestVisibleNotesDecodeToNativeObjects()
    {
        var beatmap = decode("""
                             #TITLE Decoder Smoke
                             #ARTIST Test Artist
                             #BPM 120
                             #00111:0100
                             #00116:0002
                             """);

        Assert.That(beatmap.Metadata.Title, Is.EqualTo("Decoder Smoke"));
        Assert.That(beatmap.Metadata.Artist, Is.EqualTo("Test Artist"));
        Assert.That(beatmap.HitObjects, Has.Count.EqualTo(2));

        var first = (BmsHitObject)beatmap.HitObjects[0];
        var second = (BmsHitObject)beatmap.HitObjects[1];

        Assert.That(first.Column, Is.EqualTo(1));
        Assert.That(first.SampleKey, Is.EqualTo(BmsChartParser.Enc("01")));
        Assert.That(first.TickInfo.Tick, Is.EqualTo(192));
        Assert.That(first.StartTime, Is.EqualTo(2000).Within(0.001));

        Assert.That(second.Column, Is.EqualTo(0));
        Assert.That(second.SampleKey, Is.EqualTo(BmsChartParser.Enc("02")));
        Assert.That(second.TickInfo.Tick, Is.EqualTo(288));
        Assert.That(second.StartTime, Is.EqualTo(3000).Within(0.001));
    }
}
