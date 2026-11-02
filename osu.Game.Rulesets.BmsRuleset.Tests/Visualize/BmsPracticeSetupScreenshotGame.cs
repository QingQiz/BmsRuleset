#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Configuration;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.UI.Practice;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Tests.Beatmaps;
using SixLabors.ImageSharp;
using Vector2 = osuTK.Vector2;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

internal partial class BmsPracticeSetupScreenshotGame(string outputDirectory, string locale, int width, int height,
    string? chartPath = null, double? startTime = null, double? endTime = null, bool logicalSize = false,
    double? viewDuration = null, bool focusEnd = false, BmsLayoutVariant? layout = null, bool skipResults = false, int countIn = 3,
    int? extendMinutes = null, float? uiScale = null) : OsuGameBase
{
    internal int ResultCode { get; private set; } = 1;
    private readonly string outputDirectory = Path.GetFullPath(outputDirectory);
    private BmsPracticeSetupOverlay? setup;
    private bool capturing;
    private string chartTitle = string.Empty;
    private int noteCount;
    private int longNoteCount;
    private int sampleCount;
    private BmsLayoutVariant layoutVariant;
    private int totalColumns;

    [Resolved]
    private FrameworkConfigManager frameworkConfig { get; set; } = null!;

    [BackgroundDependencyLoader]
    private void load(FrameworkConfigManager config)
    {
        config.SetValue(FrameworkSetting.WindowMode, WindowMode.Windowed);
        if (Host.Window is { } window)
            window.WindowState = WindowState.Normal;
        config.SetValue(FrameworkSetting.WindowedSize, new System.Drawing.Size(width, height));
    }

    protected override IDictionary<FrameworkSetting, object> GetFrameworkConfigDefaults() => new Dictionary<FrameworkSetting, object>
    {
        [FrameworkSetting.WindowMode] = WindowMode.Windowed,
        [FrameworkSetting.WindowedSize] = new System.Drawing.Size(width, height),
        [FrameworkSetting.AudioDevice] = "No sound",
    };

    protected override Container CreateScalingContainer() => logicalSize
        ? new DrawSizePreservingFillContainer { TargetDrawSize = new Vector2(width, height) }
        : base.CreateScalingContainer();

    public override void SetHost(GameHost host)
    {
        Storage = host.GetStorage(Path.Combine(outputDirectory, "storage"));
        base.SetHost(host);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        // The base game registers its supported locales at LoadComplete().
        frameworkConfig.SetValue(FrameworkSetting.Locale, locale);
        if (uiScale.HasValue)
            LocalConfig.SetValue(OsuSetting.UIScale, uiScale.Value);
        var beatmap = layout.HasValue ? BmsPracticeLayoutFixture.Create(layout.Value) : chartPath == null ? BmsTestBeatmaps.CreateBeatmap() : (BmsBeatmap)new BmsBeatmapConverter(
            BmsBeatmapDecoder.DecodeBytes(File.ReadAllBytes(chartPath), randomValueSelector: _ => 1), new BmsRuleset())
        {
            BranchRandomValueSelector = _ => 1,
        }.Convert();
        if (!layout.HasValue && chartPath == null)
        {
            beatmap.HitObjects.AddRange([
                new BmsNote { StartTime = 3200, Column = 2 },
                new BmsNote { StartTime = 4000, Column = 6 },
                new BmsNote { StartTime = 5500, Column = 4 },
            ]);
            beatmap.BeatmapInfo.Metadata.Title = "Practice selection";
            beatmap.BeatmapInfo.DifficultyName = "7KEY / Charge Notes";
        }
        else if (!layout.HasValue && chartPath != null)
        {
            beatmap.BeatmapInfo.DifficultyName = Path.GetFileNameWithoutExtension(chartPath);
            beatmap.BeatmapInfo.Ruleset = new BmsRuleset().RulesetInfo;
            beatmap.BeatmapInfo.Difficulty.CircleSize = beatmap.TotalColumns;
            beatmap.BeatmapInfo.Length = (int)(beatmap.HitObjects.Max(hit => hit.GetEndTime()) + 2500);
            beatmap.Metadata.Source = Path.GetDirectoryName(Path.GetFullPath(chartPath))!;
        }
        if (extendMinutes.HasValue)
            beatmap = BmsPracticeExtendedChartFixture.RepeatToMinutes(beatmap, extendMinutes.Value);
        chartTitle = beatmap.Metadata.Title;
        noteCount = beatmap.HitObjects.Count;
        longNoteCount = beatmap.HitObjects.OfType<BmsLongNote>().Count();
        sampleCount = beatmap.SampleDefinitions.Count;
        layoutVariant = beatmap.LayoutVariant;
        totalColumns = beatmap.TotalColumns;
        Ruleset.Value = new BmsRuleset().RulesetInfo;
        var source = new TestWorkingBeatmap(beatmap, audioManager: Audio);
        var settings = new BmsPracticeSettings { StartTime = startTime ?? 2500, EndTime = endTime ?? 6500, SkipResults = skipResults, CountIn = countIn };
        Mod[] mods = [new BmsModHalfTime { SpeedChange = { Value = 0.75 } }, .. (layout.HasValue ? BmsPracticeLayoutFixture.ModsFor(layout.Value) : [])];
        setup = new BmsPracticeSetupOverlay(source, mods, settings);
        if (uiScale.HasValue)
        {
            Add(new ScalingContainer.ScalingDrawSizePreservingFillContainer(true)
            {
                TargetDrawSize = logicalSize ? new Vector2(width, height) : new Vector2(1024, 768),
                Child = setup,
            });
        }
        else
            Add(setup);
        setup.Show();
        Scheduler.AddDelayed(Exit, 30000);
    }

    protected override void Update()
    {
        base.Update();
        if (capturing || setup?.IsReady != true)
            return;

        capturing = true;
        if (viewDuration.HasValue)
        {
            setup.SelectionChart.Viewport.Set(setup.SelectionChart.Viewport.Start, viewDuration.Value);
            setup.SelectionChart.LocateBoundary(!focusEnd);
        }
        Scheduler.AddDelayed(() =>
        {
            if (uiScale.HasValue)
                setup.ChildrenOfType<OsuScrollContainer>().Single(scroll => scroll.ChildrenOfType<OsuTextBox>().Contains(setup.StartTimeInput))
                    .ScrollIntoView(setup.SkipResultsInput, false);
            Scheduler.AddDelayed(() => _ = captureSetup(), 100);
        }, 1000);
    }

    private async Task captureSetup()
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, "selection.json"), JsonSerializer.Serialize(new
            {
                Chart = chartPath,
                ExtendedMinutes = extendMinutes,
                Duration = setup!.SelectionChart.Viewport.Maximum - setup.SelectionChart.Viewport.Minimum,
                Fixture = layout.HasValue,
                LayoutVariant = layoutVariant.ToString(),
                TotalColumns = totalColumns,
                Title = chartTitle,
                NoteCount = noteCount,
                LongNoteCount = longNoteCount,
                SampleDefinitions = sampleCount,
                setup!.Settings,
                ChartHeight = setup.SelectionChart.DrawHeight,
                ChartWidth = setup.SelectionChart.DrawWidth,
                setup.SelectionChart.OverviewColumns,
                Width = width,
                Height = height,
                LogicalWidth = setup.DrawWidth,
                LogicalHeight = setup.DrawHeight,
                UIScale = uiScale,
                RepeatLabel = string.Concat(setup.SkipResultsInput.ChildrenOfType<OsuSpriteText>().Select(text => text.Text.ToString())),
                RepeatLabelHeight = setup.SkipResultsInput.DrawHeight,
                RepeatLabelFits = setup.SkipResultsInput.ChildrenOfType<OsuSpriteText>().All(text =>
                    setup.SkipResultsInput.ChildrenOfType<OsuTextFlowContainer>().Single().ScreenSpaceDrawQuad.AABBFloat.Contains(text.ScreenSpaceDrawQuad.AABBFloat)),
                setup.SelectionChart.ActiveBoundary,
                ViewportStart = setup.SelectionChart.Viewport.Start,
                ViewportDuration = setup.SelectionChart.Viewport.Duration,
            }, new JsonSerializerOptions { WriteIndented = true }));
            using var screenshot = await Host.TakeScreenshotAsync();
            await screenshot.SaveAsPngAsync(Path.Combine(outputDirectory, "setup.png"));
            ResultCode = 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
        }
        finally
        {
            Schedule(Exit);
        }
    }
}
