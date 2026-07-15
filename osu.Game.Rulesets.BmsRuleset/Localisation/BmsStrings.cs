using System;
using System.Globalization;
using System.Resources;
using osu.Framework.Localisation;


namespace osu.Game.Rulesets.BmsRuleset.Localisation;

public static class BmsStrings
{
    private const string resource_prefix = "osu.Game.Rulesets.BmsRuleset.Resources.Localisation.BmsStrings";

    private static readonly ResourceManager english_resources = new(resource_prefix, typeof(BmsStrings).Assembly);
    private static readonly ResourceManager chinese_resources = new($"{resource_prefix}.zh", typeof(BmsStrings).Assembly);

    public static LocalisableString Layout => get("layout");

    public static LocalisableString GaugeHistory => get("gauge_history");

    public static LocalisableString Timeline => get("timeline");

    public static LocalisableString HitScatter => get("hit_scatter");

    public static LocalisableString HitOffset => get("hit_offset");

    public static LocalisableString StandardDeviation(double value) => get("standard_deviation", value);

    public static LocalisableString ReferenceBpm => get("reference_bpm");

    public static LocalisableString BgaDim => get("bga_dim");

    public static LocalisableString UseDedicatedPreviewAudio => get("use_dedicated_preview_audio");

    public static LocalisableString DedicatedPreviewAudioHint => get("dedicated_preview_audio_hint");

    public static LocalisableString ShowBms5K => get("show_bms_5k");

    public static LocalisableString ShowBme7K => get("show_bme_7k");

    public static LocalisableString ShowPms9K => get("show_pms_9k");

    public static LocalisableString ShowBms5KDp => get("show_bms_5k_dp");

    public static LocalisableString ShowBme7KDp => get("show_bme_7k_dp");

    public static LocalisableString ShowPms9KDp => get("show_pms_9k_dp");

    public static LocalisableString ImportBmsFiles => get("import_bms_files");

    public static LocalisableString CleanupOrphanedSets => get("cleanup_orphaned_sets");

    public static LocalisableString CleanupOrphanedSetsTooltip => get("cleanup_orphaned_sets_tooltip");

    public static LocalisableString DeleteAllImportedFiles => get("delete_all_imported_files");

    public static LocalisableString DifficultyTables => get("difficulty_tables");

    public static LocalisableString DifficultyTableWarning => get("difficulty_table_warning");

    public static LocalisableString DeleteAllConfirmation => get("delete_all_confirmation");

    public static LocalisableString CleanupOrphansConfirmation => get("cleanup_orphans_confirmation");

    public static LocalisableString ImportSelectedFile => get("import_selected_file");

    public static LocalisableString ImportCurrentFolder => get("import_current_folder");

    public static LocalisableString ImportDirectoryRecursive => get("import_directory_recursive");

    public static LocalisableString ImportDirectoryRecursiveTooltip => get("import_directory_recursive_tooltip");

    public static LocalisableString SelectFileOrFolder => get("select_file_or_folder");

    public static LocalisableString DifficultyTablePlaceholder => get("difficulty_table_placeholder");

    public static LocalisableString Import => get("import");

    public static LocalisableString Presets => get("presets");

    public static LocalisableString History => get("history");

    public static LocalisableString Subdivide => get("subdivide");

    public static LocalisableString Unsubdivide => get("unsubdivide");

    public static LocalisableString SubdivideTooltip => get("subdivide_tooltip");

    public static LocalisableString UnsubdivideTooltip => get("unsubdivide_tooltip");

    public static LocalisableString Update => get("update");

    public static LocalisableString UpdateTableTooltip => get("update_table_tooltip");

    public static LocalisableString DeleteTableTooltip => get("delete_table_tooltip");

    public static LocalisableString DifficultyTableName(string name, string symbol) => get("difficulty_table_name", name, symbol);

    public static LocalisableString DifficultyTableTooltip(int count, string levels) => get("difficulty_table_tooltip", count, levels);

    public static LocalisableString ImportingDifficultyTable => get("importing_difficulty_table");

    public static LocalisableString EditorUnsupported => get("editor_unsupported");

    public static LocalisableString Collecting => get("collecting");

    public static LocalisableString Refreshing => get("refreshing");

    public static LocalisableString Start => get("start");

    public static LocalisableString Time => get("time");

    public static LocalisableString Overall => get("overall");

    public static LocalisableString Scratch => get("scratch");

    public static LocalisableString Fast => get("fast");

    public static LocalisableString Late => get("late");

    public static LocalisableString Key(int number) => get("key", number);

    public static LocalisableString Notes => get("notes");

    public static LocalisableString Judgement => get("judgement");

    public static LocalisableString FastLate => get("fast_late");

    public static LocalisableString Note => get("note");

    public static LocalisableString LongNote => get("long_note");

    public static LocalisableString Mine => get("mine");

    public static LocalisableString Poor => get("poor");

