#nullable enable
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.UI.Objects;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsPauseRewindLongNote : BmsPlayerTestScene
{
    private const double long_note_time = 5200;
    private const double long_note_duration = 500;
    private const double pause_time = 10000;

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => new(allowPause: true, showResults: false);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            HitObjects =
            {
                new BmsLongNote { StartTime = long_note_time, Duration = long_note_duration, Column = 2 },
                new BmsNote { StartTime = 30000, Column = 1 },
            },
        };

        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset, endPadding: 3000, bpm: 120);
        return beatmap;
    }

    [Test]
    public void TestCompletedLongNoteRemainsHiddenDuringResumeRewind()
    {
        BmsLongNote hitObject = null!;
        DrawableBmsHitObject? longNote = null;

        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Playfield.Stage.IsLoaded);
        AddStep("capture long note", () => hitObject = ((BmsBeatmap)Player.GameplayState.Beatmap).HitObjects.OfType<BmsLongNote>().Single());
        AddStep("seek to long note head", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(hitObject.StartTime);
            Player.GameplayClockContainer.Start();
        });
        AddStep("stop at long note head", () => Player.GameplayClockContainer.Stop());
        AddUntilStep("long note alive", () =>
        {
            longNote = Playfield.AllColumnAliveObjects()
                .OfType<DrawableBmsHitObject>()
                .SingleOrDefault(drawable => drawable.HitObject is BmsLongNote);
            return longNote != null;
        });
        AddStep("judge long note head", () => Playfield.Stage.Columns[2].HandlePress(hitObject.StartTime));
        AddStep("seek to long note tail", () => Player.GameplayClockContainer.Seek(hitObject.EndTime));
        AddStep("judge long note tail", () => Playfield.Stage.Columns[2].HandleRelease(hitObject.EndTime));
        AddAssert("long note judged", () => longNote!.Judged);

        AddStep("seek to pause time", () =>
        {
            Player.GameplayClockContainer.Seek(pause_time);
            Player.GameplayClockContainer.Start();
        });
        AddStep("pause", () => Player.Pause());
        AddStep("resume", () => Player.Resume());
        AddUntilStep("resume rewind active", () => Playfield.IsResumeRewinding);
        AddUntilStep("completed long note restored to lifetime", () => Playfield.AllColumnAliveObjects().Any(drawable => drawable.HitObject is BmsLongNote));
        AddWaitStep("allow drawable transforms", 2);
        AddAssert("rewind remains active", () => Playfield.IsResumeRewinding);
        AddAssert("judgement survives rewind", () => longNote!.Judged);
        AddAssert("completed long note hidden", () => longNote!.Alpha, () => Is.Zero);
    }
}
