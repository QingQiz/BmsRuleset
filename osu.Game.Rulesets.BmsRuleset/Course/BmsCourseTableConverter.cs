using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using DifficultyTableModel = osu.Game.Rulesets.BmsRuleset.DifficultyTable.DifficultyTable;

namespace osu.Game.Rulesets.BmsRuleset.Course;

internal static class BmsCourseTableConverter
{
    internal static IReadOnlyList<BmsCourseDefinition> Convert(IEnumerable<DifficultyTableModel> tables) =>
        tables.SelectMany(convertTable).ToArray();

    private static IEnumerable<BmsCourseDefinition> convertTable(DifficultyTableModel table)
    {
        var entries = new Dictionary<string, TableEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in table.Entries)
        {
            entries.TryAdd(entry.Md5Hash, entry);

            if (!string.IsNullOrEmpty(entry.Sha256Hash))
                entries.TryAdd(entry.Sha256Hash, entry);
        }

        for (var courseIndex = 0; courseIndex < table.Courses.Count; courseIndex++)
        {
            var course = table.Courses[courseIndex];
            var stages = course.Hashes.Select(hash => createStage(table, entries, hash)).ToArray();

            if (stages.Length == 0)
                continue;

            yield return new BmsCourseDefinition(
                createCourseId(table, course),
                table.Name,
                course.Name,
                stages,
                course.Constraints,
                courseIndex,
                table.Symbol);
        }
    }

    private static string createCourseId(DifficultyTableModel table, TableCourse course)
    {
        var identity = $"{table.SourcePath ?? table.Name}\0{course.Name}\0{string.Join('\0', course.Hashes)}";
        return System.Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private static BmsCourseStage createStage(
        DifficultyTableModel table,
        IReadOnlyDictionary<string, TableEntry> entries,
        string hash)
    {
        entries.TryGetValue(hash, out var entry);

        return new BmsCourseStage(
            entry?.Title ?? hash,
            entry == null ? string.Empty : $"{table.Symbol}{entry.Level}",
            BeatmapHash: hash,
            Artist: entry?.Artist);
    }
}