    public static LocalisableString Bad => get("bad");

    public static LocalisableString Good => get("good");

    public static LocalisableString Great => get("great");

    public static LocalisableString Perfect => get("perfect");

    public static LocalisableString DifficultyTableAlreadyImported(string source) => get("difficulty_table_already_imported", source);

    public static LocalisableString LoadedTable(string name, int count) => get("loaded_table", name, count);

    public static LocalisableString FailedToLoadTable(string source) => get("failed_to_load_table", source);

    public static LocalisableString RemovingTable(string name) => get("removing_table", name);

    public static LocalisableString RemovedTable(string name) => get("removed_table", name);

    public static LocalisableString UpdatingTable(string name) => get("updating_table", name);

    public static LocalisableString UpdatedTable(string name, int count) => get("updated_table", name, count);

    public static LocalisableString FailedToUpdateTable(string source) => get("failed_to_update_table", source);

    public static LocalisableString DeleteTableConfirmation(string name, int count) => get("delete_table_confirmation", name, count);

    public static LocalisableString UpdateTableConfirmation(string name) => get("update_table_confirmation", name);

    public static LocalisableString MergeTableConfirmation(string name) => get("merge_table_confirmation", name);

    public static LocalisableString SplitTableConfirmation(string name) => get("split_table_confirmation", name);

    public static LocalisableString HitCount(int count) => get("hit_count", count);

    public static LocalisableString ImportInitialising => get("import_initialising");

    public static LocalisableString ScanningOrphans => get("scanning_orphans");

    public static LocalisableString CleanupComplete(int count) => get("cleanup_complete", count);

    public static LocalisableString NoOrphansFound => get("no_orphans_found");

    public static LocalisableString CleanupFailed => get("cleanup_failed");

    public static LocalisableString DeletingBeatmaps => get("deleting_beatmaps");

    public static LocalisableString DeletedSets(int count) => get("deleted_sets", count);

    public static LocalisableString DeleteCancelled(int count) => get("delete_cancelled", count);

    public static LocalisableString DeleteWasCancelled => get("delete_was_cancelled");

    public static LocalisableString DeleteFailed => get("delete_failed");

    public static LocalisableString ImportFailed => get("import_failed");

    public static LocalisableString SetAlreadyImported => get("set_already_imported");

    public static LocalisableString AllSetsAlreadyImported(int count) => get("all_sets_already_imported", count);

    public static LocalisableString ImportedSets(int count) => get("imported_sets", count);

    public static LocalisableString ImportedSetsProgress(int imported, int total) => get("imported_sets_progress", imported, total);

    public static LocalisableString RulesetUnavailable => get("ruleset_unavailable");

    public static LocalisableString ScanningFiles => get("scanning_files");

    public static LocalisableString NoChartsFound => get("no_charts_found");

    public static LocalisableString PreparingImport => get("preparing_import");

    public static LocalisableString ImportWasCancelled => get("import_was_cancelled");

    public static LocalisableString ImportCancelledProgress(int imported, int total) => get("import_cancelled_progress", imported, total);

    public static LocalisableString MissingBmstableMeta(string source) => get("missing_bmstable_meta", source);

    public static LocalisableString FileDoesNotExist => get("file_does_not_exist");

    public static LocalisableString FailedToParseTable(string source) => get("failed_to_parse_table", source);

    public static LocalisableString NoValidTableEntries(string source) => get("no_valid_table_entries", source);

    public static LocalisableString RefreshingProgress(int processed, int total) => get("refreshing_progress", processed, total);

    public static LocalisableString ExRankDescription(double exRank) => get("exrank_description", exRank);

    public static LocalisableString RankDescription(int rank) => get("rank_description", rank);

    public static LocalisableString LockedLongNoteMode(string mode) => get("locked_long_note_mode", mode);

    public static LocalisableString LockedMode => get("locked_mode");

    public static LocalisableString GaugeSummary(string gauge, int objects, string total) => get("gauge_summary", gauge, objects, total);

    public static LocalisableString GaugeSummaryWithGuts(string gauge, int objects, string total) => get("gauge_summary_with_guts", gauge, objects, total);

    public static LocalisableString ActionScratch => get("action_scratch");

    public static LocalisableString ActionKey1 => get("action_key_1");

    public static LocalisableString ActionKey2 => get("action_key_2");

    public static LocalisableString ActionKey3 => get("action_key_3");

    public static LocalisableString ActionKey4 => get("action_key_4");

    public static LocalisableString ActionKey5 => get("action_key_5");

    public static LocalisableString ActionKey6 => get("action_key_6");

    public static LocalisableString ActionKey7 => get("action_key_7");

    public static LocalisableString ActionP2Scratch => get("action_p2_scratch");

    public static LocalisableString ActionP2Key1 => get("action_p2_key_1");

    public static LocalisableString ActionP2Key2 => get("action_p2_key_2");

