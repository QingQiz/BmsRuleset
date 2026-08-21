using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Mods.LongNoteMode;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.BmsRuleset.UI.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsMods : BmsPlayerTestScene
{
    // Per-test replay factory; defaults to watch-only so the existing mod tests keep their
    // "load and inspect the beatmap/playfield" behaviour. Tests that need to drive gameplay
    // swap this before LoadPlayer.
    private Func<BmsBeatmap, IList<ReplayFrame>> replay = BmsTestReplays.CreateWatchOnlyFrames;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
    {
        // Snapshot the per-test replay, then reset to the watch-only default so a later test
        // never inherits a previous test's custom replay.
        var replayForThisPlayer = replay;
        replay = BmsTestReplays.CreateWatchOnlyFrames;
        return CreateBmsPlayer(replayForThisPlayer);
    }

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = BmsTestBeatmaps.CreateBeatmap();
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    private DrawableBmsHitObject firstAliveScratchNote()
        => Playfield.HitObjectContainer.AliveObjects
            .OfType<DrawableBmsHitObject>()
            .FirstOrDefault(d => BmsLayout.IsScratchColumn(d.HitObject.Column, Playfield.LayoutVariant));

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void TestAutoScratchHoldsAndCompletesScratchLongNote(BmsLongNoteMode mode)
    {
        this.AddSetupStep("load player with AS and LN mode", () =>
            LoadPlayer([new BmsModAutoScratch(), createLongNoteModeMod(mode)]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);

        AddUntilStep("scratch LN is automatically held", () =>
            Player.GameplayClockContainer.CurrentTime >= BmsTestBeatmaps.LN_SCENARIO_START_TIME + BmsTestBeatmaps.LN_SCENARIO_DURATION / 2
            && Playfield.GetAliveObjectAtTime(BmsTestBeatmaps.LN_SCENARIO_START_TIME) is ILongNoteHolder
            {
                IsAutomaticallyHeld: true,
                IsHoldingLongNote: true,
            });

        AddUntilStep("scratch LN tail is judged", () =>
            Player.GameplayClockContainer.CurrentTime >= BmsTestBeatmaps.LN_SCENARIO_START_TIME + BmsTestBeatmaps.LN_SCENARIO_DURATION + 100);

        AddAssert("scratch LN has successful head and tail judgements", () =>
        {
            var events = ((BmsScoreProcessor)Player.GameplayState.ScoreProcessor).JudgementEvents
                .Where(e => e.Source.Column == 0
                            && e.TimingObservations.Any(o =>
                                o.Kind is BmsTimingObservationKind.LongNoteHead or BmsTimingObservationKind.LongNoteTail
                                && o.ExpectedTime >= BmsTestBeatmaps.LN_SCENARIO_START_TIME
                                && o.ExpectedTime <= BmsTestBeatmaps.LN_SCENARIO_START_TIME + BmsTestBeatmaps.LN_SCENARIO_DURATION))
                .ToArray();

            var observations = events.SelectMany(e => e.TimingObservations).ToArray();
            var expectedEventCount = mode == BmsLongNoteMode.LongNote ? 1 : 2;

            return events.Length == expectedEventCount
                   && observations.Select(o => o.Kind).SequenceEqual([
                       BmsTimingObservationKind.LongNoteHead,
                       BmsTimingObservationKind.LongNoteTail,
                   ])
                   && observations.All(o => o.Result.IsHit());
        });
    }

    private static Mod createLongNoteModeMod(BmsLongNoteMode mode) => mode switch
    {
        BmsLongNoteMode.LongNote => new BmsModLongNote(),
        BmsLongNoteMode.ChargeNote => new BmsModChargeNote(),
        BmsLongNoteMode.HellChargeNote => new BmsModHellChargeNote(),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    [Test]
    public void TestAutoGauge()
    {
        // Autoplay raises the groove gauges past their clear threshold, then the replay stops pressing so the
        // remaining notes miss and the survival tiers fail one by one — the cascade the AG
        // mod exists to demonstrate. The 0.2 groove-tier start would otherwise drain them
        // dead before the survival tiers fail, so the pre-fill is what makes Normal reachable.
        const double auto_play_until = 13250;

        this.AddSetupStep("load player with AG mod + autoplay-then-idle replay", () =>
        {
            replay = b => BmsTestReplays.CreateAutoPlayThenIdleFrames(b, auto_play_until);
            LoadPlayer([new BmsModAutoGauge()]);
        });
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());

        AddAssert("gauge starts at hardest tier (Hazard)", () =>
            Player.GameplayState.HealthProcessor is BmsHealthProcessor hp && hp.GaugeType == BmsGaugeType.Hazard);

        // Let the autoplay phase build a groove-gauge buffer (the replay hits every note before the idle
        // boundary, so no tier has failed yet).
        AddUntilStep("autoplay phase complete", () =>
            Player.GameplayClockContainer.CurrentTime >= auto_play_until);

        // Idle: missed notes drain the survival tiers in order (Hazard, ExHard, Hard) and the
        // active gauge steps down Hazard → ExHard → Hard → Normal. Recording Normal is also the
        // proof that the pre-fill worked — from the 0.2 groove start Normal would be dead long
        // before Hard fails, so the cascade could never land on it without the autoplay buffer.
        AddUntilStep("gauge downgraded through Normal", () =>
            Player.GameplayState.HealthProcessor is BmsHealthProcessor hp
            && hp.GaugeHistory.Any(e => e.ActiveGaugeType == BmsGaugeType.Normal));
    }

    [Test]
    public void TestAutoScratch()
    {
        this.AddSetupStep("load player with AS mod", () => LoadPlayer([new BmsModAutoScratch()]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());

        AddAssert("scratch not hidden", () => !Playfield.Stage.Columns[0].Hidden);
    }

    [Test]
    public void TestHideScratch()
    {
        var mod = new BmsModHideScratch();

        this.AddSetupStep("load player with AS+HS mod", () => LoadPlayer([mod]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());

        AddAssert("scratch hidden", () => Playfield.Stage.Columns[0].Hidden);

        // Regression (issue 4): hidden scratch notes live in a zero-width column and are
        // auto-judged. They must never become visible/freeze on screen. Alpha is enforced in
        // DrawableBmsHitObject.Update so this holds even in headless mode without draw geometry.
        AddStep("seek to start", () => Player.GameplayClockContainer.Seek(0));
        AddAssert("no scratch note becomes alive", () => firstAliveScratchNote() == null);
    }

    [Test]
    public void TestLaneRandomCustomOrder()
    {
        var mod = new BmsModLaneRandom { LaneOrder = { Value = "0,2,4,6,1,3,5,7" } };

        this.AddSetupStep("load player with LRN custom order", () => LoadPlayer([mod]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);

        AddAssert("all 48 notes match LaneOrder mapping", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            var normalNotes = beatmap.HitObjects
                .Where(h => h is not BmsLongNote && h is not BmsLandmine)
                .OrderBy(h => h.StartTime)
                .ToList();

            int[] originalPattern =
            [
                0, 2, 4, 6, 1, 3, 5, 7,
                0, 4, 2, 6, 3, 7, 1, 5,
                0, 1, 2, 3, 4, 5, 6, 7,
                7, 6, 5, 4, 3, 2, 1, 0,
                0, 2, 4, 6, 1, 3, 5, 7,
                0, 4, 2, 6, 3, 7, 1, 5,
            ];

            // Parse LaneOrder "0,2,4,6,1,3,5,7" into a mapping array.
            int[] laneOrderMap = [0, 2, 4, 6, 1, 3, 5, 7];

            if (normalNotes.Count < originalPattern.Length)
                return false;

            for (var i = 0; i < originalPattern.Length; i++)
            {
                var expected = laneOrderMap[originalPattern[i]];
                if (normalNotes[i].Column != expected)
                    return false;
            }

            return true;
        });
    }

    [Test]
    public void TestLaneRandomWithSeed()
    {
        var mod = new BmsModLaneRandom { Seed = { Value = 12345 } };

        this.AddSetupStep("load player with LRN mod", () => LoadPlayer([mod]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());

        AddAssert("all non-scratch columns appear exactly once (bijection)", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            var normalNotes = beatmap.HitObjects
                .Where(h => h is not BmsLongNote && h is not BmsLandmine)
                .OrderBy(h => h.StartTime)
                .ToList();

            // With IncludeScratch=false (default), column 0 stays at 0.
            // Non-scratch notes at indices 1-7 must be a permutation of [1,2,3,4,5,6,7].
            if (normalNotes.Count < 8) return false;

            var nonScratch = normalNotes.Skip(1).Take(7).Select(n => n.Column).OrderBy(c => c).ToArray();
            return nonScratch.SequenceEqual([1, 2, 3, 4, 5, 6, 7]);
        });
    }

    [Test]
    public void TestMirror()
    {
        this.AddSetupStep("load player with MR mod", () => LoadPlayer([new BmsModMirror()]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());

        AddAssert("hit object columns mirrored", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            var normalNotes = beatmap.HitObjects
                .Where(h => h is not BmsLongNote && h is not BmsLandmine)
                .OrderBy(h => h.StartTime)
                .ToList();

            int[] originalPattern = [0, 2, 4, 6, 1, 3, 5, 7, 0, 4, 2, 6, 3, 7, 1, 5, 0, 1, 2, 3, 4, 5, 6, 7, 7, 6, 5, 4, 3, 2, 1, 0, 0, 2, 4, 6, 1, 3, 5, 7, 0, 4, 2, 6, 3, 7, 1, 5];
            int mirroredColumn(int col) => col switch { 0 => 0, 1 => 7, 2 => 6, 3 => 5, 4 => 4, 5 => 3, 6 => 2, 7 => 1, _ => col };

            for (var i = 0; i < originalPattern.Length && i < normalNotes.Count; i++)
            {
                var expected = mirroredColumn(originalPattern[i]);
                if (normalNotes[i].Column != expected)
                    return false;
            }

            return true;
        });
    }

    [Test]
    public void TestNoteRandomExcludeScratch()
    {
        var mod = new BmsModNoteRandom { IncludeScratch = { Value = false }, Seed = { Value = 42 } };

        this.AddSetupStep("load player with NRN exclude scratch", () => LoadPlayer([mod]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);

        AddAssert("scratch notes preserved, non-scratch columns populated", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            var normalNotes = beatmap.HitObjects
                .Where(h => h is not BmsLongNote && h is not BmsLandmine)
                .ToList();

            // IncludeScratch=false keeps original scratch notes at column 0.
            var hasScratchNotes = normalNotes.Any(n => n.Column == 0);
            // Non-scratch notes are shuffled among columns 1-7.
            var hasNonScratchNotes = normalNotes.Any(n => n.Column > 0);
            return hasScratchNotes && hasNonScratchNotes;
        });
    }

    [Test]
    public void TestNoteRandomHRandom()
    {
        var mod = new BmsModNoteRandom { Mode = { Value = BmsNoteRandomMode.H_Random }, Seed = { Value = 42 } };

        this.AddSetupStep("load player with NRN H-Random mod", () => LoadPlayer([mod]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);

        AddAssert("notes shuffled from original columns", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            var normalNotes = beatmap.HitObjects
                .Where(h => h is not BmsLongNote && h is not BmsLandmine)
                .OrderBy(h => h.StartTime)
                .ToList();

            int[] originalPattern = [0, 2, 4, 6, 1, 3, 5, 7, 0, 4, 2, 6, 3, 7, 1, 5, 0, 1, 2, 3, 4, 5, 6, 7, 7, 6, 5, 4, 3, 2, 1, 0];

            for (var i = 0; i < originalPattern.Length && i < normalNotes.Count; i++)
            {
                if (normalNotes[i].Column != originalPattern[i])
                    return true;
            }

            return false;
        });
    }

    [Test]
    public void TestNoteRandomSRandom()
    {
        var mod = new BmsModNoteRandom { Mode = { Value = BmsNoteRandomMode.S_Random }, Seed = { Value = 42 } };

        this.AddSetupStep("load player with NRN S-Random mod", () => LoadPlayer([mod]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);

        AddAssert("notes shuffled from original columns", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            var normalNotes = beatmap.HitObjects
                .Where(h => h is not BmsLongNote && h is not BmsLandmine)
                .OrderBy(h => h.StartTime)
                .ToList();

            int[] originalPattern = [0, 2, 4, 6, 1, 3, 5, 7, 0, 4, 2, 6, 3, 7, 1, 5, 0, 1, 2, 3, 4, 5, 6, 7, 7, 6, 5, 4, 3, 2, 1, 0];

            // At least some notes should differ from the original pattern.
            for (var i = 0; i < originalPattern.Length && i < normalNotes.Count; i++)
            {
                if (normalNotes[i].Column != originalPattern[i])
                    return true;
            }

            return false;
        });
    }

    [Test]
    public void TestRandomModsIncompatible()
    {
        AddAssert("LRN incompatible with NRN and MR", () =>
        {
            var lrn = new BmsModLaneRandom();
            return lrn.IncompatibleMods.Contains(typeof(BmsModNoteRandom))
                   && lrn.IncompatibleMods.Contains(typeof(BmsModMirror));
        });
        AddAssert("NRN incompatible with LRN and MR", () =>
        {
            var nrn = new BmsModNoteRandom();
            return nrn.IncompatibleMods.Contains(typeof(BmsModLaneRandom))
                   && nrn.IncompatibleMods.Contains(typeof(BmsModMirror));
        });
        AddAssert("RR incompatible with LRN, NRN, and MR", () =>
        {
            var rr = new BmsModRotationRandom();
            return rr.IncompatibleMods.Contains(typeof(BmsModLaneRandom))
                   && rr.IncompatibleMods.Contains(typeof(BmsModNoteRandom))
                   && rr.IncompatibleMods.Contains(typeof(BmsModMirror));
        });
    }

    [Test]
    public void TestSecondPlayer()
    {
        this.AddSetupStep("load player with 2P mod", () => LoadPlayer([new BmsModSecondPlayer()]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());

        AddAssert("stage scratch on right", () => BmsLayout.Is2P(Playfield.LayoutVariant));

        AddAssert("hit object columns unchanged (scratch=0, keys=1..N)", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            var normalNotes = beatmap.HitObjects
                .Where(h => h is not BmsLongNote && h is not BmsLandmine)
                .OrderBy(h => h.StartTime)
                .ToList();

            int[] originalPattern = [0, 2, 4, 6, 1, 3, 5, 7, 0, 4, 2, 6, 3, 7, 1, 5, 0, 1, 2, 3, 4, 5, 6, 7, 7, 6, 5, 4, 3, 2, 1, 0, 0, 2, 4, 6, 1, 3, 5, 7, 0, 4, 2, 6, 3, 7, 1, 5];

            for (var i = 0; i < originalPattern.Length && i < normalNotes.Count; i++)
            {
                if (normalNotes[i].Column != originalPattern[i])
                    return false;
            }

            return true;
        });

        AddAssert("input maps scratch action to column 0", () =>
        {
            var variant = ((BmsBeatmap)Player.GameplayState.Beatmap).LayoutVariant;
            return BmsKeyBindingConfiguration.ActionToColumn(BmsAction.Scratch, variant) == 0;
        });

        AddAssert("input maps key actions to columns 1..N", () =>
        {
            var variant = ((BmsBeatmap)Player.GameplayState.Beatmap).LayoutVariant;
            return BmsKeyBindingConfiguration.ActionToColumn(BmsAction.Key1, variant) == 1
                   && BmsKeyBindingConfiguration.ActionToColumn(BmsAction.Key2, variant) == 2
                   && BmsKeyBindingConfiguration.ActionToColumn(BmsAction.Key3, variant) == 3
                   && BmsKeyBindingConfiguration.ActionToColumn(BmsAction.Key4, variant) == 4
                   && BmsKeyBindingConfiguration.ActionToColumn(BmsAction.Key5, variant) == 5;
        });
    }

    [Test]
    public void TestSecondPlayerWithAllMods()
    {
        this.AddSetupStep("2P+HS+MR mods", () => LoadPlayer([new BmsModSecondPlayer(), new BmsModHideScratch(), new BmsModMirror()]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());

        AddAssert("stage scratch on right", () => BmsLayout.Is2P(Playfield.LayoutVariant));

        AddAssert("scratch hidden", () => Playfield.Stage.Columns[0].Hidden);

        AddAssert("hit object columns mirrored with 2P variant", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            var normalNotes = beatmap.HitObjects
                .Where(h => h is not BmsLongNote && h is not BmsLandmine)
                .OrderBy(h => h.StartTime)
                .ToList();

            int[] originalPattern = [0, 2, 4, 6, 1, 3, 5, 7, 0, 4, 2, 6, 3, 7, 1, 5, 0, 1, 2, 3, 4, 5, 6, 7, 7, 6, 5, 4, 3, 2, 1, 0, 0, 2, 4, 6, 1, 3, 5, 7, 0, 4, 2, 6, 3, 7, 1, 5];
            int mirroredColumn(int col) => col switch { 0 => 0, 1 => 7, 2 => 6, 3 => 5, 4 => 4, 5 => 3, 6 => 2, 7 => 1, _ => col };
            var expectedColumns = originalPattern.Where(column => column != 0).Select(mirroredColumn);

            return normalNotes.Select(note => note.Column).SequenceEqual(expectedColumns);
        });
    }

    [Test]
    public void TestSecondPlayerWithAutoScratch()
    {
        this.AddSetupStep("2P+AS mods", () => LoadPlayer([new BmsModSecondPlayer(), new BmsModAutoScratch()]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());

        AddAssert("stage scratch on right", () => BmsLayout.Is2P(Playfield.LayoutVariant));

        AddAssert("scratch not hidden", () => !Playfield.Stage.Columns[0].Hidden);
        AddAssert("scratch column index 0 is scratch", () => Playfield.Stage.Columns[0].IsScratch);
    }

    [Test]
    public void TestSecondPlayerWithMirror()
    {
        this.AddSetupStep("2P+MR", () => LoadPlayer([new BmsModSecondPlayer(), new BmsModMirror()]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());

        AddAssert("stage scratch on right", () => BmsLayout.Is2P(Playfield.LayoutVariant));

        AddAssert("hit object columns mirrored with 2P variant", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            var normalNotes = beatmap.HitObjects
                .Where(h => h is not BmsLongNote && h is not BmsLandmine)
                .OrderBy(h => h.StartTime)
                .ToList();

            int[] originalPattern = [0, 2, 4, 6, 1, 3, 5, 7, 0, 4, 2, 6, 3, 7, 1, 5, 0, 1, 2, 3, 4, 5, 6, 7, 7, 6, 5, 4, 3, 2, 1, 0, 0, 2, 4, 6, 1, 3, 5, 7, 0, 4, 2, 6, 3, 7, 1, 5];
            int mirroredColumn(int col) => col switch { 0 => 0, 1 => 7, 2 => 6, 3 => 5, 4 => 4, 5 => 3, 6 => 2, 7 => 1, _ => col };

            for (var i = 0; i < originalPattern.Length && i < normalNotes.Count; i++)
            {
                if (normalNotes[i].Column != mirroredColumn(originalPattern[i]))
                    return false;
            }

            return true;
        });

        AddAssert("scratch column index 0 is scratch", () => Playfield.Stage.Columns[0].IsScratch);
    }
}
