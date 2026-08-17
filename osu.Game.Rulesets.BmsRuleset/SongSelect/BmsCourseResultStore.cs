using System;
using System.Collections.Generic;
using System.Text.Json;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

internal sealed class BmsCourseResultStore
{
    internal event Action<string>? Changed;

    private readonly BmsRulesetConfigManager config;
    private readonly Dictionary<string, BmsCourseResult> results;

    internal BmsCourseResultStore(BmsRulesetConfigManager config)
    {
        this.config = config;

        results = deserialize(config.Get<string>(BmsRulesetSetting.CourseResults));
    }

    internal BmsLamp GetLamp(string courseId) => results.GetValueOrDefault(courseId).Lamp;

    internal ScoreRank? GetRank(string courseId) => results.GetValueOrDefault(courseId).Rank;

    internal void Record(string courseId, BmsCourseStatus status, ScoreRank? rank = null)
    {
        if (status == BmsCourseStatus.InProgress)
            return;

        var result = status == BmsCourseStatus.Passed
            ? new BmsCourseResult(BmsLamp.Clear, rank is null or ScoreRank.F ? ScoreRank.A : rank)
            : new BmsCourseResult(BmsLamp.Failed, ScoreRank.F);

        if (results.TryGetValue(courseId, out var previous))
        {
            if (previous.Lamp == BmsLamp.Clear)
            {
                if (result.Lamp != BmsLamp.Clear || previous.Rank >= result.Rank)
                    return;
            }

            if (previous == result)
                return;
        }

        results[courseId] = result;
        config.SetValue(BmsRulesetSetting.CourseResults, JsonSerializer.Serialize(results));
        Changed?.Invoke(courseId);
    }

    private static Dictionary<string, BmsCourseResult> deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, BmsCourseResult>>(json) ?? [];
        }
        catch (JsonException)
        {
            // Course results were originally stored as lamp-only enum values.
            try
            {
                var legacy = JsonSerializer.Deserialize<Dictionary<string, BmsLamp>>(json) ?? [];
                var migrated = new Dictionary<string, BmsCourseResult>();

                foreach (var (courseId, lamp) in legacy)
                {
                    var rank = lamp == BmsLamp.Clear ? ScoreRank.A
                        : lamp == BmsLamp.Failed ? ScoreRank.F
                        : (ScoreRank?)null;
                    migrated[courseId] = new BmsCourseResult(lamp, rank);
                }

                return migrated;
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }
}

internal readonly record struct BmsCourseResult(BmsLamp Lamp, ScoreRank? Rank);
