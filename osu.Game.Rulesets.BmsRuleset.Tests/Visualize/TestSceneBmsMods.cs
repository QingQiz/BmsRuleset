using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsMods : PlayerTestScene, IStorageResourceProvider
{
    [Resolved]
    private GameHost host { get; set; } = null!;

    protected override bool HasCustomSteps => true;

    protected override double TimePerAction => 0;

    protected override Ruleset CreatePlayerRuleset() => new BmsRuleset();

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => new TestPlayFieldCreator.SkinnedTestPlayer(
            TestPlayFieldCreator.CreateSkinSource(TestPlayFieldCreator.SkinKind.Argon, this),
            TestPlayFieldCreator.CreateWatchOnlyFrames);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = TestPlayFieldCreator.CreateBeatmap();
        TestPlayFieldCreator.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    public IRenderer Renderer => host.Renderer;

    public AudioManager AudioManager => Audio;

    public IResourceStore<byte[]> Files => null!;

    public new IResourceStore<byte[]> Resources => base.Resources;

    public IResourceStore<TextureUpload> CreateTextureLoaderStore(IResourceStore<byte[]> underlyingStore) => host.CreateTextureLoaderStore(underlyingStore);

    RealmAccess IStorageResourceProvider.RealmAccess => null!;

    private BmsPlayfield getPlayfield() => (BmsPlayfield)Player.DrawableRuleset.Playfield;

    [Test]
    public void TestAutoScratch()
    {
        this.AddSetupStep("load player with AS mod", () => LoadPlayer([new BmsModAutoScratch()]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());

        AddAssert("auto scratch enabled", () => getPlayfield().IsAutoScratch);
        AddAssert("scratch not hidden", () => !getPlayfield().HideScratch);
        AddAssert("judgement area visible", () => getPlayfield().Stage.JudgementArea.Count, () => Is.GreaterThan(0));
    }

    [Test]
    public void TestAutoScratchHideScratch()
    {
        var mod = new BmsModAutoScratch { HideScratch = { Value = true } };

        this.AddSetupStep("load player with AS+HS mod", () => LoadPlayer([mod]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());

        AddAssert("auto scratch enabled", () => getPlayfield().IsAutoScratch);
        AddAssert("scratch hidden", () => getPlayfield().HideScratch);
    }
}
