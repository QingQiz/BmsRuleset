using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

internal sealed class BmsCourseResultStore
{
    internal event Action<string>? Changed;

    private readonly BmsRulesetConfigManager config;
    private readonly Dictionary<string, List<BmsCourseResult>> results;

    internal BmsCourseResultStore(BmsRulesetConfigManager config)
    {
        this.config = config;

        results = deserialize(config.Get<string>(BmsRulesetSetting.CourseResults));
    }

    internal BmsLamp GetLamp(string courseId) => TryGet(courseId, out var result) ? result.Lamp : BmsLamp.NoPlay;

    internal ScoreRank? GetRank(string courseId) => TryGet(courseId, out var result) ? result.Rank : null;

    internal bool TryGet(string courseId, out BmsCourseResult result)
    {
        if (results.TryGetValue(courseId, out var history) && history.Count > 0)
        {
            result = history.MaxBy(resultPriority);
            return true;
        }

        result = default;
        return false;
    }

    internal IReadOnlyList<BmsCourseResult> GetHistory(string courseId) =>
        results.TryGetValue(courseId, out var history) ? history : [];

    internal void Record(string courseId, BmsCourseStatus status, ScoreRank? rank = null, ScoreInfo? score = null, BmsCourseAttemptData? attempt = null)
    {
        if (status is BmsCourseStatus.InProgress or BmsCourseStatus.Aborted)
            return;

        var result = status == BmsCourseStatus.Passed
            ? new BmsCourseResult(BmsLamp.Clear, rank is null or ScoreRank.F ? ScoreRank.A : rank, BmsCourseScoreData.From(score), attempt)
            : new BmsCourseResult(BmsLamp.Failed, ScoreRank.F, BmsCourseScoreData.From(score), attempt);

        if (!results.TryGetValue(courseId, out var history))
            results[courseId] = history = [];

        history.Add(result);
        config.SetValue(BmsRulesetSetting.CourseResults, JsonSerializer.Serialize(results));
        Changed?.Invoke(courseId);
    }

    private static (int Lamp, int Rank, long Score) resultPriority(BmsCourseResult result) =>
        (result.Lamp == BmsLamp.Clear ? 1 : 0, (int)(result.Rank ?? ScoreRank.F), result.Score?.TotalScore ?? 0);

    private static Dictionary<string, List<BmsCourseResult>> deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, List<BmsCourseResult>>>(json) ?? [];
        }
        catch (JsonException)
        {
            try
            {
                var previousResults = JsonSerializer.Deserialize<Dictionary<string, BmsCourseResult>>(json) ?? [];
                return previousResults.ToDictionary(pair => pair.Key, pair => new List<BmsCourseResult> { pair.Value });
            }
            catch (JsonException)
            {
                // Course results were originally stored as lamp-only enum values.
                try
                {
                    var legacy = JsonSerializer.Deserialize<Dictionary<string, BmsLamp>>(json) ?? [];
                    return legacy.ToDictionary(pair => pair.Key, pair => new List<BmsCourseResult>
                    {
                        new(pair.Value,
                            pair.Value == BmsLamp.Clear ? ScoreRank.A
                            : pair.Value == BmsLamp.Failed ? ScoreRank.F
                            : null,
                            null),
                    });
                }
                catch (JsonException)
                {
                    return [];
                }
            }
        }
    }
}

internal readonly record struct BmsCourseResult(BmsLamp Lamp, ScoreRank? Rank, BmsCourseScoreData? Score, BmsCourseAttemptData? Attempt = null);

internal sealed record BmsCourseAttemptData
{
    public BmsCourseStatus Status { get; init; }

    public BmsGaugeType GaugeType { get; init; }

    public string[] ModAcronyms { get; init; } = [];

    public BmsCourseStageAttemptData[] Stages { get; init; } = [];

    internal static BmsCourseAttemptData From(BmsCourseSession session) => new()
    {
        Status = session.Status,
        GaugeType = session.GaugeType,
        ModAcronyms = session.Mods.Select(mod => mod.Acronym).ToArray(),
        Stages = session.Stages.Select(stage => new BmsCourseStageAttemptData
        {
            BeatmapHash = stage.Stage.Beatmap.Hash,
            Status = stage.Status,
            ScoreId = stage.Score?.ID,
            EndingHealth = stage.EndingHealth,
        }).ToArray(),
    };
}

internal sealed record BmsCourseStageAttemptData
{
    public string BeatmapHash { get; init; } = string.Empty;

    public BmsCourseStageStatus Status { get; init; }

    public Guid? ScoreId { get; init; }

    public double? EndingHealth { get; init; }
}

internal sealed record BmsCourseScoreData
{
    public long TotalScore { get; init; }

    internal static BmsCourseScoreData? From(ScoreInfo? score) => score == null
        ? null
        : new BmsCourseScoreData
        {
            TotalScore = score.TotalScore,
        };
}
