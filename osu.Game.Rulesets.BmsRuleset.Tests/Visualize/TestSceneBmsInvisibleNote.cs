#nullable enable
using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Tests.Visual;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsInvisibleNote : BmsPlayerTestScene
{
    private BmsRulesetConfigManager config => (BmsRulesetConfigManager)RulesetConfigs.GetConfigFor(new BmsRuleset())!;

    private DrawableBmsInvisibleNote drawable = null!;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(60000)]);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            InvisibleNotes = [new BmsInvisibleNote { StartTime = 5000, Column = 1 }],
            HitObjects = [new BmsNote { StartTime = 30000, Column = 2 }],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [Test]
    public void TestVisibilityInputExpiryAndRewind()
    {
        AddStep("load with default setting", () =>
        {
            config.SetValue(BmsRulesetSetting.ShowInvisibleNotes, false);
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Playfield.Stage.IsLoaded);
        seek(4800);
        AddAssert("invisible notes retained", () => Playfield.Beatmap.InvisibleNotes.Count == 1);
        AddAssert("invisible lifetime covers current time", () => Playfield.Stage.Columns[1].HitObjectContainer.Entries
            .Any(e => e.HitObject is BmsInvisibleNote && e.LifetimeStart <= 4800 && e.LifetimeEnd > 4800));
        AddUntilStep("invisible note alive", () =>
        {
            drawable = Playfield.AllColumnAliveObjects().OfType<DrawableBmsInvisibleNote>().SingleOrDefault()!;
            return drawable != null;
        });
        AddAssert("hidden by default", () => !drawable.IsPresent);
        AddStep("enable display", () => config.SetValue(BmsRulesetSetting.ShowInvisibleNotes, true));
        AddUntilStep("outline visible", () => drawable.IsPresent);
        AddAssert("yellow hollow rectangle", () => drawable.ChildrenOfType<Container>()
            .Any(c => c.Masking && c.BorderThickness == 2 && c.BorderColour == Color4.Yellow && c.DrawWidth > 0 && c.Height == 8));
        AddAssert("press gives no judgement", () => Playfield.Stage.Columns[1].HandlePress(4800).Equals(PressOutcome.Empty));
        AddStep("release column", () => Playfield.Stage.Columns[1].HandleRelease(4800));
        AddAssert("no score events", () => Player.Results.Count == 0 && !drawable.Judged);
        AddStep("disable display live", () => config.SetValue(BmsRulesetSetting.ShowInvisibleNotes, false));
        AddUntilStep("hidden again", () => !drawable.IsPresent);
        AddStep("enable again", () => config.SetValue(BmsRulesetSetting.ShowInvisibleNotes, true));
        seek(5300);
        AddUntilStep("expired without judgement", () => !Playfield.AllColumnAliveObjects().OfType<DrawableBmsInvisibleNote>().Any());
        AddAssert("passing does not score", () => Player.Results.Count == 0);
        seek(4800);
        AddUntilStep("visible again after rewind", () => Playfield.AllColumnAliveObjects().OfType<DrawableBmsInvisibleNote>().Any(d => d.IsPresent));
        AddStep("restore default", () => config.SetValue(BmsRulesetSetting.ShowInvisibleNotes, false));
    }

    private void seek(double time)
    {
        AddStep($"seek {time}", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(time);
        });
        AddUntilStep("clock caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < 0.001);
    }
}
