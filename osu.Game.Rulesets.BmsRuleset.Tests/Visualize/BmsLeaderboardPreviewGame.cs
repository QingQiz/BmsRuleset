using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Configuration;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

internal partial class BmsLeaderboardPreviewGame(string outputDirectory, string locale) : OsuGameBase
{
    internal int ResultCode { get; private set; } = 1;
    private readonly string outputDirectory = Path.GetFullPath(outputDirectory);

    [BackgroundDependencyLoader]
    private void load(FrameworkConfigManager config) => config.SetValue(FrameworkSetting.Locale, locale);

    protected override IDictionary<FrameworkSetting, object> GetFrameworkConfigDefaults() => new Dictionary<FrameworkSetting, object>
    {
        [FrameworkSetting.WindowMode] = WindowMode.Windowed,
        [FrameworkSetting.WindowedSize] = new System.Drawing.Size(1280, 1000),
        [FrameworkSetting.AudioDevice] = "No sound",
    };

    public override void SetHost(GameHost host)
    {
        // Preview rendering must not touch the player's database or configuration.
        Storage = host.GetStorage(Path.Combine(outputDirectory, "storage"));
        base.SetHost(host);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        var scene = new TestSceneBmsLeaderboardAppearance();
        scene.OnLoadComplete += drawable => Schedule(() => scene.RunAllSteps(
            () => Scheduler.AddDelayed(() => _ = capture(scene), 2000),
            (_, error) =>
            {
                Console.Error.WriteLine(error);
                Exit();
            }));
        Add(scene);
        Scheduler.AddDelayed(Exit, 30000);
    }

    private async Task capture(TestSceneBmsLeaderboardAppearance scene)
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);
            using var screenshot = await Host.TakeScreenshotAsync();
            await screenshot.SaveAsPngAsync(Path.Combine(outputDirectory, "comparison.png"));
            var row = scene.ChildrenOfType<BmsLeaderboardScore>().First();
            var bounds = row.ScreenSpaceDrawQuad.AABBFloat;
            using var lampDetail = screenshot.Clone(context => context.Crop(new Rectangle((int)bounds.Left - 1, (int)bounds.Top - 1, 40, (int)bounds.Height + 2))
                                                                     .Resize(240, ((int)bounds.Height + 2) * 6, KnownResamplers.NearestNeighbor));
            await lampDetail.SaveAsPngAsync(Path.Combine(outputDirectory, "lamp-detail.png"));

            using var pixels = screenshot.CloneAs<Rgba32>();
            var top = lampInnerEdge(2);
            var middle = lampInnerEdge(BmsLeaderboardScore.HEIGHT / 2);
            var bottom = lampInnerEdge(BmsLeaderboardScore.HEIGHT - 2);
            if (middle < 1 || top < middle + 2 || bottom < middle + 2)
                throw new InvalidOperationException($"Lamp inner edge is not curved: top={top}, middle={middle}, bottom={bottom}.");

            int[] sampleRows = [6, 12, 38, 44];
            int[] sampleColumns = [9, 12, 15];
            foreach (var y in sampleRows)
            {
                var expected = pixelAt(18, y);
                foreach (var x in sampleColumns)
                {
                    var actual = pixelAt(x, y);
                    if (Math.Abs(actual.R - expected.R) > 3 || Math.Abs(actual.G - expected.G) > 3 || Math.Abs(actual.B - expected.B) > 3)
                        throw new InvalidOperationException($"Lamp changes the native background at ({x}, {y}): {actual}, expected {expected}.");
                }
            }

            ResultCode = 0;

            Rgba32 pixelAt(float x, float y)
            {
                var point = row.ToScreenSpace(new Vector2(x, y));
                return pixels[(int)point.X, (int)point.Y];
            }

            int lampInnerEdge(float y)
            {
                var edge = -1;
                for (var x = 0; x < 18; x++)
                {
                    var pixel = pixelAt(x, y);
                    if (pixel.R > pixel.B + 50 && pixel.G > pixel.B + 40)
                        edge = x;
                }

                return edge;
            }
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
