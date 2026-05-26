using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Game.Database;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.ImportExport;
using osu.Game.Rulesets.BmsRuleset.Screens;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Screens;

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
    private OsuGameBase? game { get; set; }

    [Resolved(CanBeNull = true)]
    private GameHost? host { get; set; }

    [Resolved(CanBeNull = true)]
    private IPerformFromScreenRunner? performer { get; set; }

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);

        if (bmsImporter != null && game != null)
            game.UnregisterImportHandler(bmsImporter);
    }

    #endregion

    [BackgroundDependencyLoader]
    private void load()
    {
        if (bmsImporter == null && realm != null && storage != null && game != null)
        {
            bmsImporter = new BmsFileImporter(realm, storage, notifications);
            game.RegisterImportHandler(bmsImporter);
        }

        if (Config is not BmsRulesetConfigManager manager)
            return;

        Children =
        [
            new SettingsItemV2(new FormSliderBar<double>
            {
                Caption = RulesetSettingsStrings.ScrollSpeed,
                Current = manager.GetBindable<double>(BmsRulesetSetting.ScrollSpeed),
                KeyboardStep = 0.1f,
                LabelFormat = v => RulesetSettingsStrings.ScrollSpeedTooltip((int)BmsDrawableRuleset.ComputeScrollTime(v), v),
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
                Action = () => bmsImporter?.DeleteAllBmsFiles(),
                Padding = new MarginPadding { Horizontal = SettingsPanel.CONTENT_MARGINS },
            },
        ];
    }
}
