#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsHcnTailVisuals : BmsPlayerTestScene
{
    private int column;
    private bool mineAtTail;
    private bool earlyRelease;
    private float? expectedPinnedHeadY;

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(createReplay);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Rank = 2,
            Total = 12,
            LockedLongNoteMode = BmsLongNoteMode.HellChargeNote,
            HitObjects = [new BmsLongNote { StartTime = 3000, Duration = 1000, Column = column }],
        };
        if (mineAtTail)
            beatmap.HitObjects.Add(new BmsLandmine { StartTime = 4000, Column = column, LandmineDamagePercent = 10 });
        beatmap.HitObjects.Add(new BmsNote { StartTime = 10000, Column = 2 });
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    private IList<ReplayFrame> createReplay(BmsBeatmap beatmap)
    {
        if (!earlyRelease)
            return [.. new BmsAutoGenerator(beatmap).Generate().Frames];

        return [new BmsReplayFrame(0), new BmsReplayFrame(3000, column == 0 ? BmsAction.Scratch : BmsAction.Key1),
            new BmsReplayFrame(3910, column == 0 ? [BmsAction.ScratchReverse] : []),
            new BmsReplayFrame(3911), new BmsReplayFrame(11000)];
    }

    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(0, true)]
    [TestCase(1, true)]
    public void AutoplayFinishesHcnAtTheTail(int noteColumn, bool adjacentMine)
    {
        load(noteColumn, adjacentMine, false);
        checkHeldBody(3990);
        checkCompleted(4000);
        checkCompleted(4001);
        checkCompleted(4040);
        checkCompleted(4500);
        checkCompleted(4000);
        seek(2990);
        checkHeldBody(3990);
        checkCompleted(4000);
        checkCompleted(4001);
    }

    [TestCase(0)]
    [TestCase(1)]
    public void SuccessfulEarlyTailKeepsTheRemainingBodyAtTheJudgementLine(int noteColumn)
    {
        load(noteColumn, false, true);
        checkHeldBody(3900);
        checkHeldBody(3915);
        checkCompleted(4000);
        checkCompleted(4001);
        checkCompleted(4500);
        checkHeldBody(3915);
        checkCompleted(4000);
    }

    [TestCase(-100)]
    [TestCase(100)]
    public void SuccessfulEarlyTailKeepsVisualOffsetAcrossRewind(double offset)
    {
        load(1, false, true);
        AddStep("set visual offset", () => Playfield.VisualOffset.Value = offset);
        seek(3900);
        AddStep("capture held head position", () =>
        {
            var note = Playfield.GetAliveObjectAtTime(3000)!;
            expectedPinnedHeadY = BmsPlayfieldAssertions.BottomOf(part(note, "NoteContainer"));
            Assert.That(Math.Sign(expectedPinnedHeadY.Value - Playfield.JudgementLineY()), Is.EqualTo(Math.Sign(offset)));
        });
        checkHeldBody(3915);
        checkCompleted(4500);
        checkHeldBody(3915);
        checkCompleted(4000);
    }

    private void load(int noteColumn, bool adjacentMine, bool early)
    {
        AddStep("load HCN tail scenario", () =>
        {
            column = noteColumn;
            mineAtTail = adjacentMine;
            earlyRelease = early;
            expectedPinnedHeadY = null;
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Playfield.Stage.IsLoaded);
        seek(2990);
    }

    private void seek(double time)
    {
        AddStep("seek " + time, () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(time);
        });
        AddUntilStep("simulation reached " + time, () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < .001);
    }

    private void checkHeldBody(double time)
    {
        seek(time);
        AddStep("held body stays at judgement line " + time, () =>
        {
            var note = Playfield.GetAliveObjectAtTime(3000) as DrawableBmsHitObject;
            Assert.That(note, Is.Not.Null);
            Assert.That(note!.Alpha, Is.GreaterThan(0));
            var head = part(note, "NoteContainer");
            var body = part(note, "longNoteBody");
            Assert.That(BmsPlayfieldAssertions.BottomOf(head), Is.EqualTo(expectedPinnedHeadY ?? Playfield.JudgementLineY()).Within(1),
                "A successfully held HCN must finish shrinking toward its pinned head.");
            Assert.That(body.Alpha, Is.EqualTo(1));
        });
    }

    private void checkCompleted(double time)
    {
        seek(time);
        AddStep("completed HCN is gone " + time, () =>
        {
            var events = ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents;
            Assert.That(events.Select(e => e.Result), Is.EqualTo(new[] { HitResult.Perfect, HitResult.Perfect }));
            Assert.That(Player.ScoreProcessor.Combo.Value, Is.EqualTo(2));
            Assert.That(Player.HealthProcessor.Health.Value, Is.EqualTo(.36).Within(1e-8),
                "TOTAL12 / three endpoints: two +4% endpoints and four +2% body ticks.");
            var note = Playfield.GetAliveObjectAtTime(3000);
            Assert.That(note == null || note.Alpha == 0, Is.True,
                "A completed HCN must disappear at its tail, including the first 50ms after autoplay releases.");
        });
    }

    private static Drawable part(DrawableBmsHitObject note, string name)
        => (Drawable)note.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(note)!;
}
