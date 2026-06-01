using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsScrollSpeedControls : PlayerTestScene, IStorageResourceProvider
{
    private float normalSpacing;

    [Resolved]
    private GameHost host { get; set; } = null!;

    protected override bool HasCustomSteps => true;

    protected override double TimePerAction => 0;

    protected override Ruleset CreatePlayerRuleset() => new BmsRuleset();

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => new TestPlayFieldCreator.SkinnedTestPlayer(
            TestPlayFieldCreator.CreateSkinSource(TestPlayFieldCreator.SkinKind.Argon, this),
            TestPlayFieldCreator.CreateAutoPlayFrames);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = TestPlayFieldCreator.CreateBeatmap();

        beatmap.BeatmapInfo.Ruleset = ruleset;
        beatmap.BeatmapInfo.Difficulty.CircleSize = beatmap.TotalColumns;
        beatmap.BeatmapInfo.BPM = 120;
        beatmap.BeatmapInfo.Length = 10000;

        return beatmap;
    }

    private const string scroll_chart =
        """
        #TITLE Scroll Speed Controls
        #ARTIST BMS Ruleset Test
        #BPM 120
        #00111:0100010000000000
        #00112:0000000001000100
        #00213:0100010001000100
        """;

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

    public IRenderer Renderer => host.Renderer;

    public AudioManager AudioManager => Audio;

    public IResourceStore<byte[]> Files => null!;

    public new IResourceStore<byte[]> Resources => base.Resources;

    public IResourceStore<TextureUpload> CreateTextureLoaderStore(IResourceStore<byte[]> underlyingStore) => host.CreateTextureLoaderStore(underlyingStore);

    RealmAccess IStorageResourceProvider.RealmAccess => null!;

    [Test]
    public void TestKeyboardScrollSpeedControlsAffectSpacing()
    {
        this.AddSetupStep("load Argon player", LoadPlayer);
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);

        AddStep("seek before notes", () => Player.GameplayClockContainer.Seek(0));
        AddUntilStep("two notes alive", () => getNoteAtTick(192) != null && getNoteAtTick(240) != null);
        AddUntilStep("spacing measurable", () => spacingBetweenTicks(192, 240), () => Is.GreaterThan(1));
        AddStep("capture spacing", () => normalSpacing = spacingBetweenTicks(192, 240));

        AddStep("press up", () => getPlayfield().AdjustScrollSpeed(1));
        AddUntilStep("scroll speed increased", () => getPlayfield().ScrollSpeed, () => Is.EqualTo(9).Within(0.001));
        AddUntilStep("spacing visibly increased", () => spacingBetweenTicks(192, 240), () => Is.GreaterThan(normalSpacing * 1.1f));

        AddStep("press down", () => getPlayfield().AdjustScrollSpeed(-1));
        AddUntilStep("scroll speed restored", () => getPlayfield().ScrollSpeed, () => Is.EqualTo(8).Within(0.001));
    }
}
