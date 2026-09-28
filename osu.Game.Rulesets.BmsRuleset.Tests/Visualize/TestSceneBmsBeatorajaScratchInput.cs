#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Replays;
using osu.Game.Scoring;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsBeatorajaScratchInput : BmsPlayerTestScene
{
    private BmsLongNoteMode mode;
    private bool shortNext;
    private bool manual;
    private bool reversedHead;
    private BmsLayoutVariant layout = BmsLayoutVariant.Bme7K;
    private int column;
    private double nextGap = 1;
    private IList<ReplayFrame>? customFrames;
    private bool autoScratch;

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(createReplay);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = layout, TotalColumns = BmsLayout.GetTotalColumns(layout), Rank = 2, Total = 12,
            LockedLongNoteMode = mode,
            HitObjects = [new BmsLongNote { StartTime = 3000, Duration = 1000, Column = column }],
        };
        if (!manual)
            beatmap.HitObjects.Add(shortNext
                ? new BmsLongNote { StartTime = 4000 + nextGap, Duration = 1, Column = column }
                : new BmsLandmine { StartTime = 4005, Column = column, LandmineDamagePercent = 10 });
        beatmap.HitObjects.Add(new BmsNote { StartTime = 10000, Column = 1 });
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    private IList<ReplayFrame> createReplay(BmsBeatmap beatmap)
    {
        if (customFrames != null) return customFrames;
        if (autoScratch) return [new BmsReplayFrame(0), new BmsReplayFrame(11000)];
        if (!manual) return archiveFrames([..new BmsAutoGenerator(beatmap).Generate().Frames]);
        var head = reversedHead ? reverseAction : forwardAction;
        var tail = reversedHead ? forwardAction : reverseAction;
        return [new BmsReplayFrame(0), new BmsReplayFrame(3000, head),
            new BmsReplayFrame(3900), new BmsReplayFrame(3950, head),
            new BmsReplayFrame(3990), new BmsReplayFrame(4000, tail),
            new BmsReplayFrame(4010), new BmsReplayFrame(11000)];
    }

    [TestCase(BmsLongNoteMode.ChargeNote, false)]
    [TestCase(BmsLongNoteMode.HellChargeNote, false)]
    [TestCase(BmsLongNoteMode.ChargeNote, true)]
    [TestCase(BmsLongNoteMode.HellChargeNote, true)]
    public void AutoplayTailAvoidsMineAndCompletesAdjacentShortScratch(BmsLongNoteMode noteMode, bool followingShort)
    {
        load(noteMode, false, followingShort, false);
        if (!followingShort)
            checkpoint(4005, () =>
            {
                var events = ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents;
                TestContext.WriteLine(string.Join("; ", events.Select(e => $"{e.Source.Kind}:{e.Result}@{e.TimingObservations.Last().ActualTime}")));
                Assert.Multiple(() =>
                {
                    Assert.That(events.Any(e => e.Source.Kind == BmsJudgementSourceKind.Landmine), Is.False,
                        "JudgeManager 223-284: autoplay endpoints do not hold physical mine input");
                    Assert.That(Player.HealthProcessor.Health.Value, Is.EqualTo(mode == BmsLongNoteMode.ChargeNote ? 0.28 : 0.36).Within(1e-8),
                        "NORMAL starts at 20%; TOTAL12 gives +4% per endpoint; HCN also has four +2% body ticks");
                });
            });
        checkpoint(4400, () =>
        {
            var events = ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents;
            TestContext.WriteLine(string.Join("; ", events.Select(e => $"{e.Source.Kind}:{e.Result}@{e.TimingObservations.Last().ActualTime}")));
            Assert.That(events.Any(e => e.Source.Kind == BmsJudgementSourceKind.Landmine), Is.False, "beatoraja autoplay does not supply pressed input for mines");
            Assert.That(events.Select(e => e.Result), Is.EqualTo(Enumerable.Repeat(HitResult.Perfect, followingShort ? 4 : 2)),
                "beatoraja autoplay directly judges every charge endpoint PGREAT, including dense scratches");
            Assert.That(BmsExScore.CreateProgression(events).Last(), Is.EqualTo(followingShort ? 8 : 4));
            Assert.That(Player.ScoreProcessor.Combo.Value, Is.EqualTo(followingShort ? 4 : 2));
        });
    }

    [TestCase(BmsLongNoteMode.ChargeNote, false)]
    [TestCase(BmsLongNoteMode.HellChargeNote, false)]
    [TestCase(BmsLongNoteMode.ChargeNote, true)]
    [TestCase(BmsLongNoteMode.HellChargeNote, true)]
    public void ReverseTailWorksBothHeadDirectionsAndAfterRewind(BmsLongNoteMode noteMode, bool reverseHead)
    {
        load(noteMode, true, false, reverseHead);
        checkpoint(3995, () => Assert.That(((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.Select(e => e.Result),
            Is.EqualTo(new[] { HitResult.Perfect }), "same direction repress and valid-window release cannot judge tail"));
        checkpoint(4400, assertPerfectTail);
        checkpoint(3995, () => Assert.That(((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.Select(e => e.Result),
            Is.EqualTo(new[] { HitResult.Perfect }), "tail must revert"));
        checkpoint(4400, assertPerfectTail);
    }

    private void assertPerfectTail()
    {
        var events = ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents;
        Assert.That(events.Select(e => e.Result), Is.EqualTo(new[] { HitResult.Perfect, HitResult.Perfect }));
        Assert.That(events.Last().TimingObservations.Single().ActualTime, Is.EqualTo(4000));
    }

    private void load(BmsLongNoteMode noteMode, bool userInput, bool followingShort, bool reverseHead)
    {
        AddStep("configure defaults", () => { layout = BmsLayoutVariant.Bme7K; column = 0; nextGap = 1; customFrames = null; autoScratch = false; });
        loadConfigured(noteMode, userInput, followingShort, reverseHead);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
    }

    private bool secondSide => column > 0;
    private BmsAction forwardAction => secondSide ? BmsAction.P2Scratch : BmsAction.Scratch;
    private BmsAction reverseAction => secondSide ? BmsAction.P2ScratchReverse : BmsAction.ScratchReverse;

    private static IList<ReplayFrame> archiveFrames(IList<ReplayFrame> frames)
    {
        var score = new Score();
        score.Replay.Frames = [..frames];
        return BmsTestReplayArchive.RoundTrip(score).Replay.Frames;
    }

    private void loadConfigured(BmsLongNoteMode noteMode, bool userInput, bool followingShort, bool reverseHead)
    {
        AddStep("load scenario", () =>
        {
            mode = noteMode; manual = userInput; shortNext = followingShort; reversedHead = reverseHead;
            if (autoScratch) LoadPlayer([new BmsModAutoScratch()]); else LoadPlayer();
        });
        AddUntilStep("scenario loaded", () => Player.IsLoaded && Player.Alpha == 1);
    }

    [TestCase(BmsLongNoteMode.ChargeNote, 1, false)]
    [TestCase(BmsLongNoteMode.HellChargeNote, 3, false)]
    [TestCase(BmsLongNoteMode.ChargeNote, 8, false)]
    [TestCase(BmsLongNoteMode.HellChargeNote, 1, true)]
    [TestCase(BmsLongNoteMode.ChargeNote, 3, true)]
    [TestCase(BmsLongNoteMode.LongNote, 1, false)]
    public void DenseAutoplayEndpointsSurviveArchive(BmsLongNoteMode noteMode, double gap, bool p2)
    {
        AddStep("configure dense notes", () =>
        {
            layout = p2 ? BmsLayoutVariant.Bme7KDouble : BmsLayoutVariant.Bme7K;
            column = p2 ? 15 : 0; nextGap = gap; customFrames = null; autoScratch = false;
        });
        loadConfigured(noteMode, false, true, false);
        checkpoint(4400, () => assertPerfectEndpoints(noteMode == BmsLongNoteMode.LongNote ? 2 : 4));
    }

    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void AutoplayTailAvoidsMineDuringContinuousPlayback(BmsLongNoteMode noteMode)
    {
        load(noteMode, false, false, false);
        checkpoint(3990, () => Assert.That(((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents, Has.Count.EqualTo(1)));
        AddStep("resume continuous playback", () => Player.GameplayClockContainer.Start());
        AddUntilStep("past mine", () => Player.DrawableRuleset.FrameStableClock.CurrentTime >= 4050);
        AddStep("mine was not triggered", () =>
        {
            Player.GameplayClockContainer.Stop();
            assertPerfectEndpoints(2);
            if (noteMode == BmsLongNoteMode.ChargeNote)
                Assert.That(Player.HealthProcessor.Health.Value, Is.EqualTo(0.28).Within(1e-8));
        });
    }

    [TestCase(BmsLayoutVariant.Bms5K, 0, false)]
    [TestCase(BmsLayoutVariant.Bms5K2P, 0, true)]
    [TestCase(BmsLayoutVariant.Bme7K2P, 0, false)]
    [TestCase(BmsLayoutVariant.Bms5KDouble, 11, true)]
    [TestCase(BmsLayoutVariant.Bme7KDouble, 15, false)]
    public void ReverseTailMappingAndHeadRewind(BmsLayoutVariant variant, int scratchColumn, bool reverseHead)
    {
        AddStep("configure scratch mapping", () =>
        {
            layout = variant; column = scratchColumn; customFrames = null; autoScratch = false;
        });
        loadConfigured(BmsLongNoteMode.ChargeNote, true, false, reverseHead);
        checkpoint(3995, () => assertPerfectEndpoints(1));
        checkpoint(4400, assertPerfectTail);
        checkpoint(2500, () => Assert.That(((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents, Is.Empty));
        checkpoint(4400, assertPerfectTail);
    }

    [TestCase(BmsLongNoteMode.LongNote, false)]
    [TestCase(BmsLongNoteMode.LongNote, true)]
    [TestCase(BmsLongNoteMode.ChargeNote, false)]
    [TestCase(BmsLongNoteMode.HellChargeNote, true)]
    public void OriginalDirectionReleaseOutsideTailWindow(BmsLongNoteMode noteMode, bool reverseHead)
    {
        AddStep("configure early release", () =>
        {
            layout = BmsLayoutVariant.Bme7K; column = 0; autoScratch = false;
            var head = reverseHead ? BmsAction.ScratchReverse : BmsAction.Scratch;
            customFrames = archiveFrames([new BmsReplayFrame(0), new BmsReplayFrame(3000, head), new BmsReplayFrame(3500), new BmsReplayFrame(11000)]);
        });
        loadConfigured(noteMode, true, false, reverseHead);
        checkpoint(3501, () =>
        {
            var events = ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents;
            Assert.That(events.Select(e => e.Result), Is.EqualTo(noteMode == BmsLongNoteMode.LongNote
                ? new[] { HitResult.Ok } : new[] { HitResult.Perfect, HitResult.Meh }));
            Assert.That(events.Last().TimingObservations.Last().TimeOffset, Is.EqualTo(-500));
            Assert.That(Player.ScoreProcessor.Combo.Value, Is.Zero);
            if (noteMode != BmsLongNoteMode.HellChargeNote)
                Assert.That(Player.HealthProcessor.Health.Value, Is.EqualTo(noteMode == BmsLongNoteMode.LongNote ? 0.17 : 0.18).Within(1e-8));
        });
    }

    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void ReversePressOutsideTailWindowStillJudgesPoor(BmsLongNoteMode noteMode)
    {
        AddStep("configure early reverse", () =>
        {
            layout = BmsLayoutVariant.Bme7K; column = 0; autoScratch = false;
            customFrames = [new BmsReplayFrame(0), new BmsReplayFrame(3000, BmsAction.Scratch),
                new BmsReplayFrame(3500, BmsAction.ScratchReverse), new BmsReplayFrame(3501), new BmsReplayFrame(11000)];
        });
        loadConfigured(noteMode, true, false, false);
        checkpoint(3500, () =>
        {
            var events = ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents;
            Assert.That(events.Select(e => e.Result), Is.EqualTo(new[] { HitResult.Perfect, HitResult.Meh }));
            Assert.That(events.Last().TimingObservations.Single().TimeOffset, Is.EqualTo(-500));
        });
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void OverlappingDirectionsKeepColumnPressed(BmsLongNoteMode noteMode)
    {
        AddStep("configure overlapping directions", () =>
        {
            layout = BmsLayoutVariant.Bme7K; column = 0; autoScratch = false;
            customFrames = [new BmsReplayFrame(0), new BmsReplayFrame(2000, BmsAction.ScratchReverse),
                new BmsReplayFrame(3000, BmsAction.ScratchReverse, BmsAction.Scratch),
                new BmsReplayFrame(3500, BmsAction.Scratch), new BmsReplayFrame(4000),
                new BmsReplayFrame(4001, BmsAction.ScratchReverse), new BmsReplayFrame(4011), new BmsReplayFrame(11000)];
        });
        loadConfigured(noteMode, true, false, false);
        checkpoint(3500, () =>
        {
            Assert.That(((osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components.BmsColumn)Playfield.Stage.Columns[0]).IsPressed, Is.True);
            var events = ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents;
            Assert.That(events.Select(e => e.Result), Is.EqualTo(noteMode == BmsLongNoteMode.LongNote ? Array.Empty<HitResult>() : new[] { HitResult.Perfect }));
        });
        checkpoint(4050, () => assertPerfectEndpoints(noteMode == BmsLongNoteMode.LongNote ? 1 : 2));
    }

    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void AutoScratchDenseNotesRewind(BmsLongNoteMode noteMode)
    {
        AddStep("configure auto scratch", () =>
        {
            layout = BmsLayoutVariant.Bme7KDouble; column = 15; nextGap = 1; customFrames = null; autoScratch = true;
        });
        loadConfigured(noteMode, false, true, false);
        checkpoint(4400, () => assertPerfectEndpoints(4));
        checkpoint(2500, () => Assert.That(((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents, Is.Empty));
        checkpoint(4400, () => assertPerfectEndpoints(4));
    }

    [Test]
    public void LegacyReleaseOnlyReplayIsReadableButDoesNotPreserveChargeTailScore()
    {
        AddStep("load legacy action sequence", () =>
        {
            layout = BmsLayoutVariant.Bme7K; column = 0; autoScratch = false;
            customFrames = archiveFrames([new BmsReplayFrame(0), new BmsReplayFrame(3000, BmsAction.Scratch),
                new BmsReplayFrame(4000), new BmsReplayFrame(11000)]);
            Assert.That((int)BmsAction.Scratch, Is.EqualTo(0));
        });
        loadConfigured(BmsLongNoteMode.ChargeNote, true, false, false);
        checkpoint(4400, () =>
        {
            var score = (BmsScoreProcessor)Player.ScoreProcessor;
            Assert.That(score.JudgementEvents.Select(e => e.Result), Is.EqualTo(new[] { HitResult.Perfect, HitResult.Meh }),
                "Compatibility evidence only: a pre-direction replay once completed by key-up now loses its tail");
            Assert.That(BmsExScore.CreateProgression(score.JudgementEvents), Is.EqualTo(new[] { 0, 2, 2 }));
        });
    }

    private void assertPerfectEndpoints(int count)
    {
        var score = (BmsScoreProcessor)Player.ScoreProcessor;
        Assert.Multiple(() =>
        {
            Assert.That(score.JudgementEvents.Select(e => e.Result), Is.EqualTo(Enumerable.Repeat(HitResult.Perfect, count)));
            Assert.That(BmsExScore.CreateProgression(score.JudgementEvents).Last(), Is.EqualTo(count * 2));
            Assert.That(score.Combo.Value, Is.EqualTo(count));
        });
    }

    private void checkpoint(double time, Action assertion)
    {
        AddStep("seek " + time, () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(time); });
        AddUntilStep("simulation reached " + time, () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < 0.001);
        AddStep("assert " + time, assertion);
    }
}
