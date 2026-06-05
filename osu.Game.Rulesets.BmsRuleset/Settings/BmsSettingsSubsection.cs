using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
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
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.ImportExport;
using osu.Game.Rulesets.BmsRuleset.Screens;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Screens;
using osu.Game.Screens.Select;
using osuTK;
using osuTK.Graphics;
using DT = osu.Game.Rulesets.BmsRuleset.DifficultyTable.DifficultyTable;

namespace osu.Game.Rulesets.BmsRuleset.Settings;

public partial class BmsSettingsSubsection(BmsRuleset ruleset) : RulesetSettingsSubsection(ruleset)
{
    protected override LocalisableString Header => "BMS";

    private static readonly ImportOption[] preset_tables =
    [
        new("turbow (zris.work)", "http://zris.work/bmstable/turbow/header.json"),
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
    private IBeatmapUpdater? beatmapUpdater { get; set; }

    [Resolved(CanBeNull = true)]
    private BeatmapManager? beatmapManager { get; set; }

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
            bmsImporter = new BmsFileImporter(realm, storage, notifications, beatmapManager)
            {
                OnImportCompleted = (beatmapSet, scope) =>
                {
                    beatmapUpdater?.Queue(beatmapSet, scope);
                    // Schedule marker refresh on the update thread to avoid nested realm writes.
                    Schedule(() => difficultyNameUpdater?.RefreshAllMarkers());
                },
            };
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

        bindable5K.BindValueChanged(_ => onLayoutSettingChanged());
        bindable7K.BindValueChanged(_ => onLayoutSettingChanged());
        bindable9K.BindValueChanged(_ => onLayoutSettingChanged());
        bindable5KDp.BindValueChanged(_ => onLayoutSettingChanged());
        bindable7KDp.BindValueChanged(_ => onLayoutSettingChanged());
        bindable9KDp.BindValueChanged(_ => onLayoutSettingChanged());

        // Initialize difficulty table services
        if (BmsRuleset.DifficultyTableStore == null && host != null)
        {
            var cacheDir = Path.Combine(host.Storage.GetFullPath(string.Empty), "difficulty-tables");
            collectionSyncManager = new CollectionSyncManager();
            var store = new DifficultyTableStore(manager, cacheDir, collectionSyncManager, realm);
            BmsRuleset.DifficultyTableStore = store;

            difficultyTableStore = store;
            difficultyNameUpdater = new DifficultyNameUpdater(realm!, store);

            // When a table is removed, refresh markers on background thread
            store.TableRemoved += _ =>
            {
                if (difficultyNameUpdater == null) return;

                Task.Run(() => difficultyNameUpdater.RefreshAllMarkers());
            };

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
            new TableListContainer(difficultyTableStore!, collectionSyncManager)
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Padding = new MarginPadding { Horizontal = SettingsPanel.CONTENT_MARGINS, Bottom = 15 },
            },
        ];

        autocomplete = new DifficultyTableAutocomplete
        {
            OnImport = importFromPathUrl,
            OnHistoryDelete = item => deleteFromHistory(item.Url),
            Padding = new MarginPadding { Horizontal = SettingsPanel.CONTENT_MARGINS, Bottom = 15 },
        };
        autocomplete.SetItems(buildPresetItems(), buildHistoryItems());
        Add(autocomplete);
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

