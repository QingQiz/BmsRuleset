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
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsBeatorajaLongNotes : BmsPlayerTestScene
{
    private BmsLayoutVariant layout;
    private BmsLongNoteMode mode;
    private double headOffset;
    private double startTime = 3000;
    private double duration = 2000;
    private double releaseTime;
    private double? repressTime;

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(createReplay);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = layout,
            TotalColumns = layout == BmsLayoutVariant.Pms9K ? 9 : 8,
            Rank = 2,
            Total = 12,
            LockedLongNoteMode = mode,
            HitObjects =
            [
                new BmsLongNote { StartTime = startTime, Duration = duration, Column = 1 },
                new BmsNote { StartTime = 10000, Column = 1 },
            ],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    private IList<ReplayFrame> createReplay(BmsBeatmap beatmap)
    {
        var action = layout == BmsLayoutVariant.Pms9K ? BmsAction.PmsKey2 : BmsAction.Key1;
        var frames = new List<ReplayFrame> { new BmsReplayFrame(0) };
        if (!double.IsNaN(headOffset))
        {
            frames.Add(new BmsReplayFrame(startTime + headOffset, action));
            frames.Add(new BmsReplayFrame(releaseTime));
            if (repressTime is { } repress)
            {
                frames.Add(new BmsReplayFrame(repress, action));
                frames.Add(new BmsReplayFrame(startTime + duration));
            }
        }
        frames.Add(new BmsReplayFrame(11000));
        return frames;
    }

    // Fixed expectations from beatoraja 9cddf911 JudgeManager/GaugeProperty.
    // TOTAL 12 / (one LN + one ordinary note) = 6%; CN counts both endpoints = 4%.
    [TestCase(BmsLongNoteMode.LongNote, 40, 5000, HitResult.Great, 0.26, 1)]
    [TestCase(BmsLongNoteMode.LongNote, 80, 5000, HitResult.Good, 0.23, 1)]
    [TestCase(BmsLongNoteMode.LongNote, 180, 5000, HitResult.Ok, 0.17, 0)]
    [TestCase(BmsLongNoteMode.LongNote, 0, 4890, HitResult.Great, 0.26, 1)]
    [TestCase(BmsLongNoteMode.LongNote, 0, 4860, HitResult.Good, 0.23, 1)]
    [TestCase(BmsLongNoteMode.LongNote, 0, 3500, HitResult.Ok, 0.17, 0)]
    [TestCase(BmsLongNoteMode.HellChargeNote, 0, 4890, HitResult.Great, 0.46, 2)]
    [TestCase(BmsLongNoteMode.ChargeNote, 0, 5000, HitResult.Perfect, 0.28, 2)]
    [TestCase(BmsLongNoteMode.ChargeNote, double.NaN, 5000, HitResult.Meh, 0.08, 0)]
    public void TestIndependentLongNoteOutcomes(BmsLongNoteMode noteMode, double offset, double release,
                                               HitResult finalResult, double health, int combo)
    {
        load(BmsLayoutVariant.Bme7K, noteMode, offset, release, null);
        if (noteMode == BmsLongNoteMode.HellChargeNote)
        {
            checkpoint(3250, "first HCN recovery tick", () => assertOutcome(new[] { HitResult.Perfect }, 0.26, 1));
            checkpoint(3450, "second HCN recovery tick", () => assertOutcome(new[] { HitResult.Perfect }, 0.28, 1));
            checkpoint(4950, "released successful tail still recovers", () => assertOutcome(new[] { HitResult.Perfect, HitResult.Great }, 0.46, 2));
        }
        if (double.IsNaN(offset))
            checkpoint(3300, "missed head immediately judges both endpoints", () => assertOutcome(
                new[] { HitResult.Meh, HitResult.Meh }, 0.08, 0));
        checkpoint(5400, "completed note", () =>
        {
            var expected = noteMode == BmsLongNoteMode.LongNote
                ? new[] { finalResult }
                : double.IsNaN(offset) ? new[] { HitResult.Meh, HitResult.Meh } : new[] { HitResult.Perfect, finalResult };
            assertOutcome(expected, health, combo);
        });
    }

    [TestCase(BmsLongNoteMode.LongNote, 3699, HitResult.Perfect, 0.36, 1)]
    [TestCase(BmsLongNoteMode.LongNote, 3701, HitResult.Ok, 0.28, 0)]
    [TestCase(BmsLongNoteMode.ChargeNote, 3699, HitResult.Perfect, 0.38, 2)]
    [TestCase(BmsLongNoteMode.ChargeNote, 3701, HitResult.Meh, 0.28, 0)]
    public void TestPmsReleaseRescueThroughRealInput(BmsLongNoteMode noteMode, double repress,
                                                   HitResult tail, double health, int combo)
    {
        load(BmsLayoutVariant.Pms9K, noteMode, 0, 3500, repress);
        checkpoint(3550, "pending release", () => assertOutcome(
            noteMode == BmsLongNoteMode.LongNote ? Array.Empty<HitResult>() : new[] { HitResult.Perfect },
            noteMode == BmsLongNoteMode.LongNote ? 0.30 : 0.34, noteMode == BmsLongNoteMode.LongNote ? 0 : 1));
        if (repress > 3700)
            checkpoint(3700, "release expires before input", finalOutcome);
        checkpoint(5400, "tail completed", finalOutcome);
        checkpoint(3550, "rewind into pending release", () => assertOutcome(
            noteMode == BmsLongNoteMode.LongNote ? Array.Empty<HitResult>() : new[] { HitResult.Perfect },
            noteMode == BmsLongNoteMode.LongNote ? 0.30 : 0.34, noteMode == BmsLongNoteMode.LongNote ? 0 : 1));
        if (repress > 3700)
            checkpoint(3700, "release expires again before input", finalOutcome);
        checkpoint(5400, "tail replayed", finalOutcome);

        void finalOutcome() => assertOutcome(noteMode == BmsLongNoteMode.LongNote
            ? new[] { tail } : new[] { HitResult.Perfect, tail }, health, combo);
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    public void TestPmsDeferredTimingGaugeAndScoreRewind(BmsLongNoteMode noteMode)
    {
        load(BmsLayoutVariant.Pms9K, noteMode, 0, 1500, null, 1000);
        checkpoint(1650, "pending release", pending);
        checkpoint(1700, "expiry", expired);
        checkpoint(1550, "rewind before application", pending);
        checkpoint(1700, "expiry after rewind", expired);
        checkpoint(1650, "rewind nearer application", pending);
        checkpoint(1700, "expiry again", expired);

        void pending() => assertOutcome(noteMode == BmsLongNoteMode.LongNote ? Array.Empty<HitResult>() : new[] { HitResult.Perfect },
            noteMode == BmsLongNoteMode.LongNote ? 0.30 : 0.34, noteMode == BmsLongNoteMode.LongNote ? 0 : 1);
        void expired()
        {
            assertOutcome(noteMode == BmsLongNoteMode.LongNote ? new[] { HitResult.Ok } : new[] { HitResult.Perfect, HitResult.Meh }, 0.28, 0);
            var tail = ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.Last().TimingObservations.Last();
            Assert.That(tail.ActualTime, Is.EqualTo(1500));
            Assert.That(tail.TimeOffset, Is.EqualTo(-1500));
            Assert.That(((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory.Last().Time, Is.EqualTo(1700));
        }
    }

    [TestCase(BmsLongNoteMode.LongNote, 1699, HitResult.Perfect, 0.36, 1)]
    [TestCase(BmsLongNoteMode.LongNote, 1700, HitResult.Perfect, 0.36, 1)]
    [TestCase(BmsLongNoteMode.LongNote, 1701, HitResult.Ok, 0.28, 0)]
    [TestCase(BmsLongNoteMode.ChargeNote, 1699, HitResult.Perfect, 0.38, 2)]
    [TestCase(BmsLongNoteMode.ChargeNote, 1700, HitResult.Perfect, 0.38, 2)]
    [TestCase(BmsLongNoteMode.ChargeNote, 1701, HitResult.Meh, 0.28, 0)]
    public void TestPmsRepressBeforeExpiryScan(BmsLongNoteMode noteMode, double repress, HitResult result, double health, int combo)
    {
        // JudgeManager 347-585 handles changed keys before the <= release margin scan.
        load(BmsLayoutVariant.Pms9K, noteMode, 0, 1500, repress, 1000);
        checkpoint(1698, "before boundary", () => { });
        if (repress > 1700)
            checkpoint(1700, "expiry before late input", () => { });
        checkpoint(3400, "result", () => assertOutcome(noteMode == BmsLongNoteMode.LongNote
            ? new[] { result } : new[] { HitResult.Perfect, result }, health, combo));
    }

    [Test]
    public void TestHcnHeadInputFrameDoesNotAccumulateBodyTime()
    {
        // JudgeManager 310 checks passing.state before the key changes at 347 judge the head.
        load(BmsLayoutVariant.Bme7K, BmsLongNoteMode.HellChargeNote, 100, 5000, null);
        checkpoint(3100, "late GOOD head", () => assertOutcome(new[] { HitResult.Good }, 0.22, 1));
        checkpoint(3300, "exactly 200ms since head", () => assertOutcome(new[] { HitResult.Good }, 0.22, 1));
        checkpoint(3301, "first body tick", () => assertOutcome(new[] { HitResult.Good }, 0.24, 1));
    }

    [Test]
    public void TestHcnSuccessfulReleaseAffectsBodyStartingNextFrame()
    {
        // At 4800 the signed accumulator is exactly 200ms. At 4801 input is already
        // released, but the tail is not judged until after body processing (Java 310-347).
        load(BmsLayoutVariant.Bme7K, BmsLongNoteMode.HellChargeNote, 0, 4801, null, 3000, 1901);
        checkpoint(4800, "before release", () => assertOutcome(new[] { HitResult.Perfect }, 0.40, 1));
        checkpoint(4801, "successful release frame", () => assertOutcome(new[] { HitResult.Perfect, HitResult.Great }, 0.44, 2));
        checkpoint(4802, "held-by-success reaches strict boundary", () => assertOutcome(new[] { HitResult.Perfect, HitResult.Great }, 0.44, 2));
        checkpoint(4803, "held-by-success recovery", () => assertOutcome(new[] { HitResult.Perfect, HitResult.Great }, 0.46, 2));
    }

    private void load(BmsLayoutVariant variant, BmsLongNoteMode noteMode, double offset, double release, double? repress, double start = 3000, double length = 2000)
    {
        AddStep("load replay", () =>
        {
            layout = variant;
            startTime = start;
            duration = length;
            mode = noteMode;
            headOffset = offset;
            releaseTime = release;
            repressTime = repress;
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
    }

    private void checkpoint(double time, string description, Action assertion)
    {
        AddStep(description, () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(time);
        });
        AddUntilStep("simulation reached " + description, () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < 0.001);
        AddStep("assert " + description, assertion);
    }

    private void assertOutcome(HitResult[] results, double health, int combo)
    {
        var score = (BmsScoreProcessor)Player.ScoreProcessor;
        Assert.That(score.JudgementEvents.Select(e => e.Result), Is.EqualTo(results), "ordered judgements");
        Assert.That(score.ScoringJudgementEventCount, Is.EqualTo(results.Length), "consumed endpoints");
        Assert.That(score.Combo.Value, Is.EqualTo(combo), "combo");
        Assert.That(Player.HealthProcessor.Health.Value, Is.EqualTo(health).Within(1e-8), "health");
    }
}
