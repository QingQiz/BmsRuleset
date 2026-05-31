using System;
using System.IO;
using System.Linq;
using System.Text;
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
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Replays;
using osu.Game.Scoring;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public partial class TestSceneBmsArgonTiming : PlayerTestScene, IStorageResourceProvider
{
    private float normalSpeedSpacing;

    [Resolved]
    private GameHost host { get; set; } = null!;

    protected override bool HasCustomSteps => true;

    protected override double TimePerAction => 0;

    protected override Ruleset CreatePlayerRuleset() => new BmsRuleset();

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => new ArgonTimingPlayer(new SkinProvidingContainer(new ArgonSkin(this)));

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = createBeatmap();

        beatmap.BeatmapInfo.Ruleset = ruleset;
        beatmap.BeatmapInfo.Difficulty.CircleSize = beatmap.TotalColumns;
        beatmap.BeatmapInfo.Difficulty.OverallDifficulty = 6;
        beatmap.BeatmapInfo.Difficulty.DrainRate = 5;
        beatmap.BeatmapInfo.BPM = 130;
        beatmap.BeatmapInfo.Length = (int)(beatmap.HitObjects.Max(h => h.EndTime) + 3000);

        return beatmap;
    }

    private BmsPlayfield getPlayfield() => (BmsPlayfield)Player.DrawableRuleset.Playfield;

    private DrawableBmsHitObject getCrossSpeedLongNote() => getPlayfield().HitObjectContainer.AliveObjects
        .OfType<DrawableBmsHitObject>()
        .FirstOrDefault(d => d.HitObject is { IsLongNote: true, TickInfo.Tick: 384 });

    private DrawableBmsHitObject getAliveObjectAtTick(long tick) => getPlayfield().HitObjectContainer.AliveObjects
        .OfType<DrawableBmsHitObject>()
        .FirstOrDefault(d => d.HitObject.TickInfo.Tick == tick && !d.HitObject.IsLongNote);

    private float spacingBetweenTicks(long firstTick, long secondTick)
    {
        var first = getAliveObjectAtTick(firstTick);
        var second = getAliveObjectAtTick(secondTick);

        if (first == null || second == null)
            return 0;

        return Math.Abs(topOf(first) - topOf(second));
    }

    private float judgementLineY()
    {
        var playfield = getPlayfield();
        var line = playfield.Stage.DrawHeight - playfield.Stage.HitTargetPosition;
        return playfield.Stage.ToScreenSpace(new Vector2(0, line)).Y;
    }

    private float topOf(Drawable drawable) => drawable.ScreenSpaceDrawQuad.TopLeft.Y;

    private float bottomOf(Drawable drawable) => drawable.ScreenSpaceDrawQuad.BottomLeft.Y;

    private void seekToTick(long tick, double leadTime = 600)
    {
        var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
        var target = beatmap.HitObjects.First(h => h.TickInfo.Tick >= tick);

        Player.GameplayClockContainer.Seek(target.StartTime - leadTime);
    }

    private static BmsBeatmap createBeatmap()
    {
        const string chart = """
                             #TITLE Argon Timing Visual
                             #ARTIST BMS Ruleset Test
                             #BPM 130
                             #BPM01 260
                             #BPM02 65
                             #BPM03 0.25
                             #BPM04 1000000
                             #BPM05 0
                             #STOP01 384
                             #LNTYPE 1

                             #00111:01000000
                             #00112:00010000
                             #00113:00000100
                             #00114:00000001
                             #00118:00000100
                             #00119:00000001

                             #00208:01
                             #00251:01000001
                             #00213:00010000
                             #00214:00000100
                             #00215:00000001
                             #00218:01000100

                             #00309:01
                             #00311:01000000
                             #00312:00010000
                             #00313:00000100
                             #00314:00000001
                             #00319:00000100

                             #00408:02
                             #00458:01000001
                             #00411:01000000
                             #00412:00010000
                             #00413:00000100
                             #00414:00000001
                             #00415:01000100

                             #00511:01000100
                             #00512:00010001
                             #00513:00000100
                             #00514:00000001
                             #00518:01000100
                             #00519:00010001

                             #00608:04
                             #00611:01000100
                             #00612:00010001
                             #00618:01000100

                             #00708:05
                             #00713:01000100
                             #00714:00010001
                             #00719:01000100

                             #00808:03
                             #00811:01000000
                             #00812:00010000
                             #00813:00000100
                             #00814:00000001
                             """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(chart));
        using var reader = new LineBufferedReader(stream);
        var decoded = new BmsBeatmapDecoder().Decode(reader);

        return (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()).Convert();
    }

    public IRenderer Renderer => host.Renderer;

    public AudioManager AudioManager => Audio;

    public IResourceStore<byte[]> Files => null!;

    public new IResourceStore<byte[]> Resources => base.Resources;

    public IResourceStore<TextureUpload> CreateTextureLoaderStore(IResourceStore<byte[]> underlyingStore) => host.CreateTextureLoaderStore(underlyingStore);

    RealmAccess IStorageResourceProvider.RealmAccess => null!;

    private partial class ArgonTimingPlayer(ISkinSource skinSource) : TestPlayer(false, false)
    {
        [Cached(typeof(ISkinSource))]
        private readonly ISkinSource skinSource = skinSource;

        protected override void PrepareReplay()
        {
            var beatmap = (BmsBeatmap)GameplayState.Beatmap;
            var replay = new BmsAutoGenerator(beatmap).Generate();

            DrawableRuleset?.SetReplayScore(new Score
            {
                Replay = new Replay { Frames = replay.Frames.Cast<ReplayFrame>().ToList() },
            });
        }
    }

    [Test]
    public void TestArgonTimingScroll()
    {
        AddStep("load Argon player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        AddAssert("loaded bms drawable ruleset", () => Player.DrawableRuleset, () => Is.TypeOf<BmsDrawableRuleset>());
        AddAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, () => Is.TypeOf<BmsPlayfield>());
        AddUntilStep("bms stage loaded", () => ((BmsPlayfield)Player.DrawableRuleset.Playfield).Stage.IsLoaded);
        AddAssert("measure lines added", () => getPlayfield().Stage.MeasureLineArea.Count, () => Is.GreaterThan(0));
        AddStep("seek normal BPM", () => seekToTick(192));
        AddUntilStep("normal notes visible", () => Player.DrawableRuleset.Playfield.HitObjectContainer.AliveObjects.Count(), () => Is.GreaterThan(0));
        AddUntilStep("normal speed spacing measurable", () => spacingBetweenTicks(192, 240), () => Is.GreaterThan(1));
        AddStep("capture normal speed spacing", () => normalSpeedSpacing = spacingBetweenTicks(192, 240));
        AddStep("increase scroll speed", () => getPlayfield().AdjustScrollSpeed(0.5));
        AddAssert("scroll speed increased", () => getPlayfield().ScrollSpeed, () => Is.GreaterThan(8));
        AddUntilStep("spacing increased", () => spacingBetweenTicks(192, 240), () => Is.GreaterThan(normalSpeedSpacing));
        AddStep("decrease scroll speed", () => getPlayfield().AdjustScrollSpeed(-0.5));
        AddAssert("scroll speed restored", () => getPlayfield().ScrollSpeed, () => Is.EqualTo(8).Within(0.001));

        AddStep("seek fast BPM", () => seekToTick(384));
        AddUntilStep("fast notes visible", () => Player.DrawableRuleset.Playfield.HitObjectContainer.AliveObjects.Count(), () => Is.GreaterThan(0));
        AddUntilStep("cross-speed LN held", () => getCrossSpeedLongNote()?.Judged == false);
        AddAssert("cross-speed LN body above line", () => bottomOf(getCrossSpeedLongNote()!), () => Is.LessThanOrEqualTo(judgementLineY() + 1));

        AddStep("seek STOP freeze", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            var stop = beatmap.TimingMap!.StopEvents[0];
            var stopObject = beatmap.HitObjects.First(h => h.TickInfo.Tick == stop.Tick);

            Player.GameplayClockContainer.Seek(stopObject.StartTime + stop.Duration / 2);
        });
        AddUntilStep("stop notes visible", () => Player.DrawableRuleset.Playfield.HitObjectContainer.AliveObjects.Count(), () => Is.GreaterThan(0));

        AddStep("seek slow BPM", () => seekToTick(768));
        AddUntilStep("slow notes visible", () => Player.DrawableRuleset.Playfield.HitObjectContainer.AliveObjects.Count(), () => Is.GreaterThan(0));

        AddStep("seek post-LN note", () => seekToTick(1056));
        AddUntilStep("post-LN note alive", () => getAliveObjectAtTick(1056) != null);
        AddAssert("post-LN note approaches from above", () => topOf(getAliveObjectAtTick(1056)), () => Is.LessThanOrEqualTo(judgementLineY() + 1));

        AddStep("seek extreme BPM", () => seekToTick(1152, 20));
        AddUntilStep("extreme BPM notes visible", () => Player.DrawableRuleset.Playfield.HitObjectContainer.AliveObjects.Count(), () => Is.GreaterThan(0));

        AddStep("seek zero BPM fallback", () => seekToTick(1344, 20));
        AddUntilStep("zero BPM fallback notes visible", () => Player.DrawableRuleset.Playfield.HitObjectContainer.AliveObjects.Count(), () => Is.GreaterThan(0));

        AddStep("seek sub-1 BPM", () => seekToTick(1536, 200));
        AddUntilStep("sub-1 BPM notes visible", () => Player.DrawableRuleset.Playfield.HitObjectContainer.AliveObjects.Count(), () => Is.GreaterThan(0));
    }
}
