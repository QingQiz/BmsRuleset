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
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
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
        Container colourBar() => meter().ChildrenOfType<Container>().Single(child => child.Name == "judgement windows");
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
        AddStep("thicken colour bar", () => meter().ColourBarHeight.Value = 5.5f);
        AddAssert("colour bar uses configured height", () => Math.Abs(colourBar().DrawHeight - 5.5f * LegacySkin.STABLE_MAGIC_SCALE_FACTOR) < 0.001);
        AddStep("hide colour bar", () => meter().ColourBarHeight.Value = 0);
        AddAssert("zero height hides colour bar and preserves timing overlay", () =>
            colourBar().DrawHeight == 0 && !colourBar().IsPresent
            && meter().ChildrenOfType<Box>().Single(child => child.Name == "centre marker").IsPresent
            && judgements().IsPresent && meter().ChildrenOfType<Triangle>().Single().IsPresent);
        AddStep("restore colour bar height", () => meter().ColourBarHeight.SetDefault());
        AddAssert("colour bar becomes visible at default height", () =>
            colourBar().IsPresent && Math.Abs(colourBar().DrawHeight - 3 * LegacySkin.STABLE_MAGIC_SCALE_FACTOR) < 0.001);
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
    public void TestColourBarMatchesJudgements([Values(false, true)] bool noBad, [Values(0, 1, 2)] int constraint)
    {
        BmsHitErrorMeter meter() => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single();

        AddStep("load window mods", () =>
        {
            Mod[] mods = constraint switch
            {
                1 => [new BmsModNoGood()],
                2 => [new BmsModNoGreat()],
                _ => [],
            };
            LoadPlayer(noBad ? [..mods, new BmsModNoBad()] : mods);
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddUntilStep("meter loaded", () => meter().IsLoaded);
        AddStep("check visible colours against playable windows", () =>
        {
            var windows = BmsJudgementProfileProvider.GetTable(Playfield.Beatmap.LayoutVariant, 1,
                Playfield.Beatmap.HitObjects[0].EffectiveJudgementRate, tail: false);
            var bars = meter().ChildrenOfType<Container>().Single(child => child.Name == "judgement windows").Children.OfType<Box>().ToArray();

            // Quarter-millisecond samples avoid shared inclusive boundaries while covering both sides
            // and the unused space beyond E-POOR's finite slow boundary.
            for (var offset = -499.75; offset < 500; offset++)
            {
                var result = windows.ResultForOffset(offset);
                if (result == HitResult.None && windows.IsEmptyPoorOffset(offset))
                    result = HitResult.Miss;

                var position = (float)((offset + 500) / 1000);
                var visible = bars.LastOrDefault(bar => bar.Width > 0 && position > bar.X && position < bar.X + bar.Width);
                if (result == HitResult.None)
                    Assert.That(visible, Is.Null, $"no judgement region at {offset} ms");
                else
                    Assert.That(visible != null && visible.Colour == BmsHitResultColours.ForHitResult(result), Is.True, $"{result} at {offset} ms");
            }

            Assert.That(meter().Width, Is.EqualTo(500 * LegacySkin.STABLE_MAGIC_SCALE_FACTOR).Within(0.001));
        });
        AddStep("hide E-POOR regions", () => meter().ShowEmptyPoor.Value = false);
        AddAssert("E-POOR background is hidden", () =>
            !meter().ChildrenOfType<Box>().Single(child => child.Name == "empty poor window").IsPresent);
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
    public void TestPoorLinesFollowVisibleWindowEnds([Values(false, true)] bool noBad, [Values(0, 1, 2)] int constraint)
    {
        BmsHitErrorMeter meter() => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single();
        Box window(HitResult result) => meter().ChildrenOfType<Box>().Single(child =>
            child.Name == (result == HitResult.Miss ? "empty poor window" : $"{result} window"));

        AddStep("load player", () =>
        {
            Mod[] mods = constraint switch
            {
                1 => [new BmsModNoGood()],
                2 => [new BmsModNoGreat()],
                _ => [],
            };
            LoadPlayer(noBad ? [..mods, new BmsModNoBad()] : mods);
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddUntilStep("hit error meter loaded", () => meter().IsLoaded);
        AddStep("stop gameplay clock", () => Player.GameplayClockContainer.Stop());
        AddStep("register Fast and Slow POOR", registerPoors);
        AddUntilStep("both POOR lines appear", () => meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Count() == 2);
        AddStep("POOR lines match visible outer edges", () => assertEnds(showEmptyPoor: true));
        AddStep("hide E-POOR", () => meter().ShowEmptyPoor.Value = false);
        AddStep("existing POOR lines follow remaining hit windows", () => assertEnds(showEmptyPoor: false));
        AddStep("clear lines", () => meter().Clear());
        AddUntilStep("lines returned to pool", () => !meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Any());
        AddStep("register POOR with E-POOR hidden", registerPoors);
        AddUntilStep("both POOR lines appear again", () => meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Count() == 2);
        AddStep("new POOR lines use remaining hit windows", () => assertEnds(showEmptyPoor: false));
        AddStep("show E-POOR", () => meter().ShowEmptyPoor.Value = true);
        AddStep("existing POOR lines return to outer edges", () => assertEnds(showEmptyPoor: true));

        void registerPoors()
        {
            var notes = Player.GameplayState.Beatmap.HitObjects.OfType<BmsNote>().Take(2).ToArray();
            for (var i = 0; i < notes.Length; i++)
            {
                var result = new JudgementResult(notes[i], notes[i].CreateJudgement()) { Type = HitResult.Meh };
                typeof(JudgementResult).GetProperty(nameof(JudgementResult.TimeOffset))!.SetValue(result, i == 0 ? -281d : 281d);
                Player.GameplayState.ScoreProcessor.ApplyResult(result);
            }
        }

        void assertEnds(bool showEmptyPoor)
        {
            var hitResult = !noBad ? HitResult.Ok : constraint switch
            {
                1 => HitResult.Great,
                2 => HitResult.Perfect,
                _ => HitResult.Good,
            };
            var left = window(showEmptyPoor ? HitResult.Miss : hitResult).ScreenSpaceDrawQuad.AABBFloat.Left;
            var right = window(showEmptyPoor && noBad ? HitResult.Miss : hitResult).ScreenSpaceDrawQuad.AABBFloat.Right;
            var lines = meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().OrderBy(line => line.X).ToArray();

            Assert.That(lines, Has.Length.EqualTo(2));
            Assert.That(lines.All(line => line.Colour == BmsHitResultColours.ForHitResult(HitResult.Meh)), Is.True);
            Assert.That(lines[0].ScreenSpaceDrawQuad.Centre.X, Is.EqualTo(left).Within(0.5f));
            Assert.That(lines[1].ScreenSpaceDrawQuad.Centre.X, Is.EqualTo(right).Within(0.5f));
        }
    }
}