    public static LocalisableString ActionP2Key3 => get("action_p2_key_3");

    public static LocalisableString ActionP2Key4 => get("action_p2_key_4");

    public static LocalisableString ActionP2Key5 => get("action_p2_key_5");

    public static LocalisableString ActionP2Key6 => get("action_p2_key_6");

    public static LocalisableString ActionP2Key7 => get("action_p2_key_7");

    public static LocalisableString ActionPmsKey1 => get("action_pms_key_1");

    public static LocalisableString ActionPmsKey2 => get("action_pms_key_2");

    public static LocalisableString ActionPmsKey3 => get("action_pms_key_3");

    public static LocalisableString ActionPmsKey4 => get("action_pms_key_4");

    public static LocalisableString ActionPmsKey5 => get("action_pms_key_5");

    public static LocalisableString ActionPmsKey6 => get("action_pms_key_6");

    public static LocalisableString ActionPmsKey7 => get("action_pms_key_7");

    public static LocalisableString ActionPmsKey8 => get("action_pms_key_8");

    public static LocalisableString ActionPmsKey9 => get("action_pms_key_9");

    public static LocalisableString ActionP2PmsKey1 => get("action_p2_pms_key_1");

    public static LocalisableString ActionP2PmsKey2 => get("action_p2_pms_key_2");

    public static LocalisableString ActionP2PmsKey3 => get("action_p2_pms_key_3");

    public static LocalisableString ActionP2PmsKey4 => get("action_p2_pms_key_4");

    public static LocalisableString ActionP2PmsKey5 => get("action_p2_pms_key_5");

    public static LocalisableString ActionP2PmsKey6 => get("action_p2_pms_key_6");

    public static LocalisableString ActionP2PmsKey7 => get("action_p2_pms_key_7");

    public static LocalisableString ActionP2PmsKey8 => get("action_p2_pms_key_8");

    public static LocalisableString ActionP2PmsKey9 => get("action_p2_pms_key_9");

    public static LocalisableString ActionIncreaseScrollSpeed => get("action_increase_scroll_speed");

    public static LocalisableString ActionDecreaseScrollSpeed => get("action_decrease_scroll_speed");

    public static LocalisableString StartBpm => get("start_bpm");

    public static LocalisableString MaxBpm => get("max_bpm");

    public static LocalisableString MainBpm => get("main_bpm");

    public static LocalisableString MinBpm => get("min_bpm");

    public static LocalisableString ModSecondPlayer => get("mod_second_player");

    public static LocalisableString ModAutoScratch => get("mod_auto_scratch");

    public static LocalisableString ModBackgroundKeysound => get("mod_background_keysound");

    public static LocalisableString ModBranchReplay => get("mod_branch_replay");

    public static LocalisableString ModConstant => get("mod_constant");

    public static LocalisableString ModHideScratch => get("mod_hide_scratch");

    public static LocalisableString ModLaneRandom => get("mod_lane_random");

    public static LocalisableString ModMirror => get("mod_mirror");

    public static LocalisableString ModNoteRandom => get("mod_note_random");

    public static LocalisableString ModPaused => get("mod_paused");

    public static LocalisableString ModRotationRandom => get("mod_rotation_random");

    public static LocalisableString ModLongNote => get("mod_long_note");

    public static LocalisableString ModChargeNote => get("mod_charge_note");

    public static LocalisableString ModHellChargeNote => get("mod_hell_charge_note");

    public static LocalisableString ModAutoGauge => get("mod_auto_gauge");

    public static LocalisableString GaugeDescription(string label) => get("gauge_description", label);

    private static LocalisableString get(string key, params object[] args)
    {
        var english = english_resources.GetString(key, CultureInfo.InvariantCulture)
                      ?? throw new InvalidOperationException($"Missing English localisation resource: {key}");
        var chinese = chinese_resources.GetString(key, CultureInfo.InvariantCulture) ?? english;

        return new BmsLocalisableString(key, english, chinese, args);
    }

    private sealed class BmsLocalisableString(string key, string english, string chinese, object[] args)
        : LocalisableFormattableString(english, args)
    {
        private readonly string key = key;

        protected override string FormatString(string format, object?[] formatArgs, LocalisationParameters parameters)
        {
            var culture = parameters.Store?.EffectiveCulture.Name;
            var useChinese = culture != null &&
                             (culture.Equals("zh", StringComparison.OrdinalIgnoreCase)
                              || culture.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
                              || culture.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase));

            return base.FormatString(useChinese ? chinese : format, formatArgs, parameters);
        }

        public override bool Equals(ILocalisableStringData? other) =>
            other is BmsLocalisableString localisable
            && key == localisable.key
            && base.Equals(localisable);

        public override bool Equals(object? obj) => obj is ILocalisableStringData localisable && Equals(localisable);

        public override int GetHashCode() => HashCode.Combine(key, base.GetHashCode());
    }
}
