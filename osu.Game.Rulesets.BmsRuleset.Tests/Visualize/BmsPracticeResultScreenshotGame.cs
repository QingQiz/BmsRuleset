#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Configuration;
using osu.Framework.Platform;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

internal partial class BmsPracticeResultScreenshotGame(string outputDirectory, string locale, bool countdown = false) : OsuGameBase
{
    internal int ResultCode { get; private set; } = 1;
    private readonly string outputDirectory = Path.GetFullPath(outputDirectory);

    [Resolved]
    private FrameworkConfigManager frameworkConfig { get; set; } = null!;

    [BackgroundDependencyLoader]
    private void load(FrameworkConfigManager config)
    {
        config.SetValue(FrameworkSetting.WindowMode, WindowMode.Windowed);
        if (Host.Window is { } window)
            window.WindowState = WindowState.Normal;
        config.SetValue(FrameworkSetting.WindowedSize, new System.Drawing.Size(1460, 774));
    }

    protected override IDictionary<FrameworkSetting, object> GetFrameworkConfigDefaults() => new Dictionary<FrameworkSetting, object>
    {
        [FrameworkSetting.WindowMode] = WindowMode.Windowed,
        [FrameworkSetting.WindowedSize] = new System.Drawing.Size(1460, 774),
        [FrameworkSetting.AudioDevice] = "No sound",
    };

    public override void SetHost(GameHost host)
    {
        Storage = host.GetStorage(Path.Combine(outputDirectory, "storage"));
        base.SetHost(host);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        frameworkConfig.SetValue(FrameworkSetting.Locale, locale);
        var scene = new TestSceneBmsCourseSelect { KeepPracticeResultOpen = !countdown, KeepPracticeCountdownOpen = countdown };
        scene.SetUpSteps();
        if (countdown)
            scene.TestPracticeRealChartNaturalClockAndLiveInput("Destr0yer (by 削除 feat. Nikki Simmons)", "7ANOTHER.bml", 0.75, true, 0);
        else
            scene.TestPracticeResultWaitsForNextAttempt(false, false);
        scene.OnLoadComplete += drawable => Schedule(() => scene.RunAllSteps(
            () => Scheduler.AddDelayed(() => _ = capture(scene), countdown ? 100 : 1000),
            (_, error) =>
            {
                Console.Error.WriteLine(error);
                Exit();
            }));
        Add(scene);
        Scheduler.AddDelayed(Exit, 60000);
    }

    private async Task capture(TestSceneBmsCourseSelect scene)
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);
            using var screenshot = await Host.TakeScreenshotAsync();
            var bounds = scene.PracticeResultCaptureTarget.ScreenSpaceDrawQuad.AABBFloat;
            screenshot.Mutate(context => context.Crop(new Rectangle((int)bounds.Left, (int)bounds.Top,
                Math.Min((int)bounds.Width, screenshot.Width - (int)bounds.Left),
                Math.Min((int)bounds.Height, screenshot.Height - (int)bounds.Top))));
            await screenshot.SaveAsPngAsync(Path.Combine(outputDirectory, countdown ? "playfield-countdown.png" : "loop-results.png"));
            ResultCode = 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
        }
        finally
        {
            Schedule(Exit);
        }
    }
}
