using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Course;

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

    internal BmsGaugeProfileFamily? GaugeProfileFamilyOverride { get; }

    internal BmsCourseStatus Status { get; private set; } = BmsCourseStatus.InProgress;

    internal int CurrentStageIndex { get; private set; }

    internal double CurrentHealth { get; private set; } = 1;

    internal IReadOnlyList<BmsGaugeStateSnapshot> CurrentGaugeStates { get; private set; }

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
        GaugeProfileFamilyOverride = ResolveCourseGaugeProfileFamily(course.Constraints);
        CurrentGaugeStates = [];

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

        for (var i = 0; i < attempts.Count; i++)
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

    internal void CompleteCurrentStage(ScoreInfo score, IReadOnlyList<BmsGaugeStateSnapshot> gaugeStates)
    {
        ensurePlaying();
        storeResult(score, gaugeStates, score.Passed ? BmsCourseStageStatus.Passed : BmsCourseStageStatus.Failed);

        if (!score.Passed)
        {
            Status = BmsCourseStatus.Failed;
            return;
        }

        if (CurrentStageIndex == stages.Length - 1)
            Status = BmsCourseStatus.Passed;
    }

    internal void FailCurrentStage(ScoreInfo score, IReadOnlyList<BmsGaugeStateSnapshot> gaugeStates)
    {
        ensurePlaying();
        storeResult(score, gaugeStates, BmsCourseStageStatus.Failed);
        Status = BmsCourseStatus.Failed;
    }

    internal void AbortCurrentStage(ScoreInfo score, IReadOnlyList<BmsGaugeStateSnapshot> gaugeStates)
    {
        ensurePlaying();
        storeResult(score, gaugeStates, BmsCourseStageStatus.Aborted);
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

    internal static BmsModGauge CreateGaugeMod(BmsGaugeType gaugeType) => gaugeType switch
    {
        BmsGaugeType.Class => new BmsModClassGauge(),
        BmsGaugeType.ExClass => new BmsModExClassGauge(),
        BmsGaugeType.ExHardClass => new BmsModExHardClassGauge(),
        _ => throw new ArgumentOutOfRangeException(nameof(gaugeType), gaugeType, null),
    };

    internal static BmsGaugeType ResolveCourseGaugeType(IEnumerable<Mod> selectedMods)
    {
        var selected = selectedMods.ToArray();
        if (selected.Any(mod => mod is BmsModAutoGauge))
            return BmsGaugeType.ExHardClass;

        var selectedGauge = selected.OfType<BmsModGauge>().FirstOrDefault()?.GaugeType ?? BmsGaugeType.Normal;

        // beatoraja exposes the regular six gauge choices in course mode and maps them
        // onto the three class tiers instead of letting the course definition choose a tier.
        return selectedGauge switch
        {
            BmsGaugeType.AssistEasy or BmsGaugeType.Easy or BmsGaugeType.Normal or BmsGaugeType.Class => BmsGaugeType.Class,
            BmsGaugeType.Hard or BmsGaugeType.ExClass => BmsGaugeType.ExClass,
            BmsGaugeType.ExHard or BmsGaugeType.Hazard or BmsGaugeType.ExHardClass => BmsGaugeType.ExHardClass,
            _ => BmsGaugeType.Class,
        };
    }

    internal static BmsGaugeProfileFamily? ResolveCourseGaugeProfileFamily(IEnumerable<string> constraints)
    {
        BmsGaugeProfileFamily? family = null;

        foreach (var constraint in constraints)
        {
            family = constraint.ToLowerInvariant() switch
            {
                "gauge_lr2" => BmsGaugeProfileFamily.Lr2,
                "gauge_5k" => BmsGaugeProfileFamily.FiveKeys,
                "gauge_7k" => BmsGaugeProfileFamily.SevenKeys,
                "gauge_9k" => BmsGaugeProfileFamily.Pms,
                "gauge_24k" => BmsGaugeProfileFamily.Keyboard,
                _ => family,
            };
        }

        return family;
    }

    internal static IReadOnlyList<Mod> CreateCourseMods(IEnumerable<Mod> selectedMods, BmsGaugeType gaugeType)
    {
        var selected = selectedMods.ToArray();
        var usesAutoGauge = selected.Any(mod => mod is BmsModAutoGauge);
        var mods = selected.Where(mod => mod is not BmsModGauge and not BmsModAutoGauge)
            .Select(mod => mod.DeepClone())
            .ToList();

        mods.Add(usesAutoGauge ? new BmsModAutoGauge() : CreateGaugeMod(gaugeType));
        return mods;
    }

    internal void ConfigureHealthProcessor(BmsHealthProcessor healthProcessor)
    {
        healthProcessor.ConfigureGaugeContext(isCourseGaugeMode: true, familyOverride: GaugeProfileFamilyOverride);

        var usesAutoGauge = Mods.Any(mod => mod is BmsModAutoGauge);
        var initialStates = CurrentGaugeStates.Where(state => !state.Failed).ToArray();
        var gaugeTypes = usesAutoGauge && CurrentGaugeStates.Count > 0
            ? initialStates.Select(state => state.GaugeType).ToArray()
            : usesAutoGauge
                ? BmsModAutoGauge.COURSE_AUTO_GAUGE_CHAIN
                : [GaugeType];

        // The processor may already contain the regular AG chain if its load completed before
        // the course player. Replace it so course gameplay never retains regular gauge states.
        healthProcessor.SetGaugeTypes(
            gaugeTypes,
            replaceExisting: true,
            profileFamilyOverride: GaugeProfileFamilyOverride,
            initialStates: initialStates);
    }

    private void storeResult(ScoreInfo score, IReadOnlyList<BmsGaugeStateSnapshot> gaugeStates, BmsCourseStageStatus stageStatus)
    {
        CurrentGaugeStates = gaugeStates.Select(state => state with { }).ToArray();
        CurrentHealth = CurrentGaugeStates.FirstOrDefault(state => !state.Failed)?.Health ?? 0;
        CurrentStage.Score = BmsScoreGaugeHistoryStore.Clone(score);
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
