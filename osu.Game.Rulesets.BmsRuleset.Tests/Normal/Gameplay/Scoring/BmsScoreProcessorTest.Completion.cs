using System.Linq;
using NUnit.Framework;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Scoring;

public partial class BmsScoreProcessorTest
{
    [Test]
    public void TestCompletionWaitsForFinalGaugeDamage([Values] bool autoGauge, [Values(1280, 1281, 2000)] double completionTime)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        };
        var manualClock = new ManualClock { CurrentTime = completionTime };
        var framedClock = new FramedClock(manualClock);
        var scoreProcessor = new TestBmsScoreProcessor { Clock = framedClock };
        var healthProcessor = new BmsHealthProcessor();
        Mod[] mods = autoGauge ? [new BmsModAutoGauge()] : [];
        scoreProcessor.Mods.Value = mods;
        scoreProcessor.ApplyBeatmap(beatmap);
        healthProcessor.ApplyBeatmap(beatmap);

        foreach (var mod in mods.OfType<IApplicableToHealthProcessor>())
            mod.ApplyToHealthProcessor(healthProcessor);

        healthProcessor.RestoreGaugeStates(healthProcessor.CurrentGaugeStates.Select(state =>
            new BmsGaugeStateSnapshot(state.GaugeType, state.GaugeType > BmsGaugeType.Normal ? 0 : 0.83,
                state.GaugeType > BmsGaugeType.Normal)).ToArray());

        var score = new Score { ScoreInfo = { Mods = mods, Passed = true } };
        var gameplayState = new GameplayState(beatmap, new BmsRuleset(), mods, score, scoreProcessor, healthProcessor);
        using var completion = new BmsGameplayCompletionController(scoreProcessor, healthProcessor, gameplayState, null, null);

        // A frame may check completion before the final drawable processes its passive POOR.
        framedClock.ProcessFrame();
        scoreProcessor.UpdateCompletion();
        var completedBeforeDamage = scoreProcessor.HasCompleted.Value;

        manualClock.CurrentTime = completionTime + 1;
        framedClock.ProcessFrame();
        var note = beatmap.HitObjects.Single();
        var result = new JudgementResult(note, note.CreateJudgement()) { Type = HitResult.Meh };
        healthProcessor.ApplyResult(result);
        scoreProcessor.ApplyResult(result);
        scoreProcessor.UpdateCompletion();

        Assert.Multiple(() =>
        {
            Assert.That(completedBeforeDamage, Is.False);
            Assert.That(scoreProcessor.HasCompleted.Value, Is.True);
            Assert.That(healthProcessor.Health.Value, Is.LessThan(0.8));
            Assert.That(score.ScoreInfo.Passed, Is.EqualTo(autoGauge));
            Assert.That(healthProcessor.WorstGaugeType, Is.EqualTo(autoGauge ? BmsGaugeType.AssistEasy : BmsGaugeType.Normal));
            Assert.That(score.ScoreInfo.Rank == ScoreRank.F, Is.EqualTo(!autoGauge));
            Assert.That(score.ScoreInfo.Mods.OfType<BmsModAssistEasyGauge>().Any(), Is.EqualTo(autoGauge));
            Assert.That(BmsScoreGaugeHistoryStore.TryGet(score.ScoreInfo, out var history), Is.True);
            Assert.That(history.LastOrDefault()?.States.Single(state => state.GaugeType == BmsGaugeType.Normal).Health, Is.EqualTo(0.77).Within(1e-12));
        });
    }

    [Test]
    public void TestCompletionWaitsForChargeTail([Values(BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode mode)
    {
        var (processor, source) = createLongNoteProcessor(mode);
        processor.Clock = new FramedClock(new ManualClock { CurrentTime = 10000 });
        processor.ApplyResult(createEndpointResult(source, BmsLongNoteEndpointKind.Head, 1000));
        processor.UpdateCompletion();
        Assert.That(processor.HasCompleted.Value, Is.False);

        processor.ApplyResult(createEndpointResult(source, BmsLongNoteEndpointKind.Tail, 1600));
        processor.UpdateCompletion();
        Assert.That(processor.HasCompleted.Value, Is.True);
    }

    [Test]
    public void TestCompletionIgnoresEmptyPoorAndLandmineCounts([Values] bool detonateMine)
    {
        var note = new BmsHitObject { StartTime = 1000, Column = 1 };
        var mine = new BmsLandmine { StartTime = 500, Column = 2 };
        var processor = new TestBmsScoreProcessor { Clock = new FramedClock(new ManualClock { CurrentTime = 10000 }) };
        processor.ApplyBeatmap(new BmsBeatmap { HitObjects = { mine, note } });

        processor.RegisterEmptyPoor(600);
        Assert.That(processor.ScoringJudgementEventCount, Is.Zero);

        if (detonateMine)
            processor.ApplyResult(new JudgementResult(mine, mine.CreateJudgement()) { Type = HitResult.Meh });

        Assert.That(processor.ScoringJudgementEventCount, Is.Zero);
        processor.UpdateCompletion();
        Assert.That(processor.HasCompleted.Value, Is.False);

        processor.ApplyResult(new JudgementResult(note, note.CreateJudgement()) { Type = HitResult.Perfect });
        Assert.That(processor.ScoringJudgementEventCount, Is.EqualTo(1));
        processor.UpdateCompletion();
        Assert.That(processor.HasCompleted.Value, Is.True);
    }

    [Test]
    public void TestCompletionWithFinalLandmine([Values] bool autoGauge, [Values] bool detonateMine, [Values] bool includeNote)
    {
        var mine = new BmsLandmine { StartTime = 3000, Column = 2, LandmineDamagePercent = 10 };
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8 };
        if (includeNote)
            beatmap.HitObjects.Add(new BmsHitObject { StartTime = 1000, Column = 1 });
        beatmap.HitObjects.Add(mine);

        var manualClock = new ManualClock();
        var framedClock = new FramedClock(manualClock);
        var scoreProcessor = new TestBmsScoreProcessor { Clock = framedClock };
        var healthProcessor = new BmsHealthProcessor();
        Mod[] mods = autoGauge ? [new BmsModAutoGauge()] : [];
        scoreProcessor.Mods.Value = mods;
        scoreProcessor.ApplyBeatmap(beatmap);
        healthProcessor.ApplyBeatmap(beatmap);
        foreach (var mod in mods.OfType<IApplicableToHealthProcessor>())
            mod.ApplyToHealthProcessor(healthProcessor);

        if (includeNote)
        {
            var note = beatmap.HitObjects[0];
            var result = new JudgementResult(note, note.CreateJudgement()) { Type = HitResult.Perfect };
            healthProcessor.ApplyResult(result);
            scoreProcessor.ApplyResult(result);
        }

        healthProcessor.RestoreGaugeStates(healthProcessor.CurrentGaugeStates.Select(state =>
            new BmsGaugeStateSnapshot(state.GaugeType, state.GaugeType > BmsGaugeType.Normal ? 0 : 0.83,
                state.GaugeType > BmsGaugeType.Normal)).ToArray());

        var score = new Score { ScoreInfo = { Mods = mods, Passed = true } };
        var gameplayState = new GameplayState(beatmap, new BmsRuleset(), mods, score, scoreProcessor, healthProcessor);
        using var completion = new BmsGameplayCompletionController(scoreProcessor, healthProcessor, gameplayState, null, null);

        manualClock.CurrentTime = mine.StartTime - 1;
        framedClock.ProcessFrame();
        scoreProcessor.UpdateCompletion();
        Assert.That(scoreProcessor.HasCompleted.Value, Is.False);

        manualClock.CurrentTime = mine.StartTime;
        framedClock.ProcessFrame();
        scoreProcessor.UpdateCompletion();
        Assert.That(scoreProcessor.HasCompleted.Value, Is.False);

        if (detonateMine)
        {
            var result = new JudgementResult(mine, mine.CreateJudgement()) { Type = HitResult.Meh };
            healthProcessor.ApplyResult(result);
            scoreProcessor.ApplyResult(result);
        }

        manualClock.CurrentTime = mine.StartTime + BmsHitWindows.FALLBACK_BAD_WINDOW - 1;
        framedClock.ProcessFrame();
        scoreProcessor.UpdateCompletion();
        Assert.That(scoreProcessor.HasCompleted.Value, Is.False);

        manualClock.CurrentTime++;
        framedClock.ProcessFrame();
        scoreProcessor.UpdateCompletion();

        Assert.Multiple(() =>
        {
            Assert.That(scoreProcessor.ScoringJudgementEventCount, Is.EqualTo(includeNote ? 1 : 0));
            Assert.That(scoreProcessor.HasCompleted.Value, Is.True);
            Assert.That(healthProcessor.Health.Value, Is.EqualTo(detonateMine ? 0.73 : 0.83).Within(1e-12));
            Assert.That(score.ScoreInfo.Passed, Is.EqualTo(!detonateMine || autoGauge));
            Assert.That(healthProcessor.WorstGaugeType, Is.EqualTo(detonateMine && autoGauge ? BmsGaugeType.AssistEasy : BmsGaugeType.Normal));
            Assert.That(score.ScoreInfo.Mods.OfType<BmsModAssistEasyGauge>().Any(), Is.EqualTo(detonateMine && autoGauge));
        });
    }

    [Test]
    public void TestCompletionWithNoScoringNotes([Values] bool includeMine)
    {
        var beatmap = new BmsBeatmap();
        if (includeMine)
            beatmap.HitObjects.Add(new BmsLandmine { StartTime = 1000, Column = 1 });

        var processor = new TestBmsScoreProcessor { Clock = new FramedClock(new ManualClock { CurrentTime = 10000 }) };
        processor.ApplyBeatmap(beatmap);
        processor.UpdateCompletion();
        Assert.That(processor.HasCompleted.Value, Is.True);
    }

    [Test]
    public void TestCompletionClearsWhenFinalJudgementReverted()
    {
        var (processor, source) = createLongNoteProcessor();
        processor.Clock = new FramedClock(new ManualClock { CurrentTime = 10000 });
        var result = createLongNoteResult(source, 1000, 1500);
        processor.ApplyResult(result);
        processor.UpdateCompletion();
        Assert.That(processor.HasCompleted.Value, Is.True);

        processor.RevertResult(result);
        processor.UpdateCompletion();
        Assert.That(processor.HasCompleted.Value, Is.False);

        processor.ApplyResult(createLongNoteResult(source, 1000, 1500));
        processor.UpdateCompletion();
        Assert.That(processor.HasCompleted.Value, Is.True);
    }

    [Test]
    public void TestRewindDoesNotFinaliseScore()
    {
        var beatmap = new BmsBeatmap { HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } } };
        var manualClock = new ManualClock { CurrentTime = 10000 };
        var framedClock = new FramedClock(manualClock);
        var scoreProcessor = new TestBmsScoreProcessor { Clock = framedClock };
        var healthProcessor = new BmsHealthProcessor();
        scoreProcessor.ApplyBeatmap(beatmap);
        healthProcessor.ApplyBeatmap(beatmap);
        healthProcessor.RestoreGaugeStates([new(BmsGaugeType.Normal, 1, false)]);
        var gameplayState = new GameplayState(beatmap, new BmsRuleset(), scoreProcessor: scoreProcessor, healthProcessor: healthProcessor);
        using var completion = new BmsGameplayCompletionController(scoreProcessor, healthProcessor, gameplayState, null, null);

        var note = beatmap.HitObjects.Single();
        scoreProcessor.ApplyResult(new JudgementResult(note, note.CreateJudgement()) { Type = HitResult.Perfect });
        scoreProcessor.UpdateCompletion();
        Assert.That(scoreProcessor.HasCompleted.Value, Is.True);

        healthProcessor.RestoreGaugeStates([new(BmsGaugeType.Normal, 0.2, false)]);
        manualClock.CurrentTime = 0;
        framedClock.ProcessFrame();
        scoreProcessor.UpdateCompletion();

        Assert.Multiple(() =>
        {
            Assert.That(scoreProcessor.HasCompleted.Value, Is.False);
            Assert.That(scoreProcessor.Rank.Value, Is.Not.EqualTo(ScoreRank.F));
        });
    }
}
