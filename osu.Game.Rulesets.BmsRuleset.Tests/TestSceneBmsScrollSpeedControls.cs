using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Game.Beatmaps;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Tests.Visual;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public partial class TestSceneBmsScrollSpeedControls : PlayerTestScene
{
    private float normalSpacing;

    protected override bool HasCustomSteps => true;

    protected override double TimePerAction => 0;

    protected override bool Autoplay => true;

    protected override Ruleset CreatePlayerRuleset() => new BmsRuleset();

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = createBeatmap();

        beatmap.BeatmapInfo.Ruleset = ruleset;
        beatmap.BeatmapInfo.Difficulty.CircleSize = beatmap.TotalColumns;
        beatmap.BeatmapInfo.BPM = 120;
        beatmap.BeatmapInfo.Length = 10000;

        return beatmap;
    }

    private BmsPlayfield getPlayfield() => (BmsPlayfield)Player.DrawableRuleset.Playfield;

    private DrawableBmsHitObject getNoteAtTick(long tick) => getPlayfield().HitObjectContainer.AliveObjects
        .OfType<DrawableBmsHitObject>()
        .FirstOrDefault(d => d.HitObject.TickInfo.Tick == tick);

    private float spacingBetweenTicks(long firstTick, long secondTick)
    {
        var first = getNoteAtTick(firstTick);
        var second = getNoteAtTick(secondTick);

        if (first == null || second == null)
            return 0;

        return Math.Abs(topOf(first) - topOf(second));
    }

    private static float topOf(Drawable drawable) => drawable.ScreenSpaceDrawQuad.TopLeft.Y;

    private static BmsBeatmap createBeatmap()
    {
        const string chart = """
                             #TITLE Scroll Speed Controls
                             #ARTIST BMS Ruleset Test
                             #BPM 120
                             #00111:0100010000000000
                             #00112:0000000001000100
                             #00213:0100010001000100
                             """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(chart));
        using var reader = new LineBufferedReader(stream);
        var decoded = new BmsBeatmapDecoder().Decode(reader);

        return (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()).Convert();
    }

    [Test]
    public void TestKeyboardScrollSpeedControlsAffectSpacing()
    {
        AddStep("load autoplay player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        AddAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, () => Is.TypeOf<BmsPlayfield>());
        AddAssert("autoplay playfield", () => getPlayfield().IsAutoplay, () => Is.True);

        AddStep("seek before notes", () => Player.GameplayClockContainer.Seek(1000));
        AddUntilStep("two notes alive", () => getNoteAtTick(192) != null && getNoteAtTick(240) != null);
        AddUntilStep("spacing measurable", () => spacingBetweenTicks(192, 240), () => Is.GreaterThan(1));
        AddStep("capture spacing", () => normalSpacing = spacingBetweenTicks(192, 240));

        AddStep("press up", () => InputManager.Key(Key.Up));
        AddUntilStep("scroll speed increased", () => getPlayfield().ScrollSpeed, () => Is.EqualTo(9).Within(0.001));
        AddUntilStep("spacing visibly increased", () => spacingBetweenTicks(192, 240), () => Is.GreaterThan(normalSpacing * 1.1f));

        AddStep("press down", () => InputManager.Key(Key.Down));
        AddUntilStep("scroll speed restored", () => getPlayfield().ScrollSpeed, () => Is.EqualTo(8).Within(0.001));
    }
}
