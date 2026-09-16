#nullable enable

using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsMineCulling : BmsPlayerTestScene
{
    private BmsLandmine mine = null!;
    private DrawableBmsHitObject drawable = null!;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(beatmap => new BmsAutoGenerator(beatmap).Generate().Frames.ToList());

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects =
            {
                new BmsLandmine { StartTime = 5000, Column = 1, LandmineDamagePercent = 5 },
                new BmsNote { StartTime = 30000, Column = 2 },
            },
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    private void loadMine()
    {
        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Playfield.Stage.IsLoaded);
        AddStep("seek before mine", () =>
        {
            mine = ((BmsBeatmap)Player.GameplayState.Beatmap).HitObjects.OfType<BmsLandmine>().Single();
            Player.GameplayClockContainer.Seek(4800);
        });
        AddStep("freeze clock", () => Player.GameplayClockContainer.Stop());
        AddUntilStep("mine alive", () =>
        {
            drawable = Playfield.AllColumnAliveObjects().OfType<DrawableBmsHitObject>().SingleOrDefault(d => d.HitObject == mine)!;
            return drawable != null;
        });
        AddUntilStep("mine visible", () => drawable.IsPresent);
    }

    [Test]
    public void TestMineCanLeaveAndReenterViewport()
    {
        loadMine();
        AddStep("move mine below viewport", () => mine.ScrollPositionAtStartTime = -1_000_000);
        AddUntilStep("mine culled", () => !drawable.IsPresent);
        AddAssert("mine remains unjudged", () => !drawable.Judged);
        AddStep("bring mine back", () => mine.ScrollPositionAtStartTime = Playfield.ScrollController.CurrentScrollPosition + 100);
        AddUntilStep("mine visible again", () => drawable.IsPresent);
        AddAssert("position refreshed on return", () => drawable.Y < 0 && drawable.Y > -drawable.Parent!.DrawHeight);
        AddStep("move mine above viewport", () => mine.ScrollPositionAtStartTime = 1_000_000);
        AddUntilStep("mine culled above too", () => !drawable.IsPresent);
    }

    [Test]
    public void TestCulledMineStillDetonates()
    {
        loadMine();
        AddStep("move mine outside viewport", () => mine.ScrollPositionAtStartTime = -1_000_000);
        AddUntilStep("mine culled", () => !drawable.IsPresent);
        AddStep("hold mine column", () => Playfield.Stage.Columns[1].HandlePress(4800));
        AddStep("advance to mine", () => Player.GameplayClockContainer.Seek(5000));
        AddUntilStep("mine judged despite culling", () => drawable.Judged);
        AddAssert("detonation reached score processor", () => Player.Results.Any(r => r.HitObject == mine));
    }

    [Test]
    public void TestPooledMineIsVisibleAfterRewind()
    {
        loadMine();
        AddStep("move mine outside viewport", () => mine.ScrollPositionAtStartTime = -1_000_000);
        AddUntilStep("mine culled", () => !drawable.IsPresent);
        AddStep("seek past lifetime", () => Player.GameplayClockContainer.Seek(5300));
        AddUntilStep("mine returned to pool", () => !Playfield.AllColumnAliveObjects().Any(d => d.HitObject == mine));
        AddStep("restore position and rewind", () =>
        {
            mine.ScrollPositionAtStartTime = 5000;
            Player.GameplayClockContainer.Seek(4800);
        });
        AddWaitStep("allow rewind", 3);
        AddUntilStep("mine visible after reapply", () => Playfield.AllColumnAliveObjects().Any(d => d.HitObject == mine && d.IsPresent));
    }

    [Test]
    public void TestResidentMineResetsOnRewind()
    {
        loadMine();
        AddStep("retain mine during cleanup", () => drawable.LifetimeEnd = 6000);
        AddStep("pass mine without pressing", () => Player.GameplayClockContainer.Seek(5050));
        AddUntilStep("mine hidden after passing", () => !drawable.IsPresent);
        AddAssert("mine still resident", () => Playfield.AllColumnAliveObjects().Contains(drawable));
        AddStep("rewind before mine", () => Player.GameplayClockContainer.Seek(4800));
        AddUntilStep("resident mine visible again", () => drawable.IsPresent && drawable.Alpha == 1);
        AddAssert("mine still unjudged", () => !drawable.Judged);
    }
}