    private async void importFromPathUrl(string pathOrUrl)
    {
        ProgressNotification? notification = null;

        try
        {
            if (difficultyTableStore == null || string.IsNullOrWhiteSpace(pathOrUrl)) return;

            var isUrl = pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            var isFile = File.Exists(pathOrUrl);

            if (!isUrl && !isFile) return;

            // Single stateful notification — updated as the import progresses.
            Schedule(() =>
            {
                notification = new ProgressNotification
                {
                    Text = "Importing difficulty table…",
                    Progress = 0,
                    State = ProgressNotificationState.Active,
                };
                notifications?.Post(notification);
            });

            // Dedup: skip if already imported.
            if (difficultyTableStore.Tables.Any(t => t.SourcePath == pathOrUrl))
            {
                Schedule(() =>
                {
                    if (notification == null) return;

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

            var importResult = await difficultyTableStore.ImportAsync(pathOrUrl).ConfigureAwait(false);

            if (importResult != null)
            {
                Schedule(() =>
                {
                    addToHistory(pathOrUrl, importResult.Table.Name, importResult.Table.Symbol);
                    autocomplete?.SetItems(buildPresetItems(), buildHistoryItems());
                    if (notification != null)
                    {
                        notification.CompletionText = $"Loaded table: {importResult.Table.Name} ({importResult.Table.Entries.Count} charts)";
                        notification.Progress = 1;
                        notification.State = ProgressNotificationState.Completed;
                    }
                });
            }
            else
            {
                Schedule(() =>
                {
                    if (notification == null) return;

                    notification.CompletionText = $"Failed to load difficulty table from: {pathOrUrl}";
                    notification.State = ProgressNotificationState.Cancelled;
                });
            }
        }
        catch (Exception e)
        {
            if (notification != null)
            {
                notification.CompletionText = $"Failed to load difficulty table from: {pathOrUrl}. {e.Message}";
                notification.State = ProgressNotificationState.Cancelled;
            }
        }
    }

    private sealed partial class TableListContainer : FillFlowContainer
    {
        private readonly DifficultyTableStore store;
        private readonly CollectionSyncManager? syncManager;

        public TableListContainer(DifficultyTableStore store, CollectionSyncManager? syncManager)
        {
            this.store = store;
            this.syncManager = syncManager;
            Direction = FillDirection.Vertical;
            AutoSizeAxes = Axes.Y;
            RelativeSizeAxes = Axes.X;
        }

        #region Disposal

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            store.TablesChanged -= onTablesChanged;
        }

        #endregion

        protected override void LoadComplete()
        {
            base.LoadComplete();
            store.TablesChanged += onTablesChanged;
            rebuild();
        }

        private void onTablesChanged() => Schedule(rebuild);

        private void rebuild()
        {
            Clear();
            foreach (var table in store.Tables)
            {
                var isSubdivided = syncManager?.IsSubdivided(table) ?? false;
                Add(new TableRowContainer(table, store, syncManager, isSubdivided));
            }
        }

        private partial class TableRowContainer : Container
        {

            public sealed override Axes RelativeSizeAxes
            {
                get => base.RelativeSizeAxes;
                set => base.RelativeSizeAxes = value;
            }

            [Resolved(CanBeNull = true)]
            private IDialogOverlay? dialogOverlay { get; set; }

            public TableRowContainer(DT table, DifficultyTableStore store,
                                     CollectionSyncManager? syncManager, bool isSubdivided)
            {
                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
                Padding = new MarginPadding { Vertical = 3 };

                Children =
                [
                    new FillFlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Direction = FillDirection.Horizontal,
                        AutoSizeAxes = Axes.Both,
                        Spacing = new Vector2(5),
                        Children =
                        [
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = $"{table.Name} ({table.Symbol})",
                                Font = OsuFont.Default.With(size: 16),
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = $"{table.Entries.Count} charts",
                                Font = OsuFont.Default.With(size: 12),
                                Colour = Color4.Gray,
                            },
                        ],
                    },
                    new FillFlowContainer
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Direction = FillDirection.Horizontal,
                        AutoSizeAxes = Axes.Both,
                        Spacing = new Vector2(3),
                        Children =
                        [
                            new RoundedButton
                            {
                                Text = isSubdivided ? "Unsubdivide" : "Subdivide",
                                Height = 25,
                                Width = 100,
                                Action = () => confirmSubdivide(table, syncManager, isSubdivided),
                            },
                            new DangerousRoundedButton
                            {
                                Text = "X",
                                Height = 25,
                                Width = 35,
                                Action = () => confirmDelete(table, store),
                            },
                        ],
                    },
                ];
            }

            private void confirmDelete(DT table, DifficultyTableStore store)
            {
                if (dialogOverlay != null)
                    dialogOverlay.Push(new MassDeleteConfirmationDialog(
                        () => store.RemoveTable(table),
                        $"Delete difficulty table \"{table.Name}\" ({table.Entries.Count} charts)?"));
                else
                    store.RemoveTable(table);
            }

            private void confirmSubdivide(DT table,
                                          CollectionSyncManager? syncManager, bool isSubdivided)
            {
                if (dialogOverlay != null)
                    dialogOverlay.Push(new MassDeleteConfirmationDialog(
                        () => syncManager?.ToggleSubdivide(table),
                        isSubdivided
                            ? $"Merge difficulty table \"{table.Name}\" back into a single collection?"
                            : $"Split difficulty table \"{table.Name}\" into per-level collections?"));
                else
                    syncManager?.ToggleSubdivide(table);
            }
        }
    }
}
