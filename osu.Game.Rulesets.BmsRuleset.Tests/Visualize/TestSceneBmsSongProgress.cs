#nullable enable
using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsSongProgress : BmsPlayerTestScene
{
    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(null);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = BmsTestBeatmaps.CreateBeatmap();
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [Test]
    public void TestAppearanceAndProgress()
    {
        BmsSongProgress progress() => Player.HUDOverlay.ChildrenOfType<BmsSongProgress>().Single();
        double firstHitTime() => Player.GameplayState.Beatmap.HitObjects.Min(hitObject => hitObject.StartTime);
        double lastHitTime() => Player.GameplayState.Beatmap.HitObjects.Max(hitObject => hitObject.GetEndTime());

        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddUntilStep("song progress loaded", () => Player.HUDOverlay.ChildrenOfType<BmsSongProgress>().SingleOrDefault()?.IsLoaded == true);
        AddStep("stop clock", () => Player.GameplayClockContainer.Stop());
        AddAssert("indicator remains left of playfield", () =>
            progress().ScreenSpaceDrawQuad.TopRight.X < Playfield.SkinnableComponentScreenSpaceDrawQuad.TopLeft.X);

        AddStep("seek to top", () => Player.GameplayClockContainer.Seek(firstHitTime()));
        AddStep("seek to middle", () => Player.GameplayClockContainer.Seek((firstHitTime() + lastHitTime()) / 2));
        AddStep("use cyan indicator", () => progress().IndicatorColour.Value = new Colour4(40, 220, 255, 255));
        AddStep("seek to bottom", () => Player.GameplayClockContainer.Seek(lastHitTime()));
        AddStep("restore default red", () => progress().IndicatorColour.SetDefault());
    }

    [Test]
    public void TestHitErrorMeterLayout()
    {
        BmsHitErrorMeter meter() => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single();
        UprightAspectMaintainingContainer label(string name) => meter().ChildrenOfType<UprightAspectMaintainingContainer>().Single(child => child.Name == name);
        Container judgements() => meter().ChildrenOfType<Container>().Single(child => child.Name == "judgements");

        float originalLabelDistance = 0;
        float originalJudgementHeight = 0;

        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddUntilStep("hit error meter loaded", () => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().SingleOrDefault()?.IsLoaded == true);
        AddAssert("meter has one window bar", () => meter().ChildrenOfType<Container>().Count(child => child.Name == "judgement windows") == 1);
        AddAssert("window bars use BMS judgement colours", () =>
        {
            HitResult[] results = [HitResult.Perfect, HitResult.Great, HitResult.Good, HitResult.Ok];
            return results.All(result => meter().ChildrenOfType<Box>().Single(child => child.Name == $"{result} window").Colour == BmsHitResultColours.ForHitResult(result))
                   && meter().ChildrenOfType<Box>().Single(child => child.Name == "empty poor window").Colour == BmsHitResultColours.ForHitResult(HitResult.Miss);
        });
        AddAssert("centre marker is white", () =>
        {
            var markers = meter().ChildrenOfType<Drawable>()
                                 .Where(child => child.Name is "middle marker behind" or "middle marker in front")
                                 .ToArray();
            return markers.Length == 2 && markers.All(child => child.Colour == Colour4.White);
        });
        AddAssert("EPOOR window is an added fast segment", () =>
            meter().ChildrenOfType<Box>().Single(child => child.Name == "empty poor window").ScreenSpaceDrawQuad.Centre.X
            < meter().ScreenSpaceDrawQuad.Centre.X);
        AddAssert("meter is horizontal", () => meter().ScreenSpaceDrawQuad.AABBFloat.Width > meter().ScreenSpaceDrawQuad.AABBFloat.Height);
        AddAssert("fast is left of slow", () => label("fast label").ScreenSpaceDrawQuad.Centre.X < label("slow label").ScreenSpaceDrawQuad.Centre.X);
        AddStep("capture original dimensions", () =>
        {
            originalLabelDistance = label("slow label").ScreenSpaceDrawQuad.Centre.X - label("fast label").ScreenSpaceDrawQuad.Centre.X;
            originalJudgementHeight = judgements().ScreenSpaceDrawQuad.AABBFloat.Height;
        });
        AddStep("stretch horizontally", () => meter().Width = 320);
        AddAssert("horizontal stretch lengthens timing axis", () =>
            label("slow label").ScreenSpaceDrawQuad.Centre.X - label("fast label").ScreenSpaceDrawQuad.Centre.X > originalLabelDistance);
        AddStep("stretch vertically", () => meter().Height = 52);
        AddAssert("vertical stretch widens judgement lines", () => judgements().ScreenSpaceDrawQuad.AABBFloat.Height > originalJudgementHeight);
    }

    [Test]
    public void TestPoorLineUsesBadWindowEnd()
    {
        BmsHitErrorMeter meter() => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single();
        Box badWindow() => meter().ChildrenOfType<Box>().Single(child => child.Name == $"{HitResult.Ok} window");
        BmsHitErrorMeter.JudgementLine poorLine() => meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Single();

        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddUntilStep("hit error meter loaded", () => meter().IsLoaded);
        AddStep("register POOR", () =>
        {
            var hitObject = Player.GameplayState.Beatmap.HitObjects.OfType<BmsNote>().First();
            Player.GameplayState.ScoreProcessor.ApplyResult(new JudgementResult(hitObject, hitObject.CreateJudgement())
            {
                Type = HitResult.Meh,
            });
        });
        AddUntilStep("POOR line appears", () => meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Count() == 1);
        AddAssert("POOR line uses BMS colour", () => poorLine().Colour == BmsHitResultColours.ForHitResult(HitResult.Meh));
        AddAssert("POOR line is at BAD window end", () =>
            Math.Abs(poorLine().ScreenSpaceDrawQuad.Centre.X - badWindow().ScreenSpaceDrawQuad.AABBFloat.Right) < 0.5f);
    }

    [Test]
    public void TestEmptyPoorLineUsesNextNoteOffset()
    {
        const double input_time = BmsTestBeatmaps.FIRST_NOTE_TIME - 400;

        Box emptyPoorWindow() => Player.HUDOverlay.ChildrenOfType<Box>().Single(child => child.Name == "empty poor window");

        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("hide EPOOR", () => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single().ShowEmptyPoor.Value = false);
        AddUntilStep("EPOOR window hidden", () => emptyPoorWindow().Alpha == 0);
        AddStep("register hidden EPOOR", () =>
            ((BmsScoreProcessor)Player.GameplayState.ScoreProcessor).RegisterEmptyPoor(input_time, BmsTestBeatmaps.FIRST_NOTE_TIME, 0));
        AddWaitStep("wait for scheduled update", 1);
        AddAssert("hidden EPOOR adds no line", () => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Count() == 0);
        AddStep("show EPOOR", () => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single().ShowEmptyPoor.Value = true);
        AddUntilStep("EPOOR window shown", () => emptyPoorWindow().Alpha == 1);
        AddStep("register EPOOR timing", () =>
            ((BmsScoreProcessor)Player.GameplayState.ScoreProcessor).RegisterEmptyPoor(input_time, BmsTestBeatmaps.FIRST_NOTE_TIME, 0));
        AddUntilStep("error line appears", () => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Count() == 1);
        AddAssert("EPOOR line is on fast side", () =>
            Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Single().ScreenSpaceDrawQuad.Centre.X
            < Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single().ScreenSpaceDrawQuad.Centre.X);
    }

    [Test]
    public void TestJudgementFadeDuration()
    {
        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("set short fade", () => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single().JudgementFadeDuration.Value = 0.1f);
        AddStep("register EPOOR timing", () =>
            ((BmsScoreProcessor)Player.GameplayState.ScoreProcessor).RegisterEmptyPoor(
                BmsTestBeatmaps.FIRST_NOTE_TIME - 400, BmsTestBeatmaps.FIRST_NOTE_TIME, 0));
        AddUntilStep("error line appears", () => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Count() == 1);
        AddUntilStep("error line expires", () => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Count() == 0);
    }

    [Test]
    public void TestHitErrorMeterCapsConcurrentLines()
    {
        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("register judgement barrage", () =>
        {
            var processor = (BmsScoreProcessor)Player.GameplayState.ScoreProcessor;

            for (var i = 0; i < 100; i++)
                processor.RegisterEmptyPoor(BmsTestBeatmaps.FIRST_NOTE_TIME - i, BmsTestBeatmaps.FIRST_NOTE_TIME, 0);
        });
        AddUntilStep("scheduled judgements processed", () =>
            Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Count() == 50);
        AddAssert("judgement lines remain capped", () =>
            Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Count() <= 50);
    }
}
