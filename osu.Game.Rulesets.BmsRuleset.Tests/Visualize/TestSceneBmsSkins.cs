using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsSkins : PlayerTestScene, IStorageResourceProvider
{
    private TestPlayFieldCreator.SkinKind skinKind;

    [Resolved]
    private GameHost host { get; set; } = null!;

    protected override bool HasCustomSteps => true;

    protected override double TimePerAction => 0;

    protected override Ruleset CreatePlayerRuleset() => new BmsRuleset();

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => new TestPlayFieldCreator.SkinnedTestPlayer(
            TestPlayFieldCreator.CreateSkinSource(skinKind, this),
            TestPlayFieldCreator.CreateReplayFrames);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = TestPlayFieldCreator.CreateBeatmap();
        TestPlayFieldCreator.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    private void createSkinScene(TestPlayFieldCreator.SkinKind kind)
    {
        this.AddSetupStep($"use {kind} skin", () => skinKind = kind);
        this.AddSetupStep("load player", LoadPlayer);
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms drawable ruleset", () => Player.DrawableRuleset, Is.TypeOf<BmsDrawableRuleset>());
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());
        this.AddSetupUntilStep("gameplay hud loaded", () => Player.HUDOverlay.IsLoaded);
        this.AddSetupUntilStep("hud skin components loaded", () => Player.HUDOverlay.ChildrenOfType<SkinnableContainer>().All(c => c.ComponentsLoaded));
        this.AddSetupUntilStep("bms stage loaded", () => ((BmsPlayfield)Player.DrawableRuleset.Playfield).Stage.IsLoaded);
        this.AddSetupAssert("bms health display present", () => Player.DrawableRuleset.Playfield.ChildrenOfType<BmsHealthDisplay>().Any());

        this.AddSetupUntilStep("all hit results produced", () => BmsRuleset.STATIC_VALID_HIT_RESULTS.All(result => Player.ScoreProcessor.Statistics.GetValueOrDefault(result) > 0));
        this.AddSetupAssert("judgement display active", () => ((BmsPlayfield)Player.DrawableRuleset.Playfield).Stage.JudgementArea.Count, Is.GreaterThan(0));
        this.AddSetupAssert("health changed", () => Math.Abs(Player.HealthProcessor.Health.Value - TestPlayFieldCreator.INIT_HEALTH), Is.GreaterThan(0.01));
        this.AddSetupAssert("score changed", () => Player.ScoreProcessor.TotalScore.Value, Is.GreaterThan(0));
        AddStep("skin scene complete", () => { });
    }

    public IRenderer Renderer => host.Renderer;

    public AudioManager AudioManager => Audio;

    public IResourceStore<byte[]> Files => null!;

    public new IResourceStore<byte[]> Resources => base.Resources;

    public IResourceStore<TextureUpload> CreateTextureLoaderStore(IResourceStore<byte[]> underlyingStore) => host.CreateTextureLoaderStore(underlyingStore);

    RealmAccess IStorageResourceProvider.RealmAccess => null!;

    [Test]
    public void TestArgonSkin()
    {
        createSkinScene(TestPlayFieldCreator.SkinKind.Argon);
    }

    [Test]
    public void TestClassicSkin()
    {
        createSkinScene(TestPlayFieldCreator.SkinKind.Classic);
    }
}
