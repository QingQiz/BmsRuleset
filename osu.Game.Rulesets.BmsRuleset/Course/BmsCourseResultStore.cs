using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.SongSelect;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Course;

internal sealed class BmsCourseResultStore
{
    internal event Action<string>? Changed;

    private const string file_extension = ".json";

    private readonly Dictionary<string, List<IndexedCourseResult>> indexByCourseKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<BmsCourseResult>> loadedResults = new(StringComparer.Ordinal);
    private readonly object sync = new();
    private long lastFileTimestamp;

    internal string StorageDirectory { get; }

    internal BmsCourseResultStore(string storageDirectory)
    {
        StorageDirectory = storageDirectory;
        Directory.CreateDirectory(storageDirectory);
        indexPersistedFiles();
    }

    internal BmsLamp GetLamp(string courseId) =>
        tryGetSummary(courseId, static x => (int)x.Lamp, out var result)
            ? result.Lamp
            : BmsLamp.NoPlay;

    internal ScoreRank? GetRank(string courseId) =>
        tryGetSummary(courseId, static x => (int)(x.Rank ?? ScoreRank.F), out var result)
            ? result.Rank
            : null;

    internal bool TryGet(string courseId, out BmsCourseResult result)
    {
        var history = GetHistory(courseId);
        if (history.Count > 0)
        {
            result = history.MaxBy(resultPriority);
            return true;
        }

        result = default;
        return false;

        static (int Lamp, int Rank, long Score) resultPriority(BmsCourseResult result) =>
            ((int)result.Lamp, (int)(result.Rank ?? ScoreRank.F), result.Score?.TotalScore ?? 0);
    }

    internal IReadOnlyList<BmsCourseResult> GetHistory(string courseId)
    {
        lock (sync)
        {
            if (loadedResults.TryGetValue(courseId, out var history))
                return history.ToArray();

            history = [];
            if (indexByCourseKey.TryGetValue(courseKeyFor(courseId), out var entries))
            {
                foreach (var entry in entries.OrderBy(entry => entry.Path, StringComparer.Ordinal))
                {
                    try
                    {
                        var result = JsonSerializer.Deserialize<BmsCourseResult>(File.ReadAllText(entry.Path));
                        history.Add(result);
                    }
                    catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
                    {
                        BmsLogger.Error(e, $"Failed to read BMS course result {entry.Path}: {e.Message}");
                    }
                }
            }

            loadedResults[courseId] = history;
            return history.ToArray();
        }
    }

