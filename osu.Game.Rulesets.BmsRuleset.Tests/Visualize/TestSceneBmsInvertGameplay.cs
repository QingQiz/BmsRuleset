using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Mods.LongNoteMode;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsInvertGameplay : BmsPlayerTestScene
{
    private static readonly double[] offsets = [0, 125, 500, 1500, 2750, 3000, 4250, 4375, 4500];

    private BmsTestSkins.SkinKind skin;
    private bool doublePlay;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(beatmap => new BmsAutoGenerator(beatmap).Generate().Frames.ToList(), skin);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = doublePlay ? BmsLayoutVariant.Bme7KDouble : BmsLayoutVariant.Bme7K,
            TotalColumns = doublePlay ? 16 : 8,
            Rank = 2,
        };
        foreach (var offset in offsets)
        {
            for (var column = 0; column < beatmap.TotalColumns; column++)
                beatmap.HitObjects.Add(new BmsNote { StartTime = 1000 + offset, Column = column });
        }

        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [Test]
    public void TestConvertedNotesHoldReleaseAndReplay(
        [Values(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode mode,
        [Values(BmsTestSkins.SkinKind.Argon, BmsTestSkins.SkinKind.Classic, BmsTestSkins.SkinKind.Legacy)] BmsTestSkins.SkinKind skinKind,
        [Values(false, true)] bool randomDoublePlay)
    {
        BmsGaugeStateSnapshot[] heldGauges = [];
        BmsGaugeStateSnapshot[] completedGauges = [];
        var completedGaugeEvents = 0;
        AddStep("load ordinary notes with IN", () =>
        {
            skin = skinKind;
            doublePlay = randomDoublePlay;
            Mod modeMod = mode switch
            {
                BmsLongNoteMode.ChargeNote => new BmsModChargeNote(),
                BmsLongNoteMode.HellChargeNote => new BmsModHellChargeNote(),
                _ => new BmsModLongNote(),
            };
            LoadPlayer([
                new BmsModInvert { RandomiseLength = { Value = randomDoublePlay }, Seed = { Value = 12345 } },
                modeMod,
            ]);
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        // TestPlayer records applied results only; keep the assertions scoped to the current attempt.
        AddStep("track reverted results", () => Player.ScoreProcessor.JudgementReverted += result => Player.Results.Remove(result));
        AddAssert("IN converted every non-final column note", () =>
        {
            var chart = (BmsBeatmap)Player.GameplayState.Beatmap;
            return chart.LockedLongNoteMode == mode
                   && chart.HitObjects.OfType<BmsLongNote>().Count() == chart.TotalColumns * (offsets.Length - 1)
                   && chart.HitObjects.OfType<BmsNote>().Count() == chart.TotalColumns;
        });

        seek(1550);
        assertHolding();
        AddStep("capture held gauge states", () => heldGauges = ((BmsHealthProcessor)Player.HealthProcessor).CurrentGaugeStates.ToArray());
        seek(5900);
        assertCompleted(mode);
        AddStep("capture completed gauge states", () =>
        {
            completedGauges = ((BmsHealthProcessor)Player.HealthProcessor).CurrentGaugeStates.ToArray();
            completedGaugeEvents = ((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory.Count;
        });

        seek(1550);
        assertHolding();
        AddAssert("rewind restores held gauge states", () => ((BmsHealthProcessor)Player.HealthProcessor).CurrentGaugeStates, () => Is.EqualTo(heldGauges));
        seek(5900);
        assertCompleted(mode);
        AddAssert("replay restores completed gauge states", () => ((BmsHealthProcessor)Player.HealthProcessor).CurrentGaugeStates, () => Is.EqualTo(completedGauges));
        AddAssert("replay does not duplicate gauge events", () => ((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory.Count, () => Is.EqualTo(completedGaugeEvents));

        seek(0);
        AddAssert("rewind clears all judgement statistics", () => Player.ScoreProcessor.Statistics.Values.All(count => count == 0));
        seek(1550);
        assertHolding();
        seek(5900);
        assertCompleted(mode);
        AddAssert("full replay preserves gauge events", () => ((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory.Count, () => Is.EqualTo(completedGaugeEvents));
    }

    private void seek(double time)
    {
        AddStep($"seek {time}", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(time);
        });
        AddUntilStep("simulation caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < 0.001);
    }

    private void assertHolding()
        => AddAssert("converted long notes remain visible while held", () =>
            Playfield.AllColumnAliveObjects().Count(d => d.HitObject.StartTime == 1500
                                                        && d is ILongNoteHolder { IsHoldingLongNote: true } && d.IsPresent)
            == (doublePlay ? 16 : 8));

    private void assertCompleted(BmsLongNoteMode mode)
    {
        AddAssert("all heads and tails are perfect", () =>
        {
            var chart = (BmsBeatmap)Player.GameplayState.Beatmap;
            var endpoints = Player.Results.OfType<BmsLongNoteJudgementResult>().SelectMany(r => r.EndpointResults).ToArray();
            return chart.HitObjects.OfType<BmsLongNote>().All(ln =>
                endpoints.Count(e => e.Source == ln && e.Kind == BmsLongNoteEndpointKind.Head && e.Result == HitResult.Perfect) == 1
                && endpoints.Count(e => e.Source == ln && e.Kind == BmsLongNoteEndpointKind.Tail && e.Result == HitResult.Perfect) == 1);
        });
        AddAssert("no non-Perfect judgements",
            () => Player.ScoreProcessor.Statistics.Where(p => p.Key != HitResult.Perfect && p.Value != 0).ToArray(), () => Is.Empty);
        AddAssert("score counts both charge endpoints and final taps",
            () => Player.ScoreProcessor.Statistics.GetValueOrDefault(HitResult.Perfect),
            () => Is.EqualTo((doublePlay ? 16 : 8) * ((offsets.Length - 1) * (mode == BmsLongNoteMode.LongNote ? 1 : 2) + 1)));
    }
}
