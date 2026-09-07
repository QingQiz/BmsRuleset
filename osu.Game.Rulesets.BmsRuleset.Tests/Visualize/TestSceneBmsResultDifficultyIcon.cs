using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.UI.Icons;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Scoring;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsResultDifficultyIcon : OsuManualInputManagerTestScene
{
    [TestCase(false)]
    [TestCase(true)]
    public void TestBmsIconWithoutGlobalPatch(bool showTooltip)
    {
        BmsResultDifficultyIcon icon = null!;
        DifficultyIcon nativeIcon = null!;

        AddStep("load BMS and native icons", () =>
        {
            var beatmap = new BeatmapInfo
            {
                Ruleset = new BmsRuleset().RulesetInfo,
                DifficultyName = "BMS difficulty",
                StarRating = 5,
                BPM = 150,
                Length = 180_000,
            };
            Assert.That(beatmap.Ruleset.OnlineID, Is.LessThan(0));
            var score = new ScoreInfo
            {
                BeatmapInfo = beatmap,
                Ruleset = beatmap.Ruleset,
                Mods = [new BmsModDoubleTime()],
            };
            Child = new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(20),
                Children =
                [
                    icon = new BmsResultDifficultyIcon(score) { ShowTooltip = showTooltip },
                    nativeIcon = new DifficultyIcon(beatmap) { TooltipType = DifficultyIconTooltipType.None },
                ],
            };
        });
        AddUntilStep("icons loaded", () => icon.IsLoaded && nativeIcon.IsLoaded);
        AddAssert("BMS icon rendered at native size", () => icon.DrawSize == nativeIcon.DrawSize
            && icon.ChildrenOfType<BmsRulesetIcon>().Single().IsPresent);
        AddAssert("native icon remains unpatched", () => !nativeIcon.ChildrenOfType<BmsRulesetIcon>().Any()
            && nativeIcon.ChildrenOfType<SpriteIcon>().Single().Icon.Equals(FontAwesome.Regular.QuestionCircle));
        AddUntilStep("background uses star colour", () => background().Colour == new OsuColour().ForStarDifficulty(5));
        AddStep("hover BMS icon", () => InputManager.MoveMouseTo(icon));

        if (showTooltip)
        {
            AddUntilStep("difficulty tooltip appears", () => this.ChildrenOfType<StarRatingDisplay>().Any());
            AddUntilStep("tooltip shows difficulty and mod-adjusted timing", () =>
            {
                var texts = this.ChildrenOfType<OsuSpriteText>().Select(text => text.Text.ToString()).ToArray();
                return texts.Contains("BMS difficulty") && texts.Contains("Length: 02:00") && texts.Contains("BPM: 225");
            });
        }
        else
        {
            AddWaitStep("wait for hover delay", 10);
            AddAssert("course icon has no difficulty tooltip", () => !this.ChildrenOfType<StarRatingDisplay>().Any());
        }

        AddStep("update displayed difficulty", () => icon.Current.Value = new StarDifficulty(8, 0));
        AddUntilStep("background follows difficulty", () => background().Colour == new OsuColour().ForStarDifficulty(8));
        if (showTooltip)
            AddUntilStep("tooltip follows difficulty", () => this.ChildrenOfType<StarRatingDisplay>().Single().Current.Value.Stars, () => Is.EqualTo(8));

        Box background() => icon.ChildrenOfType<CircularContainer>().Single(container => container.Parent == icon).ChildrenOfType<Box>().Single();
    }
}
