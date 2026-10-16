#nullable enable
using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsScrollMarkerLifecycle : BmsPlayerTestScene
{
    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(24000, BmsAction.Key1), new BmsReplayFrame(25000), new BmsReplayFrame(60000)]);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var map = new BmsTimingMap(192, [], [new BmsBpmEvent(0, 120, 0)], [],
            [new BmsScrollEvent(768, -1, 0), new BmsScrollEvent(1536, 1, 1)], [], 120);
        var position = map.GetScrollPositionAtTime(24000);
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            TimingMap = map,
            HitObjects = [new BmsLandmine { StartTime = 24000, Column = 1, ScrollPositionAtStartTime = position, LandmineDamagePercent = 5 },
                new BmsNote { StartTime = 30000, Column = 3, ScrollPositionAtStartTime = map.GetScrollPositionAtTime(30000) }],
            InvisibleNotes = [new BmsInvisibleNote { StartTime = 24000, Column = 2, ScrollPositionAtStartTime = position }],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [Test]
    public void MarkersReturnToPoolBetweenAppearancesAndPreserveMineResults()
    {
        AddStep("load markers", () =>
        {
            ((BmsRulesetConfigManager)RulesetConfigs.GetConfigFor(new BmsRuleset())!).SetValue(BmsRulesetSetting.ShowInvisibleNotes, true);
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        foreach (var time in new[] { 8000, 16000, 23000, 16000, 8000, 23000 })
        {
            seek(time);
            var visible = time != 16000;
            AddUntilStep("marker residency matches viewport", () =>
                Playfield.AllColumnAliveObjects().Count(d => d.HitObject is BmsLandmine) == (visible ? 1 : 0)
                && ((BmsColumn)Playfield.Stage.Columns[2]).InvisibleNoteLayer?.Markers.Count == (visible ? 1 : 0));
            if (visible)
                AddAssert("both markers visible", () => Playfield.AllColumnAliveObjects()
                    .Where(d => d.HitObject is BmsLandmine or BmsInvisibleNote).All(d => d.IsPresent && !d.Judged));
        }
        AddAssert("early appearances do not detonate", () => mineResults() == 0);
        seek(24000);
        AddAssert("held crossing detonates once", () => mineResults() == 1);
        seek(24500);
        AddUntilStep("expired markers released", () => !Playfield.AllColumnAliveObjects().Any(d => d.HitObject is BmsLandmine)
            && ((BmsColumn)Playfield.Stage.Columns[2]).InvisibleNoteLayer?.Markers.Count == 0);
        seek(23000);
        AddAssert("rewind undoes detonation", () => mineResults() == 0);
        AddUntilStep("rewound mine visible", () => Playfield.AllColumnAliveObjects().OfType<DrawableBmsHitObject>()
            .Any(d => d.HitObject is BmsLandmine && !d.Judged && d.IsPresent));
        seek(24000);
        AddAssert("replayed crossing detonates once", () => mineResults() == 1);
        AddStep("restore invisible setting", () => ((BmsRulesetConfigManager)RulesetConfigs.GetConfigFor(new BmsRuleset())!)
            .SetValue(BmsRulesetSetting.ShowInvisibleNotes, false));
    }

    private int mineResults() => ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.Count(e => e.Source.Kind == BmsJudgementSourceKind.Landmine);

    private void seek(double time)
    {
        AddStep($"seek {time}", () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(time); });
        AddUntilStep("clock caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < 0.001);
    }
}
