using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

internal enum BmsCourseStatus
{
    InProgress,
    Passed,
    Failed,
    Aborted,
}

internal enum BmsCourseStageStatus
{
    NotPlayed,
    Playing,
    Passed,
    Failed,
    Aborted,
}

internal sealed record BmsResolvedCourseStage(BmsCourseStage Definition, BeatmapInfo Beatmap);

internal sealed record BmsRestoredCourseStage(BmsCourseStageStatus Status, ScoreInfo? Score, double? EndingHealth);

internal sealed class BmsCourseStageAttempt(BmsResolvedCourseStage stage)
{
    internal BmsResolvedCourseStage Stage { get; } = stage;

    internal BmsCourseStageStatus Status { get; set; }

    internal ScoreInfo? Score { get; set; }

    internal double? EndingHealth { get; set; }
}

internal sealed class BmsCourseSession
{
    internal BmsCourseDefinition Course { get; }

    internal IReadOnlyList<BmsCourseStageAttempt> Stages => stages;

    internal IReadOnlyList<Mod> Mods { get; }

    internal BmsGaugeType GaugeType { get; }

    internal BmsCourseStatus Status { get; private set; } = BmsCourseStatus.InProgress;

    internal int CurrentStageIndex { get; private set; }

    internal double CurrentHealth { get; private set; } = 1;

    internal bool AdvanceRequested { get; private set; }

    internal bool SummaryShown { get; set; }

    internal BmsCourseStageAttempt CurrentStage => stages[CurrentStageIndex];

    private readonly BmsCourseStageAttempt[] stages;

    internal BmsCourseSession(BmsCourseDefinition course, IEnumerable<BmsResolvedCourseStage> stages, IEnumerable<Mod> mods, BmsGaugeType gaugeType)
    {
        Course = course;
        this.stages = stages.Select(stage => new BmsCourseStageAttempt(stage)).ToArray();
        Mods = mods.Select(mod => mod.DeepClone()).ToArray();
        GaugeType = gaugeType;

        if (this.stages.Length == 0)
            throw new ArgumentException(@"A BMS course must contain at least one stage.", nameof(stages));
    }

    internal static BmsCourseSession Restore(
        BmsCourseDefinition course,
        IReadOnlyList<BmsResolvedCourseStage> stages,
        IEnumerable<Mod> mods,
        BmsGaugeType gaugeType,
        BmsCourseStatus status,
        IReadOnlyList<BmsRestoredCourseStage> attempts)
    {
        if (stages.Count != attempts.Count)
            throw new ArgumentException(@"The restored BMS course stages and attempts must have the same length.", nameof(attempts));

        var session = new BmsCourseSession(course, stages, mods, gaugeType)
        {
            Status = status,
            SummaryShown = true,
        };

        for (int i = 0; i < attempts.Count; i++)
        {
            session.stages[i].Status = attempts[i].Status;
            session.stages[i].Score = attempts[i].Score?.DeepClone();
            session.stages[i].EndingHealth = attempts[i].EndingHealth;
        }

        session.CurrentStageIndex = Math.Max(0, attempts.TakeWhile(attempt => attempt.Status != BmsCourseStageStatus.NotPlayed).Count() - 1);
        session.CurrentHealth = attempts.Take(session.CurrentStageIndex + 1)
                                        .Select(attempt => attempt.EndingHealth)
                                        .LastOrDefault(health => health.HasValue) ?? 1;
        return session;
    }

    internal void BeginCurrentStage()
    {
        ensureInProgress();

        if (CurrentStage.Status != BmsCourseStageStatus.NotPlayed)
            throw new InvalidOperationException("The current BMS course stage has already started.");

        CurrentStage.Status = BmsCourseStageStatus.Playing;
    }

    internal void CompleteCurrentStage(ScoreInfo score, double endingHealth)
    {
        ensurePlaying();
        storeResult(score, endingHealth, score.Passed ? BmsCourseStageStatus.Passed : BmsCourseStageStatus.Failed);

        if (!score.Passed)
        {
            Status = BmsCourseStatus.Failed;
            return;
        }

        if (CurrentStageIndex == stages.Length - 1)
            Status = BmsCourseStatus.Passed;
    }

    internal void FailCurrentStage(ScoreInfo score, double endingHealth)
    {
        ensurePlaying();
        storeResult(score, endingHealth, BmsCourseStageStatus.Failed);
        Status = BmsCourseStatus.Failed;
    }

    internal void AbortCurrentStage(ScoreInfo score, double endingHealth)
    {
        ensurePlaying();
        storeResult(score, endingHealth, BmsCourseStageStatus.Aborted);
        Status = BmsCourseStatus.Aborted;
    }

    internal void AbortCurrentStageWithoutScore()
    {
        ensurePlaying();
        CurrentStage.Status = BmsCourseStageStatus.Aborted;
        Status = BmsCourseStatus.Aborted;
    }

    internal void AbortAfterStageResult()
    {
        ensureInProgress();

        if (CurrentStage.Status != BmsCourseStageStatus.Passed)
            throw new InvalidOperationException("A BMS course can only be abandoned from results after completing the current stage.");

        Status = BmsCourseStatus.Aborted;
    }

    internal void RequestAdvance()
    {
        ensureInProgress();

        if (CurrentStage.Status != BmsCourseStageStatus.Passed || CurrentStageIndex >= stages.Length - 1)
            throw new InvalidOperationException("The BMS course cannot advance from its current state.");

        AdvanceRequested = true;
    }

    internal void Advance()
    {
        ensureInProgress();

        if (!AdvanceRequested)
            throw new InvalidOperationException("No BMS course stage advance is pending.");

        AdvanceRequested = false;
        CurrentStageIndex++;
    }

    internal static bool TryParseGauge(string gauge, out BmsGaugeType gaugeType)
    {
        var normalized = gauge.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        return Enum.TryParse(normalized, true, out gaugeType)
               && gaugeType is BmsGaugeType.Class or BmsGaugeType.ExClass or BmsGaugeType.ExHardClass;
    }

    internal static BmsModGauge CreateGaugeMod(BmsGaugeType gaugeType) => gaugeType switch
    {
        BmsGaugeType.Class => new BmsModClassGauge(),
        BmsGaugeType.ExClass => new BmsModExClassGauge(),
        BmsGaugeType.ExHardClass => new BmsModExHardClassGauge(),
        _ => throw new ArgumentOutOfRangeException(nameof(gaugeType), gaugeType, null),
    };

    internal static IReadOnlyList<Mod> CreateCourseMods(IEnumerable<Mod> selectedMods, BmsGaugeType gaugeType) =>
        selectedMods.Where(mod => mod is not BmsModGauge and not BmsModAutoGauge)
            .Select(mod => mod.DeepClone())
            .Append(CreateGaugeMod(gaugeType))
            .ToArray();

    private void storeResult(ScoreInfo score, double endingHealth, BmsCourseStageStatus stageStatus)
    {
        CurrentHealth = Math.Clamp(endingHealth, 0, 1);
        CurrentStage.Score = score.DeepClone();
        CurrentStage.EndingHealth = CurrentHealth;
        CurrentStage.Status = stageStatus;
    }

    private void ensureInProgress()
    {
        if (Status != BmsCourseStatus.InProgress)
            throw new InvalidOperationException("The BMS course session has already ended.");
    }

    private void ensurePlaying()
    {
        ensureInProgress();

        if (CurrentStage.Status != BmsCourseStageStatus.Playing)
            throw new InvalidOperationException("The current BMS course stage is not being played.");
    }
}
