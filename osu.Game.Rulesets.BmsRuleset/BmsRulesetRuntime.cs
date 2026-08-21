using System;
using System.IO;
using osu.Framework.Platform;
using osu.Game.Database;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;

namespace osu.Game.Rulesets.BmsRuleset;

internal static class BmsRulesetRuntime
{
    internal static event Action? CourseResultsChanged;

    internal static BmsRulesetConfigManager? ConfigManager
    {
        get;
        set
        {
            field = value;
            if (value == null)
                CourseResults = null;
        }
    }

    internal static BmsCourseResultStore? CourseResults
    {
        get;
        private set
        {
            if (ReferenceEquals(field, value))
                return;

            field = value;
            CourseResultsChanged?.Invoke();
        }
    }

    internal static DifficultyTableStore? DifficultyTableStore
    {
        get => difficultyTableStore;
        set
        {
            if (ReferenceEquals(difficultyTableStore, value))
                return;

            if (difficultyTableStore != null)
                difficultyTableStore.TablesChanged -= syncCourses;

            difficultyTableStore = value;

            if (difficultyTableStore != null)
                difficultyTableStore.TablesChanged += syncCourses;

            syncCourses();
        }
    }

    private static DifficultyTableStore? difficultyTableStore;

    internal static BmsCourseCatalog CourseCatalog { get; } = new();

    internal static BmsVisualOffsetSuggestionStore VisualOffsetSuggestions { get; } = new();

    internal static BmsReferenceBpmMode CurrentReferenceBpmMode =>
        ConfigManager?.Get<BmsReferenceBpmMode>(BmsRulesetSetting.ReferenceBpmMode) ?? BmsReferenceBpmMode.MainBpm;

    internal static bool UseDedicatedPreviewAudio =>
        ConfigManager?.Get<bool>(BmsRulesetSetting.UseDedicatedPreviewAudio) ?? true;

    internal static void EnsureDifficultyTableStore(GameHost host, RealmAccess realm)
    {
        if (ConfigManager != null)
        {
            var courseResultsDirectory = Path.Combine(host.Storage.GetFullPath(string.Empty), "bms-course-results");
            if (CourseResults == null || !string.Equals(CourseResults.StorageDirectory, courseResultsDirectory, StringComparison.OrdinalIgnoreCase))
                CourseResults = new BmsCourseResultStore(courseResultsDirectory);
        }

        if (DifficultyTableStore != null || ConfigManager == null || CourseCatalog.Courses.Count > 0)
            return;

        var cacheDirectory = Path.Combine(host.Storage.GetFullPath(string.Empty), "difficulty-tables");
        var collectionSyncManager = new CollectionSyncManager(ConfigManager);
        var store = new DifficultyTableStore(ConfigManager, cacheDirectory, collectionSyncManager, realm);
        store.DifficultyNameUpdater = new DifficultyNameUpdater(realm, store);
        DifficultyTableStore = store;
        store.LoadPersistedTables();
    }

    private static void syncCourses() => CourseCatalog.Replace(
        BmsCourseTableConverter.Convert(difficultyTableStore?.Tables ?? []));
}
