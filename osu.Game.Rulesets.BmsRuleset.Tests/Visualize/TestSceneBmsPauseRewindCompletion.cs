#nullable enable
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Mods.LongNoteMode;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Result;
using osu.Game.Rulesets.Mods;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsPauseRewindCompletion : BmsPlayerTestScene
{
    private const double head_time = 9000;
    private const double pause_time = 10000;
    private double tailTime;

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => new(allowPause: true, showResults: true);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            HitObjects =
            {
                new BmsLongNote { StartTime = head_time, Duration = tailTime - head_time, Column = 2 },
                new BmsNote { StartTime = 14000, Column = 1 },
            },
        };

        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset, endPadding: 3000, bpm: 120);
        return beatmap;
    }

    [Test]
    public void TestCompletionAfterRewindingAcrossLongNoteHead(
        [Values(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode mode,
        [Values] bool hitHead,
        [Values(9500, 12000)] double endTime)
    {
        DrawableBmsHitObject? initialDrawable = null;
        var judgementsAtPause = 0;
        Mod modeMod = mode switch
        {
            BmsLongNoteMode.ChargeNote => new BmsModChargeNote(),
            BmsLongNoteMode.HellChargeNote => new BmsModHellChargeNote(),
            _ => new BmsModLongNote(),
        };

        AddStep("load player", () =>
        {
            tailTime = endTime;
            LoadPlayer([modeMod]);
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Playfield.Stage.IsLoaded);
        AddStep("seek to head", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(head_time);
            Player.GameplayClockContainer.Start();
        });
        // A no-replay player cannot catch up after stopping; wait for simulation before freezing it.
        AddUntilStep("simulation reaches head", () => Player.DrawableRuleset.FrameStableClock.CurrentTime >= head_time);
        AddStep("stop at head", () => Player.GameplayClockContainer.Stop());
        AddUntilStep("long note alive", () =>
            (initialDrawable = Playfield.AllColumnAliveObjects().OfType<DrawableBmsHitObject>().SingleOrDefault(d => d.HitObject is BmsLongNote)) != null);
        AddStep("play head", () =>
        {
            if (hitHead)
                Playfield.Stage.Columns[2].HandlePress(head_time);
        });
        AddStep("seek to pause time", () =>
        {
            Player.GameplayClockContainer.Seek(pause_time);
            Player.GameplayClockContainer.Start();
        });
        AddUntilStep("simulation reaches pause", () => Player.DrawableRuleset.FrameStableClock.CurrentTime >= pause_time);
        AddStep("pause", () => Player.Pause());
        AddStep("capture judgement count", () => judgementsAtPause = Player.ScoreProcessor.JudgedHits);
        AddStep("resume", () => Player.Resume());
        AddUntilStep("long note leaves lifetime during rewind", () => initialDrawable!.HitObject == null);
        AddStep("pause again during lead-in", () => Player.Pause());
        AddStep("resume again", () => Player.Resume());
        AddUntilStep("long note returns during lead-in", () => Playfield.AllColumnAliveObjects().Any(d => d.HitObject is BmsLongNote));
        AddAssert("judgements retained during lead-in", () => Player.ScoreProcessor.JudgedHits, () => Is.EqualTo(judgementsAtPause));
        AddUntilStep("resume lead-in finished", () => !Playfield.IsResumeRewinding);
        AddUntilStep("play past final note", () => Player.DrawableRuleset.FrameStableClock.CurrentTime >= 14500);
        AddAssert("each scoring event counted once", () => ((BmsScoreProcessor)Player.ScoreProcessor).ScoringJudgementEventCount,
            () => Is.EqualTo(mode == BmsLongNoteMode.LongNote ? 2 : 3));
        AddUntilStep("score completed", () => Player.ScoreProcessor.HasCompleted.Value);
        AddUntilStep("results screen shown", () => Stack.CurrentScreen is BmsResultsScreen);
    }
}
