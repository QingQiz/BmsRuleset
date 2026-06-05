using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Overlays.Settings.Sections.Maintenance;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.ImportExport;
using osu.Game.Rulesets.BmsRuleset.Screens;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Screens;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.BmsRuleset.Settings;

public partial class BmsSettingsSubsection(BmsRuleset ruleset) : RulesetSettingsSubsection(ruleset)
{
    protected override LocalisableString Header => "BMS";

    [Cached]
    private OverlayColourProvider colourProvider = new(OverlayColourScheme.Purple);

    private BmsFileImporter? bmsImporter;

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
                OnImportCompleted = (beatmapSet, scope) => beatmapUpdater?.Queue(beatmapSet, scope),
            };
            game.RegisterImportHandler(bmsImporter);
        }

        if (Config is not BmsRulesetConfigManager manager)
            return;

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
        ];
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
}
