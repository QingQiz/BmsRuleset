using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
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
using osu.Game.Overlays.Settings;
using osu.Game.Overlays.Settings.Sections.Maintenance;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.IO.Import;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Settings.Components;
using osu.Game.Screens;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.BmsRuleset.UI.Settings;

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
                Caption = BmsStrings.ReferenceBpm,
                Current = manager.GetBindable<BmsReferenceBpmMode>(BmsRulesetSetting.ReferenceBpmMode),
            }),
            new SettingsItemV2(new FormSliderBar<double>
            {
                Caption = BmsStrings.BgaDim,
                Current = manager.GetBindable<double>(BmsRulesetSetting.BgaDim),
                DisplayAsPercentage = true,
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = BmsStrings.UnlockFrameRateLimit,
                HintText = BmsStrings.UnlockFrameRateLimitHint,
                Current = manager.GetBindable<bool>(BmsRulesetSetting.UnlockFrameRateLimit),
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = BmsStrings.UseDedicatedPreviewAudio,
                HintText = BmsStrings.DedicatedPreviewAudioHint,
                Current = useDedicatedPreviewAudio,
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = BmsStrings.ShowBms5K,
                Current = bindable5K,
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = BmsStrings.ShowBme7K,
                Current = bindable7K,
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = BmsStrings.ShowPms9K,
                Current = bindable9K,
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = BmsStrings.ShowBms5KDp,
                Current = bindable5KDp,
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = BmsStrings.ShowBme7KDp,
                Current = bindable7KDp,
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = BmsStrings.ShowPms9KDp,
                Current = bindable9KDp,
            }),
            new RoundedButton
            {
                Text = BmsStrings.ImportBmsFiles,
                RelativeSizeAxes = Axes.X,
                Height = 36,
                Action = () => { performer?.PerformFromScreen(menu => menu.Push(new BmsFileImportScreen(manager))); },
                Padding = SettingsPanel.CONTENT_PADDING,
            },
            new RoundedButton
            {
                Text = BmsStrings.CleanupOrphanedSets,
                TooltipText = BmsStrings.CleanupOrphanedSetsTooltip,
                BackgroundColour = colours.YellowDarker,
                RelativeSizeAxes = Axes.X,
                Height = 36,
                Action = confirmCleanupOrphans,
                Padding = SettingsPanel.CONTENT_PADDING,
            },
            new DangerousRoundedButton
            {
                Text = BmsStrings.DeleteAllImportedFiles,
                RelativeSizeAxes = Axes.X,
                Height = 36,
                Action = confirmDeleteAllBmsFiles,
                Padding = SettingsPanel.CONTENT_PADDING,
            },
            new OsuSpriteText
            {
                Text = BmsStrings.BmsVisualOffset,
                Font = OsuFont.GetFont(size: 18),
                Margin = new MarginPadding { Vertical = VERTICAL_PADDING },
                Padding = SettingsPanel.CONTENT_PADDING,
            },
            new VisualOffsetAdjustControl
            {
                Current = manager.GetBindable<double>(BmsRulesetSetting.VisualOffset),
                Margin = new MarginPadding { Bottom = 5 },
            },
            new SettingsItemV2(new FormSliderBar<double>
            {
                Caption = BmsStrings.LongNoteTailVisualOffset,
                Current = manager.GetBindable<double>(BmsRulesetSetting.LongNoteTailVisualOffset),
                KeyboardStep = 1,
                LabelFormat = BmsStrings.OffsetMilliseconds,
                TooltipFormat = BmsStrings.LongNoteTailVisualOffsetTooltip,
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = BmsStrings.AdjustVisualOffsetAutomatically,
                HintText = BmsStrings.AdjustVisualOffsetAutomaticallyTooltip,
                Current = manager.GetBindable<bool>(BmsRulesetSetting.AutomaticallyAdjustVisualOffset),
            }),
            new DifficultyTableSettings(manager),
        ];
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

    private void confirmDeleteAllBmsFiles()
    {
        if (bmsImporter == null)
            return;

        // Mirror osu!'s maintenance mass-delete flow: require an explicit (hold-to-)confirm before
        // irreversibly removing every imported BMS beatmap.
        var dialog = new MassDeleteConfirmationDialog(
            () => bmsImporter.DeleteAllBmsFilesAsync(),
            BmsStrings.DeleteAllConfirmation);

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
            BmsStrings.CleanupOrphansConfirmation);

        if (dialogOverlay != null)
            dialogOverlay.Push(dialog);
        else
            bmsImporter.CleanupOrphanedSets();
    }
}
