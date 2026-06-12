#nullable enable
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsScroll : BmsPlayerTestScene
{
    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(BmsTestReplays.CreateAutoPlayFrames);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = BmsTestBeatmaps.CreateBeatmapFromChart(scroll_chart);
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset, endPadding: 3000);
        return beatmap;
    }

    /// <summary>
    /// Chart testing SCROLL as per-note display multiplier.
    ///
    ///   M1:  default 1.0
    ///   M2:  SCROLL 1.5
    ///   M3:  SCROLL 2.0
    ///   M4:  SCROLL 2.5
    ///   M5:  SCROLL 3.0
    ///   M6:  SCROLL 1.0
    ///   M7:  SCROLL 0.75
    ///   M8:  SCROLL 0.5
    ///   M9:  SCROLL 0.25
    ///   M10: SCROLL 0
    ///   M11: SCROLL -0.25
    ///   M12: SCROLL -0.5
    ///   M13: SCROLL -0.75
    ///   M14: SCROLL -1.0
    ///   M15: SCROLL -1.5
    ///   M16: SCROLL -2.0
    ///   M17: SCROLL -2.5
    ///   M18: SCROLL -3.0
    /// </summary>
    private const string scroll_chart =
        """
        #TITLE BMS Scroll Visual
        #ARTIST BMS Ruleset Test
        #BPM 120
        #LNTYPE 1

        #SCROLL01 1.5
        #SCROLL02 2.0
        #SCROLL03 2.5
        #SCROLL04 3.0
        #SCROLL05 1.0
        #SCROLL06 0.75
        #SCROLL07 0.5
        #SCROLL08 0.25
        #SCROLL09 0
        #SCROLL0A -0.25
        #SCROLL0B -0.5
        #SCROLL0C -0.75
        #SCROLL0D -1.0
        #SCROLL0E -1.5
        #SCROLL0F -2.0
        #SCROLL0G -2.5
        #SCROLL0H -3.0

        #00111:0100000000000000
        #00112:0001000000000000
        #00113:0000010000000000
        #00114:0000000100000000

        #002SC:01
        #00211:0100000000000000
        #00212:0001000000000000
        #00213:0000010000000000
        #00214:0000000100000000

        #003SC:02
        #00311:0100000000000000
        #00312:0001000000000000
        #00313:0000010000000000
        #00314:0000000100000000

        #004SC:03
        #00411:0100000000000000
        #00412:0001000000000000
        #00413:0000010000000000
        #00414:0000000100000000

        #005SC:04
        #00511:0100000000000000
        #00512:0001000000000000
        #00513:0000010000000000
        #00514:0000000100000000

        #006SC:05
        #00611:0100000000000000
        #00612:0001000000000000
        #00613:0000010000000000
        #00614:0000000100000000

        #007SC:06
        #00711:0100000000000000
        #00712:0001000000000000

        #008SC:07
        #00811:0100000000000000
        #00812:0001000000000000

        #009SC:08
        #00911:0100000000000000
        #00912:0001000000000000

        #010SC:09
        #01011:0100000000000000
        #01012:0001000000000000

        #011SC:0A
        #01111:0100000000000000
        #01112:0001000000000000

        #012SC:0B
        #01211:0100000000000000
        #01212:0001000000000000

        #013SC:0C
        #01311:0100000000000000
        #01312:0001000000000000

        #014SC:0D
        #01411:0100000000000000
        #01412:0001000000000000

        #015SC:0E
        #01511:0100000000000000
        #01512:0001000000000000

        #016SC:0F
        #01611:0100000000000000
        #01612:0001000000000000

        #017SC:0G
        #01711:0100000000000000
        #01712:0001000000000000

        #018SC:0H
        #01811:0100000000000000
        #01812:0001000000000000
        """;

    [Test]
    public void TestScrollVisual()
    {
        AddStep("load scroll player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        AddAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>);
        AddUntilStep("bms stage loaded", () => Playfield.Stage.IsLoaded);

        AddAssert("has 17 scroll events", () =>
        {
            var bm = (BmsBeatmap)Player.GameplayState.Beatmap;
            return bm.TimingMap!.ScrollEvents.Count == 17;
        });

        // BPM 120, 4/4: each measure = 2000ms.
        // #002SC at tick 384 (4000ms), #003SC at 576 (6000ms), etc.
        // SCROLL is global — all on-screen notes use the same CurrentScrollFactor.
        var tm = () => ((BmsBeatmap)Player.GameplayState.Beatmap).TimingMap!;

        AddAssert("M1: default 1.0", () => tm().GetScrollFactorAtTime(500) == 1.0);
        AddAssert("M2: 1.5", () => tm().GetScrollFactorAtTime(5000) == 1.5);
        AddAssert("M3: 2.0", () => tm().GetScrollFactorAtTime(7000) == 2.0);
        AddAssert("M4: 2.5", () => tm().GetScrollFactorAtTime(9000) == 2.5);
        AddAssert("M5: 3.0", () => tm().GetScrollFactorAtTime(11000) == 3.0);
        AddAssert("M6: 1.0", () => tm().GetScrollFactorAtTime(13000) == 1.0);
        AddAssert("M7: 0.75", () => tm().GetScrollFactorAtTime(15000) == 0.75);
        AddAssert("M8: 0.5", () => tm().GetScrollFactorAtTime(17000) == 0.5);
        AddAssert("M9: 0.25", () => tm().GetScrollFactorAtTime(19000) == 0.25);
        AddAssert("M10: 0 (freeze)", () => tm().GetScrollFactorAtTime(21000) == 0.0);
        AddAssert("M11: -0.25", () => tm().GetScrollFactorAtTime(23000) == -0.25);
        AddAssert("M12: -0.5", () => tm().GetScrollFactorAtTime(25000) == -0.5);
        AddAssert("M13: -0.75", () => tm().GetScrollFactorAtTime(27000) == -0.75);
        AddAssert("M14: -1.0", () => tm().GetScrollFactorAtTime(29000) == -1.0);
        AddAssert("M15: -1.5", () => tm().GetScrollFactorAtTime(31000) == -1.5);
        AddAssert("M16: -2.0", () => tm().GetScrollFactorAtTime(33000) == -2.0);
        AddAssert("M17: -2.5", () => tm().GetScrollFactorAtTime(35000) == -2.5);
        AddAssert("M18: -3.0", () => tm().GetScrollFactorAtTime(37000) == -3.0);
    }
}
