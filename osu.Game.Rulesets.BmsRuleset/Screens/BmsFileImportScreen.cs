#nullable disable

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Screens;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Screens;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Screens;

public partial class BmsFileImportScreen(BmsRulesetConfigManager config = null) : OsuScreen
{
    public override bool HideOverlaysOnEnter => true;

    private const float duration = 300;
    private const float button_height = 50;
    private const float button_vertical_margin = 10;

    private OsuFileSelector fileSelector = null!;
    private Container contentContainer = null!;
    private TextFlowContainer currentFileText = null!;

    private RoundedButton importButton = null!;
    private RoundedButton importFolderButton = null!;

    [Cached]
    private OverlayColourProvider colourProvider = new(OverlayColourScheme.Purple);

    private FillFlowContainer buttonGroup;
    private Bindable<string> lastImportPath;

    [Resolved]
    private OsuGameBase game { get; set; } = null!;

    [Resolved(CanBeNull = true)]
    private BmsRulesetConfigManager resolvedConfig { get; set; }

    public override void OnEntering(ScreenTransitionEvent e)
    {
        base.OnEntering(e);

        contentContainer.ScaleTo(0.95f).ScaleTo(1, duration, Easing.OutQuint);
        this.FadeInFromZero(duration);
    }

    public override bool OnExiting(ScreenExitEvent e)
    {
        contentContainer.ScaleTo(0.95f, duration, Easing.OutQuint);
        this.FadeOut(duration, Easing.OutQuint);

        return base.OnExiting(e);
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        lastImportPath = (config ?? resolvedConfig)?.GetBindable<string>(BmsRulesetSetting.LastImportPath);
        var lastPath = lastImportPath?.Value;

        buttonGroup = new FillFlowContainer
        {
            Anchor = Anchor.BottomCentre,
            Origin = Anchor.BottomCentre,
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, button_vertical_margin),
            Width = 0.9f,
            Padding = new MarginPadding { Bottom = 2 * button_vertical_margin },
            Children =
            [
                importButton = new RoundedButton
                {
                    Text = "Import selected file",
                    RelativeSizeAxes = Axes.X,
                    Height = button_height,
                    Action = () => startImport(fileSelector.CurrentFile.Value?.FullName),
                },
                importFolderButton = new RoundedButton
                {
                    Text = "Import all in current folder",
                    RelativeSizeAxes = Axes.X,
                    Height = button_height,
                    Action = () => startDirectoryImport(false),
                },
                new RoundedButton
                {
                    Text = "Import all from directory (recursive)",
                    TooltipText = "Imports all BMS files from the selected directory and subdirectories",
                    RelativeSizeAxes = Axes.X,
                    Height = button_height,
                    Action = () => startDirectoryImport(true),
                },
            ],
        };

        InternalChild = contentContainer = new Container
        {
            Masking = true,
            CornerRadius = 10,
            RelativeSizeAxes = Axes.Both,
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(0.9f, 0.8f),
            Children =
            [
                fileSelector = new OsuFileSelector(
                    !string.IsNullOrEmpty(lastPath) && Directory.Exists(lastPath) ? lastPath : null,
                    Constant.BMS_EXTENSIONS)
                {
                    RelativeSizeAxes = Axes.Both,
                    Width = 0.65f,
                },
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Width = 0.35f,
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Children =
                    [
                        new Box
                        {
                            Colour = colourProvider.Background4,
                            RelativeSizeAxes = Axes.Both,
                        },
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Child = new OsuScrollContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Child = currentFileText = new TextFlowContainer(t => t.Font = OsuFont.Default.With(size: 30))
                                {
                                    AutoSizeAxes = Axes.Y,
                                    RelativeSizeAxes = Axes.X,
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    TextAnchor = Anchor.Centre,
                                },
                                ScrollContent =
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                },
                            },
                        },
                        buttonGroup,
                    ],
                },
            ],
        };

        fileSelector.CurrentFile.BindValueChanged(fileChanged, true);
        fileSelector.CurrentPath.BindValueChanged(directoryChanged);
    }

    private void directoryChanged(ValueChangedEvent<DirectoryInfo> directoryChangedEvent)
    {
        fileSelector.CurrentFile.Value = null;

        var newDirectory = directoryChangedEvent.NewValue;
        var hasBmsFiles = newDirectory != null
                          && newDirectory.Exists
                          && newDirectory.EnumerateFiles().Any(f => Constant.BMS_EXTENSIONS.Contains(f.Extension));

        importFolderButton.Enabled.Value = hasBmsFiles;

        if (newDirectory != null)
        {
            if (lastImportPath != null)
                lastImportPath.Value = newDirectory.FullName;
        }
    }

    private void fileChanged(ValueChangedEvent<FileInfo> selectedFile)
    {
        importButton.Enabled.Value = selectedFile.NewValue != null;
        currentFileText.Text = selectedFile.NewValue?.Name ?? "Select a file/folder";
    }

    private void startImport(params string[] paths)
    {
        if (paths.Length == 0)
            return;

        Task.Factory.StartNew(async () =>
        {
            await game.Import(paths).ConfigureAwait(false);

            Schedule(() => { fileSelector.CurrentPath.TriggerChange(); });
        }, TaskCreationOptions.LongRunning);
    }

    private void startDirectoryImport(bool recursive)
    {
        var path = fileSelector.CurrentPath.Value;
        if (path == null || !path.Exists)
            return;

        var files = Directory.GetFiles(
            path.ToString(), "*.*",
            recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

        var filesToImport = files
            .Where(Constant.IsChartFile)
            .ToArray();

        if (filesToImport.Length == 0)
            return;

        startImport(filesToImport);
    }
}
