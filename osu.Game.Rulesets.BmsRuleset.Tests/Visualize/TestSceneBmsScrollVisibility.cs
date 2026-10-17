#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsScrollVisibility : BmsPlayerTestScene
{
    private bool denseMines;
    private ulong frame;
    private const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Func<Drawable, ulong, int, bool, DrawNode?> generate = typeof(Drawable)
        .GetMethod("GenerateDrawNodeSubtree", flags)!.CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(60000)]);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = BmsTestBeatmaps.CreateBeatmapFromChart(
            """
            #BPM 120
            #SCROLL01 -1
            #SCROLL02 1
            #002SC:01
            #004SC:02
            #00511:01
            #00552:0101
            #00534:01
            #01511:01
            """);
        for (var i = 0; i < (denseMines ? 128 : 1); i++)
        {
            var time = 10000 + i / 1000d;
            beatmap.HitObjects.Add(new BmsLandmine
            {
                StartTime = time,
                Column = 3,
                ScrollPositionAtStartTime = beatmap.TimingMap!.GetScrollPositionAtTime(time),
                LandmineDamagePercent = 1,
            });
        }
        beatmap.HitObjects = beatmap.HitObjects.OrderBy(n => n.StartTime).ToList();
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FoldedVisualsStayHiddenUntilTheForwardScanReachesThem(bool batchMines)
    {
        AddStep("load folded notes", () =>
        {
            denseMines = batchMines;
            ((BmsRulesetConfigManager)RulesetConfigs.GetConfigFor(new BmsRuleset())!)
                .SetValue(BmsRulesetSetting.ShowInvisibleNotes, true);
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        foreach (var time in new[] { 1000, 9000, 1000, 9000 })
        {
            seek(time);
            var visible = time == 9000;
            AddUntilStep("tap and long note follow the timeline barrier", () =>
            {
                var notes = Playfield.AllColumnAliveObjects().Where(d => d.HitObject is BmsNote or BmsLongNote
                    && d.HitObject.StartTime == 10000).ToArray();
                return notes.Length == 2 && notes.All(d => d.IsPresent == visible);
            });
            AddUntilStep("hidden-note marker follows the timeline barrier", () =>
                ((BmsColumn)Playfield.Stage.Columns[4]).InvisibleNoteLayer?.Markers.Count == (visible ? 1 : 0));
            AddUntilStep("measure line follows the timeline barrier", () =>
                Playfield.Stage.MeasureLineArea.Children.OfType<BmsMeasureLine>().Any(line =>
                    (double)typeof(BmsMeasureLine).GetField("timeAtTick", flags)!.GetValue(line)! == 10000
                    && line.Alpha == (visible ? 1 : 0)));
            AddUntilStep("mine rendering follows the timeline barrier", () =>
            {
                var column = (BmsColumnHitObjectContainer)((BmsColumn)Playfield.Stage.Columns[3]).HitObjectContainer;
                if (!batchMines)
                {
                    var mines = column.AliveObjects.Where(d => d.HitObject is BmsLandmine).ToArray();
                    return mines.Length == 1 && mines[0].IsPresent == visible;
                }

                if (!column.BatchMinesActive)
                    return false;
                Playfield.UpdateSubTreeMasking();
                var node = generate(column, ++frame, 0, true)!;
                _ = ((ICompositeDrawNode)node).AddChildDrawNodes;
                var snapshots = (List<BmsColumnHitObjectContainer.MineSnapshot>)node.GetType().GetField("Mines", flags)!.GetValue(node)!;
                return snapshots.Count == (visible ? 128 : 0);
            });
        }
        AddAssert("early visual appearances do not judge notes", () => Player.Results.Count == 0);
        AddStep("restore hidden-note setting", () => ((BmsRulesetConfigManager)RulesetConfigs.GetConfigFor(new BmsRuleset())!)
            .SetValue(BmsRulesetSetting.ShowInvisibleNotes, false));
    }

    private void seek(double time)
    {
        AddStep($"seek {time}", () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(time); });
        AddUntilStep("clock caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < 0.001);
    }
}
