using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using System.Collections.Generic;
using System.Linq;
using System;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.LongNoteMode;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Randomisation;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Course;

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

internal sealed record BmsRestoredCourseStage(BmsCourseStageStatus Status, ScoreInfo? Score);

internal sealed class BmsCourseStageAttempt(BmsResolvedCourseStage stage)
{
    internal BmsResolvedCourseStage Stage { get; } = stage;

    internal BmsCourseStageStatus Status { get; set; }

    internal ScoreInfo? Score { get; set; }
}

internal sealed class BmsCourseSession
{
    internal BmsCourseDefinition Course { get; }

    internal IReadOnlyList<BmsCourseStageAttempt> Stages => stages;

    internal IReadOnlyList<Mod> Mods { get; private set; }

    internal BmsGaugeType GaugeType { get; }

    /// <summary>
    ///     Whether the course declares the <c>no_speed</c> constraint. Applied at play start by
    ///     locking the playfield scroll speed rather than via a mod.
    /// </summary>
    internal bool HasNoSpeedConstraint { get; }

    internal BmsCourseStatus Status { get; private set; } = BmsCourseStatus.InProgress;

    internal bool CanContinue => Status == BmsCourseStatus.InProgress;

    internal int CurrentStageIndex { get; private set; }

    internal HealthState? CurrentHealth { get; private set; }

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
        HasNoSpeedConstraint = course.Constraints.Any(constraint => constraint.Equals("no_speed", StringComparison.OrdinalIgnoreCase));
        updateHealthSettings();

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
        }

        session.CurrentStageIndex = Math.Max(0, attempts.TakeWhile(attempt => attempt.Status != BmsCourseStageStatus.NotPlayed).Count() - 1);
        return session;
    }

    internal void BeginCurrentStage()
    {
        ensureCanContinue();

        if (CurrentStage.Status != BmsCourseStageStatus.NotPlayed)
            throw new InvalidOperationException("The current BMS course stage has already started.");

        CurrentStage.Status = BmsCourseStageStatus.Playing;
    }

    internal void CompleteCurrentStage(ScoreInfo score, HealthState health)
    {
        ensurePlaying();
        storeResult(score, health, score.Passed ? BmsCourseStageStatus.Passed : BmsCourseStageStatus.Failed);

        if (!score.Passed)
        {
            Status = BmsCourseStatus.Failed;
            return;
        }

        if (CurrentStageIndex == stages.Length - 1)
            Status = BmsCourseStatus.Passed;
    }

    internal void FailCurrentStage(ScoreInfo score, HealthState health)
    {
        ensurePlaying();
        storeResult(score, health, BmsCourseStageStatus.Failed);
        Status = BmsCourseStatus.Failed;
    }

    internal void AbortCurrentStage(ScoreInfo score, HealthState health)
    {
        ensurePlaying();
        storeResult(score, health, BmsCourseStageStatus.Aborted);
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
        ensureCanContinue();

        if (CurrentStage.Status != BmsCourseStageStatus.Passed)
            throw new InvalidOperationException("A BMS course can only be abandoned from results after completing the current stage.");

        Status = BmsCourseStatus.Aborted;
    }

    internal void RequestAdvance()
    {
        ensureCanContinue();

        if (CurrentStage.Status != BmsCourseStageStatus.Passed || CurrentStageIndex >= stages.Length - 1)
            throw new InvalidOperationException("The BMS course cannot advance from its current state.");

        AdvanceRequested = true;
    }

    internal void Advance()
    {
        ensureCanContinue();

        if (!AdvanceRequested)
            throw new InvalidOperationException("No BMS course stage advance is pending.");

        AdvanceRequested = false;
        CurrentStageIndex++;
        // Each stage owns its settings so advancing cannot mutate the preceding stage's saved score.
        Mods = Mods.Select(mod => mod.DeepClone()).ToArray();
        updateHealthSettings();
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

    internal static IReadOnlyList<Mod> CreateCourseMods(IReadOnlyList<Mod> selectedMods, BmsGaugeType gaugeType, IEnumerable<string>? constraints = null)
    {
        var selected = selectedMods.ToArray();
        var usesAutoGauge = selected.Any(mod => mod is BmsModAutoGauge);
        var requiredMods = CreateConstraintMods(constraints ?? []);
        var hasLnConstraint = requiredMods.Any(mod => mod is BmsModLongNoteModeBase);
        var forbidden = ResolveForbiddenModTypes(constraints ?? []).ToArray();

        var mods = selected
            .Where(mod => mod is not BmsModGauge and not BmsModAutoGauge)
            .Where(mod => !hasLnConstraint || mod is not BmsModLongNoteModeBase)
            .Where(mod => !forbidden.Any(type => type.IsInstanceOfType(mod)))
            .Select(mod => mod.DeepClone())
            .ToList();

        mods.Add(usesAutoGauge ? new BmsModAutoGauge() : CreateGaugeMod(gaugeType));

        mods.AddRange(requiredMods);

        return mods;
    }

    internal static List<Mod> CreateConstraintMods(IEnumerable<string> constraints)
    {
        var mods = new List<Mod>();
        var lnMode = BmsLongNoteMode.Undefined;
        var judgementConstraint = 2;

        foreach (var constraint in constraints.Select(constraint => constraint.ToLowerInvariant()))
        {
            switch (constraint)
            {
                case "no_good":
                    judgementConstraint = Math.Min(judgementConstraint, 1);
                    break;

                case "no_great":
                    judgementConstraint = Math.Min(judgementConstraint, 0);
                    break;

                case "ln":
                    lnMode = BmsLongNoteMode.LongNote;
                    break;

                case "cn":
                    lnMode = BmsLongNoteMode.ChargeNote;
                    break;

                case "hcn":
                    lnMode = BmsLongNoteMode.HellChargeNote;
                    break;
            }
        }

        if (judgementConstraint == 0)
            mods.Add(new BmsModNoGreat());
        else if (judgementConstraint == 1)
            mods.Add(new BmsModNoGood());

        if (lnMode != BmsLongNoteMode.Undefined)
            mods.Add(lnMode switch
            {
                BmsLongNoteMode.LongNote => new BmsModLongNote(),
                BmsLongNoteMode.ChargeNote => new BmsModChargeNote(),
                _ => new BmsModHellChargeNote(),
            });

        return mods;
    }

    /// <summary>
    ///     Returns the mod types the given course constraints forbid the user from selecting.
    ///     Used to adjust the selected mods and to disable the matching entries in the mod select
    ///     overlay while the course is selected, mirroring beatoraja's select-screen handling.
    /// </summary>
    internal static IEnumerable<Type> ResolveForbiddenModTypes(IEnumerable<string> constraints)
    {
        var forbidden = new HashSet<Type> { typeof(BmsModAutoplay), typeof(IApplicableToGameplayProfile) };

        foreach (var constraint in constraints.Select(constraint => constraint.ToLowerInvariant()))
        {
            switch (constraint)
            {
                // grade: only IDENTITY survives — mirror and all shuffles are forbidden.
                case "grade":
                    forbidden.Add(typeof(BmsModMirror));
                    forbidden.Add(typeof(BmsModLaneRandom));
                    forbidden.Add(typeof(BmsModNoteRandom));
                    forbidden.Add(typeof(BmsModRotationRandom));
                    break;

                // grade_mirror: MIRROR or IDENTITY; shuffles are forbidden.
                case "grade_mirror":
                    forbidden.Add(typeof(BmsModLaneRandom));
                    forbidden.Add(typeof(BmsModNoteRandom));
                    forbidden.Add(typeof(BmsModRotationRandom));
                    break;

                // no_speed fixes the scroll speed, so constant scroll must not override it either.
                case "no_speed":
                    forbidden.Add(typeof(BmsModConstant));
                    break;

                case "ln":
                    forbidden.Add(typeof(BmsModChargeNote));
                    forbidden.Add(typeof(BmsModHellChargeNote));
                    break;

                case "cn":
                    forbidden.Add(typeof(BmsModLongNote));
                    forbidden.Add(typeof(BmsModHellChargeNote));
                    break;

                case "hcn":
                    forbidden.Add(typeof(BmsModLongNote));
                    forbidden.Add(typeof(BmsModChargeNote));
                    break;
            }
        }

        return forbidden;
    }

    private void updateHealthSettings()
    {
        var family = ResolveCourseGaugeProfileFamily(Course.Constraints);
        var previous = CurrentHealth is { } health ? GetGaugeStates(health) : [];
        if (Mods.OfType<BmsModAutoGauge>().FirstOrDefault() is { } autoGauge)
        {
            autoGauge.GaugeProfileFamily.Value = family;
            autoGauge.ClassHealth.Value = startingHealth(BmsGaugeType.Class);
            autoGauge.ExClassHealth.Value = startingHealth(BmsGaugeType.ExClass);
            autoGauge.ExHardClassHealth.Value = startingHealth(BmsGaugeType.ExHardClass);
        }
        else if (Mods.OfType<BmsModGauge>().FirstOrDefault() is { } gauge)
        {
            gauge.GaugeProfileFamily.Value = family;
            gauge.InitialHealth.Value = startingHealth(gauge.GaugeType);
        }

        double? startingHealth(BmsGaugeType type)
            => CurrentHealth == null ? 1 : previous.FirstOrDefault(state => state.GaugeType == type && !state.Failed)?.Health;
    }

    internal IReadOnlyList<BmsGaugeStateSnapshot> GetGaugeStates(HealthState health)
    {
        var types = Mods.OfType<BmsModAutoGauge>().FirstOrDefault() is { } autoGauge
            ? new[]
            {
                (BmsGaugeType.ExHardClass, autoGauge.ExHardClassHealth.Value),
                (BmsGaugeType.ExClass, autoGauge.ExClassHealth.Value),
                (BmsGaugeType.Class, autoGauge.ClassHealth.Value),
            }.Where(pair => pair.Item2.HasValue).Select(pair => pair.Item1).ToArray()
            : Mods.OfType<BmsModGauge>().Select(mod => mod.GaugeType).ToArray();

        return types.Select((type, index) => new BmsGaugeStateSnapshot(type, health.Layers[index].Value, health.Layers[index].HasFailed)).ToArray();
    }

    private void storeResult(ScoreInfo score, HealthState health, BmsCourseStageStatus stageStatus)
    {
        CurrentHealth = health;
        CurrentStage.Score = score.DeepClone();
        CurrentStage.Status = stageStatus;
    }

    private void ensureCanContinue()
    {
        if (!CanContinue)
            throw new InvalidOperationException("The BMS course session has already ended.");
    }

    private void ensurePlaying()
    {
        ensureCanContinue();

        if (CurrentStage.Status != BmsCourseStageStatus.Playing)
            throw new InvalidOperationException("The current BMS course stage is not being played.");
    }
}
