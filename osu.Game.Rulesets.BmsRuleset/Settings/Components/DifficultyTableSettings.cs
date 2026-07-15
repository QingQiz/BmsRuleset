using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osuTK;
using DT = osu.Game.Rulesets.BmsRuleset.DifficultyTable.DifficultyTable;

namespace osu.Game.Rulesets.BmsRuleset.Settings.Components;

internal partial class DifficultyTableSettings : FillFlowContainer
{
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

    private readonly BmsRulesetConfigManager configManager;

    private DifficultyTableStore? difficultyTableStore;
    private CollectionSyncManager? collectionSyncManager;
    private DifficultyTableAutocomplete? autocomplete;
    private CancellationTokenSource? importCancellation;

    [Resolved(CanBeNull = true)]
    private RealmAccess? realm { get; set; }

    [Resolved(CanBeNull = true)]
    private INotificationOverlay? notifications { get; set; }

    [Resolved(CanBeNull = true)]
    private GameHost? host { get; set; }

    public DifficultyTableSettings(BmsRulesetConfigManager configManager)
    {
        this.configManager = configManager;
        Direction = FillDirection.Vertical;
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Spacing = new Vector2(0, SettingsSection.ITEM_SPACING_V2);
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        initialiseStore();

        autocomplete = new DifficultyTableAutocomplete
        {
            OnImport = importFromPathOrUrl,
            OnHistoryDelete = item => deleteFromHistory(item.Url),
            Padding = SettingsPanel.CONTENT_PADDING,
        };
        autocomplete.SetItems(buildPresetItems(), buildHistoryItems());

        Children =
        [
            new OsuSpriteText
            {
                Text = BmsStrings.DifficultyTables,
                Font = OsuFont.GetFont(size: 18),
                Margin = new MarginPadding { Vertical = SettingsSubsection.VERTICAL_PADDING },
                Padding = SettingsPanel.CONTENT_PADDING,
            },
            new DifficultyTableListContainer(difficultyTableStore!, collectionSyncManager, deleteTable, updateTable)
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Padding = SettingsPanel.CONTENT_PADDING,
            },
            new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Padding = SettingsPanel.CONTENT_PADDING,
                Child = new SettingsNote
                {
                    RelativeSizeAxes = Axes.X,
                    Current =
                    {
                        Value = new SettingsNote.Data(BmsStrings.DifficultyTableWarning, SettingsNote.Type.Warning)
                    },
                },
            },
            autocomplete,
        ];
    }

    protected override void Dispose(bool isDisposing)
    {
        importCancellation?.Cancel();
        importCancellation?.Dispose();
        base.Dispose(isDisposing);
    }

    private void initialiseStore()
    {
        if (BmsRulesetRuntime.DifficultyTableStore == null && host != null)
        {
            var cacheDir = Path.Combine(host.Storage.GetFullPath(string.Empty), "difficulty-tables");
            collectionSyncManager = new CollectionSyncManager();
            var store = new DifficultyTableStore(configManager, cacheDir, collectionSyncManager, realm);
            BmsRulesetRuntime.DifficultyTableStore = store;

            if (realm != null)
                store.DifficultyNameUpdater = new DifficultyNameUpdater(realm, store);

            store.LoadPersistedTables();
        }

        difficultyTableStore = BmsRulesetRuntime.DifficultyTableStore;
    }

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

    private bool isPreset(string url) => preset_tables.Any(p => p.Url == url);

    private void addToHistory(string url, string name, string symbol)
    {
        if (isPreset(url)) return;

        var bindable = configManager.GetBindable<string>(BmsRulesetSetting.DifficultyTableHistory);
        var entries = bindable.Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var existingIndex = entries.FindIndex(e =>
        {
            var parts = e.Split('|', 3);
            return parts.Length > 1 && parts[1] == url;
        });

        if (existingIndex >= 0)
            entries.RemoveAt(existingIndex);

        entries.Insert(0, $"{name}|{url}|{symbol}");

        if (entries.Count > max_history)
            entries = entries.Take(max_history).ToList();

        bindable.Value = string.Join(";", entries);
    }

    private void deleteFromHistory(string url)
    {
        var bindable = configManager.GetBindable<string>(BmsRulesetSetting.DifficultyTableHistory);
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

    private static List<ImportOption> buildPresetItems() => preset_tables.ToList();

    private List<ImportOption> buildHistoryItems()
    {
        var history = configManager.Get<string>(BmsRulesetSetting.DifficultyTableHistory);
        return history
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(parseHistoryEntry)
            .ToList();
    }

    private async void importFromPathOrUrl(string pathOrUrl)
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
                Text = BmsStrings.ImportingDifficultyTable,
                Progress = 0,
                State = ProgressNotificationState.Active,
            };
            notifications?.Post(notification);

            if (difficultyTableStore.Tables.Any(t => t.SourcePath == pathOrUrl))
            {
                Schedule(() =>
                {
                    notification.CompletionText = BmsStrings.DifficultyTableAlreadyImported(pathOrUrl);
                    notification.State = ProgressNotificationState.Completed;
                });
                return;
            }

            if (importCancellation != null) await importCancellation.CancelAsync();
            importCancellation = new CancellationTokenSource();
            var cancellationToken = importCancellation.Token;

            try
            {
                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
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
                    notification.CompletionText = BmsStrings.LoadedTable(importResult.Table.Name, importResult.Table.Entries.Count);
                    notification.Progress = 1;
                    notification.State = ProgressNotificationState.Completed;
                });
            }
            else
            {
                Schedule(() =>
                {
                    notification.CompletionText = BmsStrings.FailedToLoadTable(pathOrUrl);
                    notification.State = ProgressNotificationState.Cancelled;
                });
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, $"Failed to import difficulty table from: {pathOrUrl}");
        }
    }

    private async void deleteTable(DT table)
    {
        try
        {
            ProgressNotification? notification = null;

            Schedule(() =>
            {
                notification = new ProgressNotification
                {
                    Text = BmsStrings.RemovingTable(table.Name),
                    Progress = 0,
                    State = ProgressNotificationState.Active,
                };
                notifications?.Post(notification);
            });

            await Task.Run(() => difficultyTableStore?.RemoveTable(table, notification)).ConfigureAwait(false);

            Schedule(() =>
            {
                if (notification == null) return;

                notification.CompletionText = BmsStrings.RemovedTable(table.Name);
                notification.Progress = 1;
                notification.State = ProgressNotificationState.Completed;
            });
        }
        catch (Exception e)
        {
            Logger.Error(e, $"Failed to delete difficulty table: {table.SourcePath}");
        }
    }

    private async void updateTable(DT table)
    {
        try
        {
            if (difficultyTableStore == null || table.SourcePath == null) return;
            if (table.Source != TableSource.RemoteUrl) return;

            var notification = new ProgressNotification
            {
                Text = BmsStrings.UpdatingTable(table.Name),
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
                    notification.CompletionText = BmsStrings.UpdatedTable(importResult.Table.Name, importResult.Table.Entries.Count);
                    notification.Progress = 1;
                    notification.State = ProgressNotificationState.Completed;
                });
            }
            else
            {
                Schedule(() =>
                {
                    notification.CompletionText = BmsStrings.FailedToUpdateTable(table.SourcePath);
                    notification.State = ProgressNotificationState.Cancelled;
                });
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, $"Failed to update difficulty table: {table.SourcePath}");
        }
    }
}
