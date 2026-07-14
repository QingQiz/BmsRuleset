using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Overlays.Settings;
using osu.Game.Overlays.Settings.Sections.Maintenance;
using osu.Game.Rulesets.BmsRuleset.Audio;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.ImportExport;
using osu.Game.Rulesets.BmsRuleset.Screens;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Screens;
using osu.Game.Screens.Select;
using osuTK;
using DT = osu.Game.Rulesets.BmsRuleset.DifficultyTable.DifficultyTable;

namespace osu.Game.Rulesets.BmsRuleset.Settings;

public partial class BmsSettingsSubsection(BmsRuleset ruleset) : RulesetSettingsSubsection(ruleset)
{
    protected override LocalisableString Header => "BMS";

    private static readonly ImportOption[] preset_tables =
    [
        new("Satellite (sl)", "http://zris.work/bmstable/satellite/header.json"),
        new("Stella (st)", "http://zris.work/bmstable/stella/header.json"),
        new("発狂BMS難易度表 (★)", "http://zris.work/bmstable/insane/insane_header.json"),
        new("通常難易度表 (☆)", "http://zris.work/bmstable/normal/normal_header.json"),
        new("NEW GENERATION 発狂 (▼)", "http://zris.work/bmstable/insane2/insane_header.json"),
        new("第三期Overjoy (★★)", "http://zris.work/bmstable/overjoy/header.json"),
        new("Scramble (SB)", "http://zris.work/bmstable/scramble/header.json"),
        new("Luminous (ln)", "http://zris.work/bmstable/luminous/header.json"),
        new("BMS図書館 (T)", "http://zris.work/bmstable/turbow/header.json"),
    ];

    private const int max_history = 20;

    [Cached]
    private OverlayColourProvider colourProvider = new(OverlayColourScheme.Purple);

    private BmsFileImporter? bmsImporter;
    private DifficultyTableStore? difficultyTableStore;
    private CollectionSyncManager? collectionSyncManager;
    private DifficultyNameUpdater? difficultyNameUpdater;
    private BmsRulesetConfigManager? configManager;
    private DifficultyTableAutocomplete? autocomplete;

    private CancellationTokenSource? importCancellation;

    [Resolved(CanBeNull = true)]
    private RealmAccess? realm { get; set; }

    [Resolved(CanBeNull = true)]
    private Storage? storage { get; set; }

    [Resolved(CanBeNull = true)]
    private INotificationOverlay? notifications { get; set; }

    [Resolved(CanBeNull = true)]
    private IDialogOverlay? dialogOverlay { get; set; }

    [Resolved(CanBeNull = true)]
    private OsuGameBase? game { get; set; }

    [Resolved(CanBeNull = true)]
    private GameHost? host { get; set; }

    [Resolved(CanBeNull = true)]
    private IPerformFromScreenRunner? performer { get; set; }

    [Resolved(CanBeNull = true)]
    private BeatmapManager? beatmapManager { get; set; }

    [Resolved(CanBeNull = true)]
    private MusicController? musicController { get; set; }

    [Resolved(CanBeNull = true)]
    private IBindable<WorkingBeatmap>? workingBeatmap { get; set; }

