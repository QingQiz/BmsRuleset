#nullable enable
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsMeasureLineLifecycle : BmsPlayerTestScene
{
    private static readonly FieldInfo lineTime = typeof(BmsMeasureLine)
        .GetField("timeAtTick", BindingFlags.Instance | BindingFlags.NonPublic)!;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(60000)]);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = BmsTestBeatmaps.CreateBeatmapFromChart(
            """
            #BPM 120
            #00011:0101010101010101
            #00111:0101010101010101
            #00211:0101010101010101
            #00311:0101010101010101
            #00411:0101010101010101
            #01611:01
            """);
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [Test]
    public void FutureLinesBecomeVisibleAfterSuppressionAndPoolReuse()
    {
        AddStep("load ordinary chart", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);

        foreach (var measureTime in new[] { 2000, 4000, 6000, 8000, 2000 })
        {
            seek(measureTime - 1900);
            AddUntilStep("future line waits beyond timeline barrier", () =>
                lineAt(measureTime)?.Alpha == 0 && !Playfield.ScrollController.IsTimelineVisible(measureTime));

            seek(measureTime - 1000);
            AddUntilStep("measure line returns inside the viewport", () =>
            {
                var line = lineAt(measureTime);
                return line?.Alpha == 1 && line.Y > 0
                    && line.Y + line.Height < Playfield.Stage.DrawHeight - Playfield.Stage.HitTargetPosition;
            });
        }
    }

    private BmsMeasureLine? lineAt(double time)
        => Playfield.Stage.MeasureLineArea.Children.OfType<BmsMeasureLine>()
            .SingleOrDefault(line => (double)lineTime.GetValue(line)! == time);

    private void seek(double time)
    {
        AddStep($"seek {time}", () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(time); });
        AddUntilStep("clock caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < 0.001);
    }
}
