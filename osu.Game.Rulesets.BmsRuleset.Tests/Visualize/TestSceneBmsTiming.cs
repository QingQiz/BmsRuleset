#nullable enable
using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsTiming : BmsPlayerTestScene
{
    private float normalSpeedSpacing;
    private string currentChart = timing_chart;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(BmsTestReplays.CreateAutoPlayFrames);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = BmsTestBeatmaps.CreateBeatmapFromChart(currentChart);
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset, endPadding: 3000);
        return beatmap;
    }

    private void seekToTick(long tick, double leadTime = 600)
    {
        var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
        var target = beatmap.HitObjects.First(h => h.TickInfo.Tick >= tick);

        Player.GameplayClockContainer.Seek(target.StartTime - leadTime);
    }

    private const string timing_chart =
        """
        #TITLE Argon Timing Visual
        #ARTIST BMS Ruleset Test
        #BPM 130
        #BPM01 260
        #BPM02 65
        #BPM03 0.25
        #BPM04 1000000
        #BPM05 0
        #STOP01 384
        #LNTYPE 1

        #00111:01000000
        #00112:00010000
        #00113:00000100
        #00114:00000001
        #00118:00000100
        #00119:00000001

        #00208:01
        #00251:01000001
        #00213:00010000
        #00214:00000100
        #00215:00000001
        #00218:01000100

        #00309:01
        #00311:01000000
        #00312:00010000
        #00313:00000100
        #00314:00000001
        #00319:00000100

        #00408:02
        #00458:01000001
        #00411:01000000
        #00412:00010000
        #00413:00000100
        #00414:00000001
        #00415:01000100

        #00511:01000100
        #00512:00010001
        #00513:00000100
        #00514:00000001
        #00518:01000100
        #00519:00010001

        #00608:04
        #00611:01000100
        #00612:00010001
        #00618:01000100

        #00708:05
        #00713:01000100
        #00714:00010001
        #00719:01000100

        #00808:03
        #00811:01000000
        #00812:00010000
        #00813:00000100
        #00814:00000001
        """;

    private DrawableBmsHitObject? getCrossSpeedLongNote()
        => Playfield.AllColumnAliveObjects()
            .OfType<DrawableBmsHitObject>()
            .FirstOrDefault(d => d.HitObject is { IsLongNote: true, TickInfo.Tick: 384 });

    // ── Negative BPM (reverse scroll) ──────────────────────────────────────

    private const string negative_bpm_chart =
        """
        #TITLE Negative BPM Timing Visual
        #ARTIST BMS Ruleset Test
        #BPM 130
        #BPM01 -130
        #LNTYPE 1

        #00111:0100000000000000
        #00112:0001000000000000

        #00211:0100000000000000
        #00212:0001000000000000

        #00308:01
        #00311:0100000000000000
        #00312:0001000000000000

        #00411:0100000000000000
        #00412:0001000000000000
        """;

    [Test]
    public void TestArgonTimingScroll()
    {
        AddStep("load Argon player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        AddAssert("loaded bms drawable ruleset", () => Player.DrawableRuleset, Is.TypeOf<BmsDrawableRuleset>);
        AddAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>);
        AddUntilStep("bms stage loaded", () => Playfield.Stage.IsLoaded);
        AddAssert("measure lines added", () => Playfield.Stage.MeasureLineArea.Count, () => Is.GreaterThan(0));

        AddStep("seek normal BPM", () => seekToTick(192));
        AddUntilStep("normal notes visible", () => Playfield.AllColumnAliveObjects().Count(), () => Is.GreaterThan(0));
        AddUntilStep("normal speed spacing measurable", () => Playfield.SpacingBetweenTicks(192, 240, excludeLongNotes: true), () => Is.GreaterThan(1));
        AddStep("capture normal speed spacing", () => normalSpeedSpacing = Playfield.SpacingBetweenTicks(192, 240, excludeLongNotes: true));

        AddStep("increase scroll speed", () => Playfield.AdjustScrollSpeed(0.5));
        AddAssert("scroll speed increased", () => Playfield.ScrollSpeed, () => Is.GreaterThan(8));
        AddUntilStep("spacing increased", () => Playfield.SpacingBetweenTicks(192, 240, excludeLongNotes: true), () => Is.GreaterThan(normalSpeedSpacing));
        AddStep("decrease scroll speed", () => Playfield.AdjustScrollSpeed(-0.5));
        AddAssert("scroll speed restored", () => Playfield.ScrollSpeed, () => Is.EqualTo(8).Within(0.001));

        AddStep("seek fast BPM", () => seekToTick(384));
        AddUntilStep("fast notes visible", () => Playfield.AllColumnAliveObjects().Count(), () => Is.GreaterThan(0));
        AddUntilStep("cross-speed LN held", () => getCrossSpeedLongNote()?.Judged == false);
        AddAssert("cross-speed LN body above line",
            () => BmsPlayfieldAssertions.BottomOf(getCrossSpeedLongNote()!),
            () => Is.LessThanOrEqualTo(Playfield.JudgementLineY() + 1));

        AddStep("seek STOP freeze", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            var stop = beatmap.TimingMap!.StopEvents[0];
            var stopObject = beatmap.HitObjects.First(h => h.TickInfo.Tick == stop.Tick);

            Player.GameplayClockContainer.Seek(stopObject.StartTime + stop.Duration / 2);
        });
        AddUntilStep("stop notes visible", () => Playfield.AllColumnAliveObjects().Count(), () => Is.GreaterThan(0));

        AddStep("seek slow BPM", () => seekToTick(768));
        AddUntilStep("slow notes visible", () => Playfield.AllColumnAliveObjects().Count(), () => Is.GreaterThan(0));

        AddStep("seek post-LN note", () => seekToTick(1056));
        AddUntilStep("post-LN note alive", () => Playfield.GetAliveObjectAtTick(1056, excludeLongNotes: true) != null);
        AddAssert("post-LN note approaches from above",
            () => BmsPlayfieldAssertions.TopOf(Playfield.GetAliveObjectAtTick(1056, excludeLongNotes: true)!),
            () => Is.LessThanOrEqualTo(Playfield.JudgementLineY() + 1));

        AddStep("seek extreme BPM", () => seekToTick(1152, 20));
        AddUntilStep("extreme BPM notes visible", () => Playfield.AllColumnAliveObjects().Count(), () => Is.GreaterThan(0));

        AddStep("seek zero BPM fallback", () => seekToTick(1344, 20));
        AddUntilStep("zero BPM fallback notes visible", () => Playfield.AllColumnAliveObjects().Count(), () => Is.GreaterThan(0));

        AddStep("seek sub-1 BPM", () => seekToTick(1536, 200));
        AddUntilStep("sub-1 BPM notes visible", () => Playfield.AllColumnAliveObjects().Count(), () => Is.GreaterThan(0));
    }

    [Test]
    public void TestNegativeBpmTiming()
    {
        AddStep("switch to negative BPM chart", () => currentChart = negative_bpm_chart);
        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);

        AddAssert("BPM event parsed", () =>
        {
            var bm = (BmsBeatmap)Player.GameplayState.Beatmap;
            return bm.TimingMap!.BpmEvents.Any(e => e.Bpm < 0);
        });

        AddAssert("negative BPM uses abs for timing", () =>
        {
            var bm = (BmsBeatmap)Player.GameplayState.Beatmap;
            var timingMap = bm.TimingMap!;

            // One measure (192 ticks) at BPM 130 = 192 * 60000/130 / 48 ≈ 1846.15ms
            const double measure_ms = 60000d / 130 * 192 / (192 / 4d);

            var t0 = timingMap.ProjectTickToTime(0);
            var t192 = timingMap.ProjectTickToTime(192);
            if (Math.Abs(t192 - t0 - measure_ms) > 1) return false;

            // Measure 2 (tick 384→576) still at |BPM| 130, even after BPM switches to -130 at tick 576
            var t384 = timingMap.ProjectTickToTime(384);
            var t576 = timingMap.ProjectTickToTime(576);
            return Math.Abs(t576 - t384 - measure_ms) < 1;
        });

        AddAssert("negative BPM reverses scroll direction", () =>
        {
            var bm = (BmsBeatmap)Player.GameplayState.Beatmap;
            var timingMap = bm.TimingMap!;

            // Before change (+130): scroll advances forward.
            var posBefore = timingMap.GetScrollPositionAtTime(1000);
            var posLater = timingMap.GetScrollPositionAtTime(5000);
            if (posLater <= posBefore) return false;

            // After change (-130 at tick 576 ≈ 5538ms): scroll goes backward.
            var posAtChange = timingMap.GetScrollPositionAtTime(5600);
            var posAfter = timingMap.GetScrollPositionAtTime(6600);
            return posAfter < posAtChange;
        });
    }
}
