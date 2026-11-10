using System;
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
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Overlays.Settings.Sections.Maintenance;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
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

        var layoutFilters = BmsLayoutVisibility.Options.Select(option =>
        {
            var current = manager.GetBindable<bool>(option.Setting);
            current.BindValueChanged(_ => onLayoutSettingChanged());
            return new SettingsItemV2(new FormCheckBox { Caption = option.Caption, Current = current });
        }).ToArray();
        var useDedicatedPreviewAudio = manager.GetBindable<bool>(BmsRulesetSetting.UseDedicatedPreviewAudio);
        var softConstant = manager.GetBindable<bool>(BmsRulesetSetting.SoftConstant);

        useDedicatedPreviewAudio.BindValueChanged(_ => reloadCurrentPreview());

        Children =
        [
            new SettingsItemV2(new FormSliderBar<double>
            {
                Caption = BmsStrings.ScrollSpeed,
                HintText = BmsStrings.ScrollSpeedHint,
                Current = manager.GetBindable<double>(BmsRulesetSetting.ScrollSpeed),
                KeyboardStep = 0.1f,
                LabelFormat = formatScrollSpeed,
            }),
            new SettingsItemV2(new FormEnumDropdown<BmsReferenceBpmMode>
            {
                Caption = BmsStrings.ReferenceBpm,
                Current = manager.GetBindable<BmsReferenceBpmMode>(BmsRulesetSetting.ReferenceBpmMode),
            }),
            new SettingsItemV2(new FormEnumDropdown<BmsJudgementAlgorithm>
            {
                Caption = BmsStrings.JudgementAlgorithm,
                HintText = BmsStrings.JudgementAlgorithmHint,
                Current = manager.GetBindable<BmsJudgementAlgorithm>(BmsRulesetSetting.JudgementAlgorithm),
            }),
            new SettingsItemV2(new FormSliderBar<double>
            {
                Caption = BmsStrings.BgaDim,
                Current = manager.GetBindable<double>(BmsRulesetSetting.BgaDim),
                DisplayAsPercentage = true,
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = BmsStrings.ShowInvisibleNotes,
                HintText = BmsStrings.ShowInvisibleNotesHint,
                Current = manager.GetBindable<bool>(BmsRulesetSetting.ShowInvisibleNotes),
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
            new ExpandableSettingsGroup(new SettingsItemV2(new FormCheckBox
            {
                Caption = BmsStrings.SoftConstant,
                HintText = BmsStrings.SoftConstantHint,
                Current = softConstant,
            }), new SettingsItemV2(new FormSliderBar<double>
            {
                Caption = BmsStrings.SoftConstantFadeIn,
                HintText = BmsStrings.SoftConstantFadeInHint,
                Current = manager.GetBindable<double>(BmsRulesetSetting.SoftConstantFadeIn),
                KeyboardStep = 1,
                LabelFormat = BmsStrings.OffsetMilliseconds,
            }), softConstant),
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
            new BeatmapManagementSettings
            {
                Children =
                [
                    ..layoutFilters,
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
                ],
            },
        ];
    }

    private static LocalisableString formatScrollSpeed(double speed)
    {
        var duration = Math.Round(BmsDrawableRuleset.ComputeScrollTime(speed), MidpointRounding.AwayFromZero);
        return BmsStrings.ScrollSpeedValue(speed, duration, BmsGameplayScrollController.ComputeGreenNumber(duration));
    }

    private sealed partial class BeatmapManagementSettings : SettingsSubsection
    {
        protected override LocalisableString Header => BmsStrings.BeatmapManagement;
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
