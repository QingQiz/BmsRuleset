#nullable enable
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsSpeed : BmsPlayerTestScene
{
    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(BmsTestReplays.CreateAutoPlayFrames);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = BmsTestBeatmaps.CreateBeatmapFromChart(speed_chart);
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset, endPadding: 3000);
        return beatmap;
    }

    /// <summary>
    /// Chart testing SPEED as ScrollSpeedMultiplier with gradual changes:
    ///   M1:   default 1.0
    ///   M2:   SPEED 1.5
    ///   M3:   SPEED 2.0
    ///   M4:   SPEED 2.5
    ///   M5:   SPEED 3.0
    ///   M6:   SPEED 1.0  (back to normal)
    ///   M7:   SPEED 0.75
    ///   M8:   SPEED 0.5
    ///   M9:   SPEED 0.25
    ///   M10:  SPEED 0    (freeze)
    /// </summary>
    private const string speed_chart =
        """
        #TITLE BMS Speed Visual
        #ARTIST BMS Ruleset Test
        #BPM 120
        #LNTYPE 1

        #SPEED01 1.5
        #SPEED02 2.0
        #SPEED03 2.5
        #SPEED04 3.0
        #SPEED05 1.0
        #SPEED06 0.75
        #SPEED07 0.5
        #SPEED08 0.25
        #SPEED09 0

        #00111:0100000000000000
        #00112:0001000000000000
        #00113:0000010000000000
        #00114:0000000100000000

        #002SP:01
        #00211:0100000000000000
        #00212:0001000000000000
        #00213:0000010000000000
        #00214:0000000100000000

        #003SP:02
        #00311:0100000000000000
        #00312:0001000000000000
        #00313:0000010000000000
        #00314:0000000100000000

        #004SP:03
        #00411:0100000000000000
        #00412:0001000000000000
        #00413:0000010000000000
        #00414:0000000100000000

        #005SP:04
        #00511:0100000000000000
        #00512:0001000000000000
        #00513:0000010000000000
        #00514:0000000100000000

        #006SP:05
        #00611:0100000000000000
        #00612:0001000000000000
        #00613:0000010000000000
        #00614:0000000100000000

        #007SP:06
        #00711:0100000000000000
        #00712:0001000000000000

        #008SP:07
        #00811:0100000000000000
        #00812:0001000000000000

        #009SP:08
        #00911:0100000000000000
        #00912:0001000000000000

        #010SP:09
        #01011:0100000000000000
        #01012:0001000000000000
        """;

    [Test]
    public void TestSpeedVisual()
    {
        AddStep("load speed player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        AddAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>);
        AddUntilStep("bms stage loaded", () => Playfield.Stage.IsLoaded);

        AddAssert("has 9 speed events", () =>
        {
            var bm = (BmsBeatmap)Player.GameplayState.Beatmap;
            return bm.TimingMap!.SpeedEvents.Count == 9;
        });

        // Speed events: #002SP at tick 384 (4000ms), #003SP at 576 (6000ms), etc.
        // Each measure = 192 ticks = 2000ms at BPM 120. Times checked at midpoint AFTER change.
        var tm = () => ((BmsBeatmap)Player.GameplayState.Beatmap).TimingMap!;

        AddAssert("before M2: speed 1.0 (default)", () => tm().GetSpeedFactorAtTime(500) == 1.0);
        AddAssert("M2: speed 1.5", () => tm().GetSpeedFactorAtTime(5000) == 1.5);
        AddAssert("M3: speed 2.0", () => tm().GetSpeedFactorAtTime(7000) == 2.0);
        AddAssert("M4: speed 2.5", () => tm().GetSpeedFactorAtTime(9000) == 2.5);
        AddAssert("M5: speed 3.0", () => tm().GetSpeedFactorAtTime(11000) == 3.0);
        AddAssert("M6: speed 1.0", () => tm().GetSpeedFactorAtTime(13000) == 1.0);
        AddAssert("M7: speed 0.75", () => tm().GetSpeedFactorAtTime(15000) == 0.75);
        AddAssert("M8: speed 0.5", () => tm().GetSpeedFactorAtTime(17000) == 0.5);
        AddAssert("M9: speed 0.25", () => tm().GetSpeedFactorAtTime(19000) == 0.25);
        AddAssert("M10: speed 0 (freeze)", () => tm().GetSpeedFactorAtTime(21000) == 0.0);
    }
}