    internal async Task<IReadOnlyList<BmsCourseResult>> GetHistoryAsync(string courseId, CancellationToken cancellationToken)
    {
        var attemptedPaths = new HashSet<string>(StringComparer.Ordinal);
        var loadedByPath = new Dictionary<string, BmsCourseResult>(StringComparer.Ordinal);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IndexedCourseResult[] entries;

            lock (sync)
            {
                if (loadedResults.TryGetValue(courseId, out var cached))
                    return cached.ToArray();

                entries = indexByCourseKey.TryGetValue(courseKeyFor(courseId), out var indexed)
                    ? indexed.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray()
                    : [];

                if (entries.All(entry => attemptedPaths.Contains(entry.Path)))
                {
                    var history = entries.Where(entry => loadedByPath.ContainsKey(entry.Path))
                        .Select(entry => loadedByPath[entry.Path])
                        .ToList();
                    loadedResults[courseId] = history;
                    return history.ToArray();
                }
            }

            foreach (var entry in entries.Where(entry => attemptedPaths.Add(entry.Path)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var json = await File.ReadAllTextAsync(entry.Path, cancellationToken).ConfigureAwait(false);
                    var result = JsonSerializer.Deserialize<BmsCourseResult>(json);
                    loadedByPath[entry.Path] = result;
                }
                catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
                {
                    BmsLogger.Error(e, $"Failed to read BMS course result {entry.Path}: {e.Message}");
                }
            }
        }
    }

    internal void Record(string courseId, BmsCourseStatus status, ScoreRank? rank = null, ScoreInfo? score = null, BmsCourseAttemptData? attempt = null)
    {
        if (status == BmsCourseStatus.InProgress
            || (status == BmsCourseStatus.Aborted && score == null && attempt == null))
            return;

        var result = status == BmsCourseStatus.Passed
            ? new BmsCourseResult(lampFor(attempt?.GaugeType), rank is null or ScoreRank.F ? ScoreRank.A : rank, BmsCourseScoreData.From(score), attempt)
            : new BmsCourseResult(BmsLamp.Failed, ScoreRank.F, BmsCourseScoreData.From(score), attempt);

        var file = fileFor(courseId, result);
        writeAtomically(file, result);

        lock (sync)
        {
            var courseKey = courseKeyFor(courseId);
            if (!indexByCourseKey.TryGetValue(courseKey, out var entries))
                indexByCourseKey[courseKey] = entries = [];
            entries.Add(IndexedCourseResult.From(file, result));

            if (loadedResults.TryGetValue(courseId, out var history))
                history.Add(result);
        }

        Changed?.Invoke(courseId);
        return;

        static BmsLamp lampFor(BmsGaugeType? gaugeType) => gaugeType switch
        {
            BmsGaugeType.ExHardClass => BmsLamp.ExHardClear,
            BmsGaugeType.ExClass => BmsLamp.HardClear,
            _ => BmsLamp.Clear,
        };
    }

    private void indexPersistedFiles()
    {
        lock (sync)
        {
            foreach (var file in Directory.EnumerateFiles(StorageDirectory, $"*{file_extension}", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var parts = name.Split('_');
                if (parts.Length < 6)
                    continue;

                var courseKey = parts[0];
                if (courseKey.Length != 64 || !courseKey.All(Uri.IsHexDigit))
                    continue;

                if (!int.TryParse(parts[^3], out var lampValue)
                    || !Enum.IsDefined(typeof(BmsLamp), lampValue)
                    || !tryParseRank(parts[^2], out var rank)
                    || !long.TryParse(parts[^1], out _))
                    continue;

                if (!indexByCourseKey.TryGetValue(courseKey, out var entries))
                    indexByCourseKey[courseKey] = entries = [];
                entries.Add(new IndexedCourseResult(file, (BmsLamp)lampValue, rank));
            }
        }
    }

    private string fileFor(string courseId, BmsCourseResult result)
    {
        lock (sync)
        {
            var timestamp = Math.Max(DateTime.UtcNow.Ticks, lastFileTimestamp + 1);
            lastFileTimestamp = timestamp;
            var courseKey = courseKeyFor(courseId);
            return Path.Combine(StorageDirectory, fileName(courseKey, timestamp, Guid.NewGuid().ToString("N"), result));
        }
    }

    private static string fileName(string courseKey, long timestamp, string id, BmsCourseResult result) =>
        $"{courseKey}_{timestamp:D19}_{id}_{(int)result.Lamp}_{(result.Rank is { } rank ? (int)rank : "n")}_{result.Score?.TotalScore ?? 0}{file_extension}";

    private static string courseKeyFor(string courseId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(courseId)));

    private bool tryGetSummary<TKey>(string courseId, Func<IndexedCourseResult, TKey> priority, out IndexedCourseResult result)
    {
        lock (sync)
        {
            if (indexByCourseKey.TryGetValue(courseKeyFor(courseId), out var entries) && entries.Count > 0)
            {
                result = entries.MaxBy(priority);
                return true;
            }

            result = default;
            return false;
        }
    }

    private static bool tryParseRank(string value, out ScoreRank? rank)
    {
        if (value == "n")
        {
            rank = null;
            return true;
        }

        if (int.TryParse(value, out var parsed) && Enum.IsDefined(typeof(ScoreRank), parsed))
        {
            rank = (ScoreRank)parsed;
            return true;
        }

        rank = null;
        return false;
    }

    private static void writeAtomically(string path, BmsCourseResult result)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(result));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private readonly record struct IndexedCourseResult(string Path, BmsLamp Lamp, ScoreRank? Rank)
    {
        internal static IndexedCourseResult From(string path, BmsCourseResult result) =>
            new(path, result.Lamp, result.Rank);
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
    // System.Text.Json skips non-public accessors unless annotated, which would silently
    // round-trip persisted results with TotalScore = 0.
    [JsonInclude]
    public long TotalScore { get; private init; }

    internal static BmsCourseScoreData? From(ScoreInfo? score) => score == null
        ? null
        : new BmsCourseScoreData
        {
            TotalScore = score.TotalScore,
        };
}