    [Resolved]
    private OsuColour colours { get; set; } = null!;

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);

        if (bmsImporter != null && game != null)
            game.UnregisterImportHandler(bmsImporter);
    }

    #endregion

    private static string friendlyName(string url)
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(new Uri(url).LocalPath);
            return !string.IsNullOrEmpty(name) ? $"{name} ({url})" : url;
        }
        catch
        {
            return url;
        }
    }

    /// <summary>
    /// Parse a history entry in "name|url|symbol" format (symbol is optional).
    /// Falls back to treating the whole string as a URL for backward compatibility.
    /// </summary>
    private static ImportOption parseHistoryEntry(string entry)
    {
        var parts = entry.Split('|', 3);

        if (parts.Length >= 2 && !string.IsNullOrEmpty(parts[0]) && !string.IsNullOrEmpty(parts[1]))
        {
            var symbol = parts.Length >= 3 ? parts[2] : null;
            var display = !string.IsNullOrEmpty(symbol) ? $"{parts[0]} ({symbol})" : parts[0];
            return new ImportOption(display, parts[1]);
        }

        return new ImportOption(friendlyName(entry), entry);
    }

    private void onLayoutSettingChanged()
    {
        Scheduler.AddOnce(() =>
        {
            if (game == null) return;

            var filterControl = game.ChildrenOfType<FilterControl>().FirstOrDefault();
            var carousel = game.ChildrenOfType<BeatmapCarousel>().FirstOrDefault();

            if (filterControl == null || carousel == null) return;

            var criteria = filterControl.CreateCriteria();
            carousel.Filter(criteria);
        });
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        if (bmsImporter == null && realm != null && storage != null && game != null)
        {
            bmsImporter = new BmsFileImporter(realm, storage, notifications, beatmapManager);
            game.RegisterImportHandler(bmsImporter);
        }

        if (Config is not BmsRulesetConfigManager manager)
            return;

        configManager = manager;

        var bindable5K = manager.GetBindable<bool>(BmsRulesetSetting.ShowBms5K);
        var bindable7K = manager.GetBindable<bool>(BmsRulesetSetting.ShowBme7K);
        var bindable9K = manager.GetBindable<bool>(BmsRulesetSetting.ShowPms9K);
        var bindable5KDp = manager.GetBindable<bool>(BmsRulesetSetting.ShowBms5KDouble);
        var bindable7KDp = manager.GetBindable<bool>(BmsRulesetSetting.ShowBme7KDouble);
        var bindable9KDp = manager.GetBindable<bool>(BmsRulesetSetting.ShowPms9KDouble);
        var useDedicatedPreviewAudio = manager.GetBindable<bool>(BmsRulesetSetting.UseDedicatedPreviewAudio);

        bindable5K.BindValueChanged(_ => onLayoutSettingChanged());
        bindable7K.BindValueChanged(_ => onLayoutSettingChanged());
        bindable9K.BindValueChanged(_ => onLayoutSettingChanged());
        bindable5KDp.BindValueChanged(_ => onLayoutSettingChanged());
        bindable7KDp.BindValueChanged(_ => onLayoutSettingChanged());
        bindable9KDp.BindValueChanged(_ => onLayoutSettingChanged());
        useDedicatedPreviewAudio.BindValueChanged(_ => reloadCurrentPreview());

        // Initialize difficulty table services
        if (BmsRuleset.DifficultyTableStore == null && host != null)
        {
            var cacheDir = Path.Combine(host.Storage.GetFullPath(string.Empty), "difficulty-tables");
            collectionSyncManager = new CollectionSyncManager();
            var store = new DifficultyTableStore(manager, cacheDir, collectionSyncManager, realm);
            BmsRuleset.DifficultyTableStore = store;

            difficultyTableStore = store;
            difficultyNameUpdater = new DifficultyNameUpdater(realm!, store);

            // Wire the updater into the store so AddTable / RemoveTable automatically
            // trigger RefreshAllMarkers.
            store.DifficultyNameUpdater = difficultyNameUpdater;

            store.LoadPersistedTables();
        }
        else
        {
            difficultyTableStore = BmsRuleset.DifficultyTableStore;
        }

        Children =
        [
            new SettingsItemV2(new FormSliderBar<double>
            {
                Caption = RulesetSettingsStrings.ScrollSpeed,
                Current = manager.GetBindable<double>(BmsRulesetSetting.ScrollSpeed),
                KeyboardStep = 0.1f,
                LabelFormat = v => RulesetSettingsStrings.ScrollSpeedTooltip((int)BmsDrawableRuleset.ComputeScrollTime(v), v),
            }),
            new SettingsItemV2(new FormEnumDropdown<BmsReferenceBpmMode>
            {
                Caption = "Reference BPM",
                Current = manager.GetBindable<BmsReferenceBpmMode>(BmsRulesetSetting.ReferenceBpmMode),
            }),
            new SettingsItemV2(new FormSliderBar<double>
            {
                Caption = "BGA dim",
                Current = manager.GetBindable<double>(BmsRulesetSetting.BgaDim),
                DisplayAsPercentage = true,
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = "Use dedicated preview audio",
                HintText = "Use #PREVIEW or preview.* files when available. Disable this to synthesize song-select previews only from BGM and keysound samples.",
                Current = useDedicatedPreviewAudio,
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = "Show BMS 5K",
                Current = bindable5K,
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = "Show BME 7K",
                Current = bindable7K,
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = "Show PMS 9K",
                Current = bindable9K,
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = "Show BMS 5K DP",
                Current = bindable5KDp,
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = "Show BME 7K DP",
                Current = bindable7KDp,
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = "Show PMS 9K DP",
                Current = bindable9KDp,
            }),
            new RoundedButton
            {
                Text = "Import BMS files",
                RelativeSizeAxes = Axes.X,
                Height = 36,
                Action = () => { performer?.PerformFromScreen(menu => menu.Push(new BmsFileImportScreen(manager))); },
                Padding = new MarginPadding { Horizontal = SettingsPanel.CONTENT_MARGINS },
            },
            new RoundedButton
            {
                Text = "Clean up orphaned BMS sets",
                TooltipText = "Removes BMS beatmaps whose source files (audio, images) can no longer be found on disk",
                BackgroundColour = colours.YellowDarker,
                RelativeSizeAxes = Axes.X,
                Height = 36,
                Action = confirmCleanupOrphans,
                Padding = new MarginPadding { Horizontal = SettingsPanel.CONTENT_MARGINS },
            },
            new DangerousRoundedButton
            {
                Text = "Delete all imported BMS files",
                RelativeSizeAxes = Axes.X,
                Height = 36,
                Action = confirmDeleteAllBmsFiles,
                Padding = new MarginPadding { Horizontal = SettingsPanel.CONTENT_MARGINS },
            },
            // ── Difficulty Table Section ──
            new OsuSpriteText
            {
                Text = "Difficulty Tables",
                Font = OsuFont.Default.With(size: 16, weight: FontWeight.Bold),
                Padding = new MarginPadding { Horizontal = SettingsPanel.CONTENT_MARGINS, Top = 15 },
            },
            new TableListContainer(difficultyTableStore!, collectionSyncManager, deleteDiffTable, updateDiffTable)
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Padding = new MarginPadding { Horizontal = SettingsPanel.CONTENT_MARGINS, Bottom = 15 },
            },
            new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Padding = new MarginPadding { Horizontal = SettingsPanel.CONTENT_MARGINS },
                Child = new SettingsNote
                {
                    RelativeSizeAxes = Axes.X,
                    Current =
                    {
                        Value = new SettingsNote.Data(
                            "Adding or removing difficulty tables while on the song select screen may freeze the UI. Switch to the main menu first.",
                            SettingsNote.Type.Warning)
                    },
                },
            },
        ];

        autocomplete = new DifficultyTableAutocomplete
        {
            OnImport = importDiffTableFromPathUrl,
            OnHistoryDelete = item => deleteFromHistory(item.Url),
            Padding = new MarginPadding { Horizontal = SettingsPanel.CONTENT_MARGINS, Bottom = 15 },
        };
        autocomplete.SetItems(buildPresetItems(), buildHistoryItems());
        Add(autocomplete);
    }

    private void reloadCurrentPreview()
    {
        Scheduler.AddOnce(() =>
        {
            if (musicController == null
                || workingBeatmap?.Value is not BmsWorkingBeatmap
                || BmsWorkingBeatmap.ActivePreviewTrack?.PlaybackMode != BmsPreviewTrackPlaybackMode.Preview)
                return;

            var wasPlaying = musicController.IsPlaying;
            musicController.ReloadCurrentTrack();

            if (wasPlaying)
                musicController.Play();
        });
    }

    /// <summary>
    /// Whether the given URL matches a built-in preset table.
    /// </summary>
    private bool isPreset(string url) => preset_tables.Any(p => p.Url == url);

    /// <summary>
    /// Add or update history for a successfully imported table.
    /// Skips presets entirely.
    /// </summary>
    private void addToHistory(string url, string name, string symbol)
    {
        if (isPreset(url)) return;

        var bindable = configManager?.GetBindable<string>(BmsRulesetSetting.DifficultyTableHistory);
        if (bindable == null) return;

        var entries = bindable.Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        // Remove any existing entry with the same URL.
        var existingIndex = entries.FindIndex(e =>
        {
            var parts = e.Split('|', 3);
            return parts.Length > 1 && parts[1] == url;
        });

        if (existingIndex >= 0)
            entries.RemoveAt(existingIndex);

        // Insert at front with the (possibly updated) name from the table header and its symbol.
        entries.Insert(0, $"{name}|{url}|{symbol}");

        if (entries.Count > max_history)
            entries = entries.Take(max_history).ToList();

        bindable.Value = string.Join(";", entries);
    }

    private void deleteFromHistory(string url)
    {
        var bindable = configManager?.GetBindable<string>(BmsRulesetSetting.DifficultyTableHistory);
        if (bindable == null) return;

        var entries = bindable.Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(e =>
            {
                var parts = e.Split('|', 3);
                var entryUrl = parts.Length > 1 ? parts[1] : parts[0];
                return entryUrl != url;
            })
            .ToList();

        bindable.Value = string.Join(";", entries);
        autocomplete?.SuppressAutoHide();
        autocomplete?.SetItems(buildPresetItems(), buildHistoryItems());
        Schedule(() => autocomplete?.RefreshFilter());
    }

    private List<ImportOption> buildPresetItems() => preset_tables.ToList();

    private List<ImportOption> buildHistoryItems()
    {
        var historyStr = configManager?.Get<string>(BmsRulesetSetting.DifficultyTableHistory) ?? string.Empty;
        return historyStr
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(parseHistoryEntry)
            .ToList();
    }

    private void confirmDeleteAllBmsFiles()
    {
        if (bmsImporter == null)
            return;

        // Mirror osu!'s maintenance mass-delete flow: require an explicit (hold-to-)confirm before
        // irreversibly removing every imported BMS beatmap.
        var dialog = new MassDeleteConfirmationDialog(
            () => bmsImporter.DeleteAllBmsFilesAsync(),
            "All imported BMS beatmaps will be permanently deleted. This cannot be undone!");

        if (dialogOverlay != null)
            dialogOverlay.Push(dialog);
        else
            // No dialog overlay available (e.g. isolated test harness): fall back to direct deletion.
            bmsImporter.DeleteAllBmsFilesAsync();
    }

    private void confirmCleanupOrphans()
    {
        if (bmsImporter == null)
            return;

        var dialog = new MassDeleteConfirmationDialog(
            () => Task.Run(() => bmsImporter.CleanupOrphanedSets()),
            "BMS beatmaps whose source directory no longer exists will be removed. This cannot be undone.\n\nAfter deletion, you will need to re-import them. Cancel now and move the files back to their original location if you want to keep them.");

        if (dialogOverlay != null)
            dialogOverlay.Push(dialog);
        else
            bmsImporter.CleanupOrphanedSets();
    }

    private async void importDiffTableFromPathUrl(string pathOrUrl)
    {
        ProgressNotification notification;

        try
        {
            if (difficultyTableStore == null || string.IsNullOrWhiteSpace(pathOrUrl)) return;

            var isUrl = pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            var isFile = File.Exists(pathOrUrl);

            if (!isUrl && !isFile) return;

            notification = new ProgressNotification
            {
                Text = "Importing difficulty table…",
                Progress = 0,
                State = ProgressNotificationState.Active,
            };
            notifications?.Post(notification);

            // Dedup: skip if already imported.
            if (difficultyTableStore.Tables.Any(t => t.SourcePath == pathOrUrl))
            {
                Schedule(() =>
                {
                    notification.CompletionText = $"Difficulty table already imported: {pathOrUrl}";
                    notification.State = ProgressNotificationState.Completed;
                });
                return;
            }

            // Debounce: cancel any previous pending import.
            // ReSharper disable once MethodHasAsyncOverload
            importCancellation?.Cancel();
            importCancellation = new CancellationTokenSource();
            var ct = importCancellation.Token;

            try
            {
                await Task.Delay(300, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var importResult = await difficultyTableStore.ImportAsync(pathOrUrl, notification).ConfigureAwait(false);

            if (importResult != null)
            {
                Schedule(() =>
                {
                    addToHistory(pathOrUrl, importResult.Table.Name, importResult.Table.Symbol);
                    autocomplete?.SetItems(buildPresetItems(), buildHistoryItems());
                    notification.CompletionText = $"Loaded table: {importResult.Table.Name} ({importResult.Table.Entries.Count} charts)";
                    notification.Progress = 1;
                    notification.State = ProgressNotificationState.Completed;
                });
            }
            else
            {
                Schedule(() =>
                {
                    notification.CompletionText = $"Failed to load difficulty table from: {pathOrUrl}";
                    notification.State = ProgressNotificationState.Cancelled;
                });
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, $"Failed to import difficulty table from: {pathOrUrl}");
        }
    }

    /// <summary>
    /// Remove a table with a progress notification. Runs the index removal and
    /// marker refresh on a background thread so the UI stays responsive.
    /// </summary>
    private async void deleteDiffTable(DT table)
    {
        try
        {
            ProgressNotification? notification = null;

            Schedule(() =>
            {
                notification = new ProgressNotification
                {
                    Text = $"Removing difficulty table \"{table.Name}\"...",
                    Progress = 0,
                    State = ProgressNotificationState.Active,
                };
                notifications?.Post(notification);
            });

            // Offload to thread pool — RemoveTable now calls RefreshAllMarkers which
            // runs Realm queries that would block the UI.
            await Task.Run(() => difficultyTableStore?.RemoveTable(table, notification)).ConfigureAwait(false);

            Schedule(() =>
            {
                if (notification == null) return;

                notification.CompletionText = $"Removed difficulty table \"{table.Name}\"";
                notification.Progress = 1;
                notification.State = ProgressNotificationState.Completed;
            });
        }
        catch (Exception e)
        {
            Logger.Error(e, $"Failed to delete difficulty table: {table.SourcePath}");
        }
    }

    /// <summary>
    /// Re-fetch a difficulty table from its remote URL and replace the in-memory data.
    /// </summary>
    private async void updateDiffTable(DT table)
    {
        try
        {
            if (difficultyTableStore == null || table.SourcePath == null) return;
            if (table.Source != TableSource.RemoteUrl) return;

            var notification = new ProgressNotification
            {
                Text = $"Updating difficulty table \"{table.Name}\"...",
                Progress = 0,
                State = ProgressNotificationState.Active,
            };
            Schedule(() => notifications?.Post(notification));

            var importResult = await difficultyTableStore.ImportAsync(table.SourcePath, notification).ConfigureAwait(false);

            if (importResult != null)
            {
                difficultyTableStore.ReplaceTable(table, importResult.Table);
                Schedule(() =>
                {
                    notification.CompletionText = $"Updated table: {importResult.Table.Name} ({importResult.Table.Entries.Count} charts)";
                    notification.Progress = 1;
                    notification.State = ProgressNotificationState.Completed;
                });
            }
            else
            {
                Schedule(() =>
                {
                    notification.CompletionText = $"Failed to update difficulty table: {table.SourcePath}";
                    notification.State = ProgressNotificationState.Cancelled;
                });
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, $"Failed to update difficulty table: {table.SourcePath}");
        }
    }

    private sealed partial class TableListContainer : FillFlowContainer
    {
        private readonly DifficultyTableStore store;
        private readonly CollectionSyncManager? syncManager;
        private readonly Action<DT>? onDelete;
        private readonly Action<DT>? onUpdate;

        public TableListContainer(DifficultyTableStore store, CollectionSyncManager? syncManager,
                                  Action<DT>? onDelete = null,
                                  Action<DT>? onUpdate = null)
        {
            this.store = store;
            this.syncManager = syncManager;
            this.onDelete = onDelete;
            this.onUpdate = onUpdate;
            Direction = FillDirection.Vertical;
            AutoSizeAxes = Axes.Y;
            RelativeSizeAxes = Axes.X;
        }

        #region Disposal

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            store.TableListRebuildEvent -= onTableListRebuildEvent;
        }

        #endregion

        protected override void LoadComplete()
        {
            base.LoadComplete();
            store.TableListRebuildEvent += onTableListRebuildEvent;
            rebuild();
        }

        private void onTableListRebuildEvent(DT? _) => Schedule(rebuild);

        private void rebuild()
        {
            Clear();

            foreach (var table in store.Tables)
            {
                var isSubdivided = syncManager?.IsSubdivided(table) ?? false;
                Add(new TableRowContainer(table, syncManager, isSubdivided, onDelete, onUpdate));
            }
        }

        private partial class TableRowContainer : Container
        {

            public sealed override Axes RelativeSizeAxes
            {
                get => base.RelativeSizeAxes;
                set => base.RelativeSizeAxes = value;
            }

            private readonly Action<DT>? onDelete;
            private readonly Action<DT>? onUpdate;

            [Resolved(CanBeNull = true)]
            private IDialogOverlay? dialogOverlay { get; set; }

            [Resolved(CanBeNull = true)]
            private RealmAccess? realm { get; set; }

            public TableRowContainer(DT table,
                                     CollectionSyncManager? syncManager, bool isSubdivided,
                                     Action<DT>? onDelete = null,
                                     Action<DT>? onUpdate = null)
            {
                this.onDelete = onDelete;
                this.onUpdate = onUpdate;
                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
                Padding = new MarginPadding { Vertical = 3 };

                var buttons = createButtons(
                    table, isSubdivided,
                    () => confirmSubdivide(table, syncManager, isSubdivided),
                    () => confirmUpdate(table),
                    () => confirmDelete(table));

                Children =
                [
                    new ResponsiveTableRowLayout(
                        $"{table.Name} ({table.Symbol})",
                        buttons,
                        table)
                    {
                        RelativeSizeAxes = Axes.X,
                    },
                ];
            }

            private static List<Drawable> createButtons(
                DT table, bool isSubdivided,
                Action confirmSubdivide, Action confirmUpdate, Action confirmDelete)
            {
                var buttons = new List<Drawable>
                {
                    new RoundedButton
                    {
                        Text = isSubdivided ? "Unsubdivide" : "Subdivide",
                        TooltipText = isSubdivided
                            ? "Merge per-level collections back into one"
                            : "Split into per-level collections",
                        Height = 25,
                        Width = 100,
                        Action = confirmSubdivide,
                    },
                };

                if (table.Source == TableSource.RemoteUrl)
                {
                    buttons.Add(new RoundedButton
                    {
                        Text = "Upd",
                        TooltipText = "Re-fetch table from source",
                        Height = 25,
                        Width = 40,
                        Action = confirmUpdate,
                    });
                }

                buttons.Add(new DangerousRoundedButton
                {
                    Text = "X",
                    TooltipText = "Delete table",
                    Height = 25,
                    Width = 35,
                    Action = confirmDelete,
                });

                return buttons;
            }

            private void confirmDelete(DT table)
            {
                if (dialogOverlay != null)
                    dialogOverlay.Push(new MassDeleteConfirmationDialog(
                        () => Task.Run(() => onDelete?.Invoke(table)),
                        $"Delete difficulty table \"{table.Name}\" ({table.Entries.Count} charts)?\n\n⚠ This may freeze the UI if done from song select. Switch to the main menu first."));
                else
                    onDelete?.Invoke(table);
            }

            private void confirmUpdate(DT table)
            {
                if (dialogOverlay != null)
                    dialogOverlay.Push(new MassDeleteConfirmationDialog(
                        () => onUpdate?.Invoke(table),
                        $"Re-fetch difficulty table \"{table.Name}\" from source?\n\n⚠ This may freeze the UI if done from song select. Switch to the main menu first."));
                else
                    onUpdate?.Invoke(table);
            }

            private void confirmSubdivide(DT table,
                                          CollectionSyncManager? syncManager, bool isSubdivided)
            {
                if (dialogOverlay != null)
                    dialogOverlay.Push(new MassDeleteConfirmationDialog(
                        () => syncManager?.ToggleSubdivide(realm, table),
                        isSubdivided
                            ? $"Merge difficulty table \"{table.Name}\" back into a single collection?"
                            : $"Split difficulty table \"{table.Name}\" into per-level collections?"));
                else
                    syncManager?.ToggleSubdivide(realm, table);
            }

            /// <summary>
            /// Lays out wrapped text and a trailing button group with manual
            /// line breaking. Estimates word widths from character counts and
            /// builds lines that fit within the available width.
            /// </summary>
            private partial class ResponsiveTableRowLayout : Container, IHasTooltip
            {

                public LocalisableString TooltipText { get; }

                private const float line_height = 22f;
                private const float button_spacing = 6f;
                private const float vertical_spacing = 4f;

                private readonly string[] words;
                private readonly List<Drawable> buttons;

                private readonly FillFlowContainer lineContainer;
                private readonly FillFlowContainer buttonGroup;

                private float lastAvailableWidth;

                public ResponsiveTableRowLayout(string nameText,
                                                List<Drawable> buttons,
                                                DT table)
                {
                    this.buttons = buttons;
                    words = nameText.Split(' ');
                    TooltipText = buildTooltip(table);

                    lineContainer = new FillFlowContainer
                    {
                        Direction = FillDirection.Vertical,
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                    };

                    buttonGroup = new FillFlowContainer
                    {
                        Direction = FillDirection.Horizontal,
                        AutoSizeAxes = Axes.Both,
                        Spacing = new Vector2(3),
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        Children = buttons,
                    };

                    Children = [lineContainer, buttonGroup];
                }

                protected override void Update()
                {
                    base.Update();

                    var availableWidth = DrawWidth;
                    if (availableWidth <= 0 || availableWidth == lastAvailableWidth)
                        return;

                    lastAvailableWidth = availableWidth;
                    rebuildLines(availableWidth);
                }

                /// <summary>
                /// Character-break a long unbroken word into chunks that each
                /// fit within <paramref name="maxWidth"/>.
                /// </summary>
                private static List<string> breakLongWord(string word, float maxWidth)
                {
                    var chunks = new List<string>();
                    var current = "";
                    float currentW = 0;

                    foreach (var c in word)
                    {
                        var cw = estimateCharWidth(c, 16);

                        if (current.Length > 0 && currentW + cw > maxWidth)
                        {
                            chunks.Add(current);
                            current = "";
                            currentW = 0;
                        }

                        current += c;
                        currentW += cw;
                    }

                    if (current.Length > 0)
                        chunks.Add(current);

                    return chunks;
                }

                /// <summary>Estimate pixel width of a string at a given font size.</summary>
                private static float estimateWidth(string text, float fontSize)
                {
                    float w = 0;
                    foreach (var c in text)
                        w += estimateCharWidth(c, fontSize);
                    return w;
                }

                /// <summary>Estimate pixel width of a single character.</summary>
                private static float estimateCharWidth(char c, float fontSize)
                {
                    if (c >= 0x2E80) return fontSize;      // CJK — roughly square
                    if (c == ' ') return fontSize * 0.28f; // space
                    if (c is 'i' or 'l' or 'I' or '1') return fontSize * 0.3f;
                    if (c is 'M' or 'W') return fontSize * 0.6f;

                    return fontSize * 0.48f; // average Latin
                }

                /// <summary>Build a tooltip showing chart counts per level.</summary>
                private static string buildTooltip(DT table)
                {
                    // Count entries per level.
                    var counts = new Dictionary<string, int>();

                    foreach (var e in table.Entries)
                    {
                        counts.TryGetValue(e.Level, out var c);
                        counts[e.Level] = c + 1;
                    }

                    // Order by LevelOrder, then any remaining levels.
                    var ordered = new List<string>();

                    foreach (var lv in table.LevelOrder)
                    {
                        if (counts.TryGetValue(lv, out var c))
                        {
                            ordered.Add($"{lv}: {c}");
                            counts.Remove(lv);
                        }
                    }

                    foreach (var kv in counts.OrderBy(kv => kv.Key))
                        ordered.Add($"{kv.Key}: {kv.Value}");

                    return $"{table.Entries.Count} charts\n{string.Join(", ", ordered)}";
                }

                private void rebuildLines(float availableWidth)
                {
                    lineContainer.Clear();

                    // Build lines from name words at 16px.
                    var lines = new List<List<string>>();
                    var currentLine = new List<string>();
                    float currentWidth = 0;

                    foreach (var word in words)
                    {
                        var wordWidth = estimateWidth(word, 16);
                        var space = currentLine.Count > 0 ? estimateCharWidth(' ', 16) : 0;

                        if (currentWidth + space + wordWidth > availableWidth)
                        {
                            // If the word alone exceeds the line width, character-break it.
                            if (currentLine.Count == 0)
                            {
                                foreach (var chunk in breakLongWord(word, availableWidth))
                                {
                                    lines.Add([chunk]);
                                }

                                currentLine = [];
                                currentWidth = 0;
                                continue;
                            }

                            // Otherwise wrap to a new line first.
                            lines.Add(currentLine);
                            currentLine = [];
                            currentWidth = 0;
                            space = 0;

                            // If it still doesn't fit alone, character-break it.
                            if (wordWidth > availableWidth)
                            {
                                foreach (var chunk in breakLongWord(word, availableWidth))
                                    lines.Add([chunk]);
                                continue;
                            }
                        }

                        currentLine.Add(word);
                        currentWidth += space + wordWidth;
                    }

                    if (currentLine.Count > 0)
                        lines.Add(currentLine);

                    // Build text sprites for each line (16px bold name words).
                    for (var li = 0; li < lines.Count; li++)
                    {
                        var lineFlow = new TextFlowContainer { AutoSizeAxes = Axes.Both };

                        for (var wi = 0; wi < lines[li].Count; wi++)
                        {
                            lineFlow.AddText((wi > 0 ? " " : "") + lines[li][wi], t =>
                                t.Font = OsuFont.Default.With(size: 16, weight: FontWeight.Bold));
                        }

                        lineContainer.Add(lineFlow);
                    }

                    // Actual button width (or estimate on first pass).
                    var btnW = buttonGroup.DrawWidth;
                    if (btnW <= 0) btnW = buttons.Count switch { 3 => 190f, _ => 145f };

                    // Estimate last-line width for button fit.
                    var lastLineWidth = estimateLineWidth(lines[^1]);
                    var textHeight = lines.Count * line_height;

                    if (lastLineWidth + button_spacing + btnW <= availableWidth)
                    {
                        buttonGroup.Y = textHeight - line_height;
                        Height = textHeight;
                    }
                    else
                    {
                        buttonGroup.Y = textHeight + vertical_spacing;
                        Height = textHeight + line_height + vertical_spacing;
                    }
                }

                private float estimateLineWidth(List<string> lineWords)
                {
                    float w = 0;
                    for (var i = 0; i < lineWords.Count; i++)
                        w += estimateWidth((i > 0 ? " " : "") + lineWords[i], 16);
                    return w;
                }
            }
        }
    }
}
