#nullable enable
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsHellChargeNoteHeadPoor : BmsPlayerTestScene
{
    private const double start_time = 3000;
    private const double duration = 900;
    private const long tick = 192;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(null);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Rank = 2,
            Total = 160,
            LockedLongNoteMode = BmsLongNoteMode.HellChargeNote,
            HitObjects =
            {
                new BmsLongNote
                {
                    StartTime = start_time,
                    Duration = duration,
                    Column = 1,
                    TickInfo = new BmsTickInfo { Tick = tick, EndTick = tick + 96 },
                },
            },
        };

        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset, endPadding: 3000, bpm: 120);
        return beatmap;
    }

    [Test]
    public void TestHeadPoorKeepsBodyDrainingBeforeTail()
    {
        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        AddUntilStep("bms stage loaded", () => Playfield.Stage.IsLoaded);
        AddStep("seek before hcn", () =>
        {
            Player.GameplayClockContainer.Seek(start_time - 100);
            Player.HealthProcessor.Health.Value = 0.5;
        });
        AddUntilStep("past head poor before tail", () => Player.GameplayClockContainer.CurrentTime >= start_time + 650);
        AddStep("assert released body drains", () =>
        {
            var longNote = Playfield.GetAliveObjectAtTick(tick);

            Assert.That(longNote is { Alpha: > 0 }, Is.True, "HCN should keep the body active after head POOR until the tail passes");
            Assert.That(Player.HealthProcessor.Health.Value, Is.LessThan(0.5), "HCN released body should drain health after head POOR");
        });
    }
}
