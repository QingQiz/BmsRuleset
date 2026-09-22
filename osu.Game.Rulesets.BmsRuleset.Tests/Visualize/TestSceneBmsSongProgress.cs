#nullable enable
using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Pooling;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;
using osuTK;

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
        AddAssert("hidden EPOOR adds no line", () => !Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Any());
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
    public void TestHitErrorMeterExpandsAndReusesPool()
    {
        BmsHitErrorMeter meter() => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single();
        DrawablePool<BmsHitErrorMeter.JudgementLine> pool() => meter().ChildrenOfType<DrawablePool<BmsHitErrorMeter.JudgementLine>>().Single();

        void registerBarrage()
        {
            var processor = (BmsScoreProcessor)Player.GameplayState.ScoreProcessor;

            for (var i = 0; i < 100; i++)
                processor.RegisterEmptyPoor(BmsTestBeatmaps.FIRST_NOTE_TIME - i, BmsTestBeatmaps.FIRST_NOTE_TIME, 0);
        }

        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("stop gameplay clock", () => Player.GameplayClockContainer.Stop());
        AddAssert("pool starts with fifty reusable lines", () => pool().CurrentPoolSize == 50);
        AddStep("register judgement barrage", registerBarrage);
        AddUntilStep("all hundred lines are displayed", () => meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Count() == 100);
        AddAssert("pool grows past its initial size", () => pool().CurrentPoolSize == 100 && pool().CountInUse == 100);
        AddStep("thicken existing lines", () => meter().JudgementLineThickness.Value = 5.5f);
        AddAssert("existing and expanded lines update thickness", () =>
            meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().All(line => line.Width == 5.5f));
        AddStep("clear judgement lines", () => meter().Clear());
        AddUntilStep("lines return to pool", () => pool().CountInUse == 0 && pool().CountAvailable == 100);
        AddStep("use short fade", () => meter().JudgementFadeDuration.Value = 0.5f);
        AddStep("change thickness while lines are pooled", () => meter().JudgementLineThickness.Value = 1.5f);
        AddStep("register another barrage", registerBarrage);
        AddUntilStep("all hundred lines are reused", () => pool().CountInUse == 100);
        AddAssert("reusing lines does not grow pool", () => pool().CurrentPoolSize == 100);
        AddAssert("reused lines use current thickness", () =>
            meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().All(line => line.Width == 1.5f));
        AddUntilStep("faded lines return to pool", () => pool().CountInUse == 0 && pool().CountAvailable == 100);
    }

    [Test]
    public void TestHitErrorMeterLayout()
    {
        BmsHitErrorMeter meter() => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single();
        Box badWindow() => meter().ChildrenOfType<Box>().Single(child => child.Name == $"{HitResult.Ok} window");
        Container judgements() => meter().ChildrenOfType<Container>().Single(child => child.Name == "judgements");

        float originalWindowWidth = 0;
        float originalJudgementHeight = 0;

        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddUntilStep("hit error meter loaded", () => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().SingleOrDefault()?.IsLoaded == true);
        AddAssert("legacy dimensions follow the BMS domain", () =>
        {
            var domain = BmsHitErrorMeter.CreateDomain(Playfield.Beatmap.LayoutVariant, Playfield.Beatmap.HitObjects[0].EffectiveJudgementRate);
            return Math.Abs(meter().Width - (domain.SlowOffset - domain.FastOffset) / 2 * LegacySkin.STABLE_MAGIC_SCALE_FACTOR) < 0.001
                   && Math.Abs(meter().Height - 12 * LegacySkin.STABLE_MAGIC_SCALE_FACTOR) < 0.001
                   && meter().Scale == Vector2.One;
        });
        AddAssert("legacy background is translucent black", () =>
        {
            var background = meter().ChildrenOfType<Box>().Single(child => child.Name == "background");
            return background.Colour == Colour4.Black && background.Alpha == 0.6f;
        });
        AddStep("make background transparent", () => meter().BackgroundOpacity.Value = 0);
        AddAssert("only background becomes transparent", () =>
            meter().ChildrenOfType<Box>().Single(child => child.Name == "background").Alpha == 0
            && badWindow().IsPresent && meter().ChildrenOfType<Triangle>().Single().IsPresent);
        AddStep("make background opaque", () => meter().BackgroundOpacity.Value = 1);
        AddAssert("background is fully opaque", () => meter().ChildrenOfType<Box>().Single(child => child.Name == "background").Alpha == 1);
        AddStep("restore background opacity", () => meter().BackgroundOpacity.SetDefault());
        AddAssert("meter has one window bar", () => meter().ChildrenOfType<Container>().Count(child => child.Name == "judgement windows") == 1);
        AddAssert("window bars use BMS judgement colours", () =>
        {
            HitResult[] results = [HitResult.Perfect, HitResult.Great, HitResult.Good, HitResult.Ok];
            return results.All(result => meter().ChildrenOfType<Box>().Single(child => child.Name == $"{result} window").Colour == BmsHitResultColours.ForHitResult(result))
                   && meter().ChildrenOfType<Box>().Single(child => child.Name == "empty poor window").Colour == BmsHitResultColours.ForHitResult(HitResult.Miss);
        });
        AddAssert("centre marker is white", () =>
        {
            var marker = meter().ChildrenOfType<Box>().Single(child => child.Name == "centre marker");
            return marker.Colour == Colour4.White
                   && Math.Abs(marker.ScreenSpaceDrawQuad.Centre.X - meter().ScreenSpaceDrawQuad.Centre.X) < 0.001
                   && marker.Width == 1.5f * LegacySkin.STABLE_MAGIC_SCALE_FACTOR;
        });
        AddAssert("legacy triangle starts at zero", () =>
        {
            var arrow = meter().ChildrenOfType<Triangle>().Single();
            return arrow.X == 0.5f && arrow.Scale.Y == -1;
        });
        AddAssert("BAD window retains asymmetric BMS edges", () =>
        {
            var domain = BmsHitErrorMeter.CreateDomain(Playfield.Beatmap.LayoutVariant, Playfield.Beatmap.HitObjects[0].EffectiveJudgementRate);
            var windows = BmsJudgementProfileProvider.GetTable(Playfield.Beatmap.LayoutVariant, 1, Playfield.Beatmap.HitObjects[0].EffectiveJudgementRate, tail: false);
            return Math.Abs(badWindow().X - domain.RelativePosition(-windows.FastWindowFor(HitResult.Ok))) < 0.00001
                   && Math.Abs(badWindow().X + badWindow().Width - domain.RelativePosition(windows.SlowWindowFor(HitResult.Ok))) < 0.00001;
        });
        AddAssert("EPOOR window is an added fast segment", () =>
            meter().ChildrenOfType<Box>().Single(child => child.Name == "empty poor window").ScreenSpaceDrawQuad.Centre.X
            < meter().ScreenSpaceDrawQuad.Centre.X);
        AddAssert("meter is horizontal", () => meter().ScreenSpaceDrawQuad.AABBFloat.Width > meter().ScreenSpaceDrawQuad.AABBFloat.Height);
        AddStep("capture original dimensions", () =>
        {
            originalWindowWidth = badWindow().ScreenSpaceDrawQuad.AABBFloat.Width;
            originalJudgementHeight = judgements().ScreenSpaceDrawQuad.AABBFloat.Height;
        });
        AddStep("stretch horizontally", () => meter().Width *= 1.5f);
        AddAssert("horizontal stretch lengthens timing axis", () =>
            badWindow().ScreenSpaceDrawQuad.AABBFloat.Width > originalWindowWidth);
        AddStep("stretch vertically", () => meter().Height = 52);
        AddAssert("vertical stretch widens judgement lines", () => judgements().ScreenSpaceDrawQuad.AABBFloat.Height > originalJudgementHeight);
    }

    [Test]
    public void TestJudgementFadeDuration()
    {
        BmsHitErrorMeter meter() => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single();

        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("default fade lasts ten seconds", () => meter().JudgementFadeDuration.Value == 10);
        AddStep("set short fade", () => meter().JudgementFadeDuration.Value = 0.5f);
        AddStep("register EPOOR timing", () =>
            ((BmsScoreProcessor)Player.GameplayState.ScoreProcessor).RegisterEmptyPoor(
                BmsTestBeatmaps.FIRST_NOTE_TIME - 400, BmsTestBeatmaps.FIRST_NOTE_TIME, 0));
        AddUntilStep("error line appears", () => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Count() == 1);
        AddAssert("legacy line fades without shrinking", () =>
        {
            var line = meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Single();
            return line.Width == 3 && line.Height == 1 && line.RelativeSizeAxes == Axes.Y
                   && line.Alpha is > 0 and <= 0.4f && line.ChildrenOfType<Box>().Count() == 1;
        });
        AddUntilStep("error line expires", () => !Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Any());
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
}
