using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsSkins : BmsPlayerTestScene
{
    private BmsTestSkins.SkinKind skinKind;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(BmsTestReplays.CreateReplayFrames, skinKind);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = BmsTestBeatmaps.CreateBeatmap();
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    private void createSkinScene(BmsTestSkins.SkinKind kind)
    {
        this.AddSetupStep($"use {kind} skin", () => skinKind = kind);
        this.AddSetupStep("load player", LoadPlayer);
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms drawable ruleset", () => Player.DrawableRuleset, Is.TypeOf<BmsDrawableRuleset>());
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());
        this.AddSetupUntilStep("gameplay hud loaded", () => Player.HUDOverlay.IsLoaded);
        this.AddSetupUntilStep("hud skin components loaded", () => Player.HUDOverlay.ChildrenOfType<SkinnableContainer>().All(c => c.ComponentsLoaded));
        this.AddSetupUntilStep("bms stage loaded", () => Playfield.Stage.IsLoaded);
        this.AddSetupAssert("bms health display present", () => Playfield.ChildrenOfType<BmsHealthDisplay>().Any());

        this.AddSetupUntilStep("all hit results produced",
            () => BmsRuleset.STATIC_VALID_HIT_RESULTS.All(result => Player.ScoreProcessor.Statistics.GetValueOrDefault(result) > 0));
        this.AddSetupAssert("judgement display active", () => Playfield.Stage.JudgementArea.Count, Is.GreaterThan(0));
        this.AddSetupAssert("health changed", () => Math.Abs(Player.HealthProcessor.Health.Value - BmsTestBeatmaps.INIT_HEALTH), Is.GreaterThan(0.01));
        this.AddSetupAssert("score changed", () => Player.ScoreProcessor.TotalScore.Value, Is.GreaterThan(0));
        AddStep("skin scene complete", () => { });
    }

    [Test]
    public void TestArgonSkin() => createSkinScene(BmsTestSkins.SkinKind.Argon);

    [Test]
    public void TestClassicSkin() => createSkinScene(BmsTestSkins.SkinKind.Classic);
}
