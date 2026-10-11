#nullable enable
using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsGitComparison
{
    private bool autoScratchMine = true;

    [Test]
    public void CaptureAutoScratch(
        [Values(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode noteMode,
        [Values(BmsLayoutVariant.Bme7K, BmsLayoutVariant.Bme7KDouble)] BmsLayoutVariant variant,
        [Values(false, true)] bool deferred,
        [Values(false, true)] bool reapply,
        [Values(false, true)] bool mine)
    {
        loadAutoScratchComparison(noteMode, variant, false, mine);
        AddStep("record automatic scratch at identical simulation times", () => withClock((advance, _) =>
        {
            using var observer = new BmsGitComparisonRecorder(Playfield, Player.ScoreProcessor, Player.HealthProcessor);
            if (deferred) Playfield.BeginGameplayFrame();
            foreach (var time in new[] { 2999d, 3000, 3001, 3002, 3003 })
                step(time);
            if (deferred) Playfield.EndGameplayFrame();
            observer.Mark("initial-traversal.complete");

            foreach (var time in new[] { 3201d, 3202, 3350, 3400, 3990, 4000, 4002, 4100, 4200, 4500, 6000 })
            {
                step(time);
                if (reapply && time == 3202)
                {
                    Playfield.Beatmap.HitObjects.OfType<BmsLongNote>().First().ApplyDefaults(Playfield.Beatmap.ControlPointInfo, Playfield.Beatmap.Difficulty);
                    observer.Mark("reapply");
                }
            }

            foreach (var time in new[] { 2998d, 3000, 3202, 4202 })
                step(time);
            observer.Save($"as-keyboard-{mode}-{layout}-deferred-{deferred}-reapply-{reapply}-mine-{mine}");

            void step(double time)
            {
                advance(time);
                inputManager.UpdateSubTree();
                observer.Mark("simulation");
            }
        }));
    }

    [Test]
    public void CaptureAutoScratchReplay(
        [Values(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode noteMode,
        [Values(BmsLayoutVariant.Bme7K, BmsLayoutVariant.Bme7KDouble)] BmsLayoutVariant variant,
        [Values(false, true)] bool mine)
    {
        loadAutoScratchComparison(noteMode, variant, true, mine);
        AddStep("record legacy AS replay catchup and rewind", () => captureReplay($"as-replay-{mode}-{layout}-mine-{mine}"));
    }

    private void loadAutoScratchComparison(BmsLongNoteMode noteMode, BmsLayoutVariant variant, bool useReplay, bool mine)
    {
        AddStep("load AS comparison chart", () =>
        {
            mode = noteMode;
            layout = variant;
            replay = useReplay;
            algorithm = BmsJudgementAlgorithm.Combo;
            skin = BmsTestSkins.SkinKind.Argon;
            autoScratchMine = mine;
            LoadPlayer([new BmsModAutoScratch()]);
        });
        AddUntilStep("AS player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        AddStep("freeze AS host clock", () =>
        {
            inputManager = Player.DrawableRuleset.ChildrenOfType<BmsInputManager>().Single(i => i.ChildrenOfType<BmsColumn>().Any());
            typeof(GameplayClockContainer).GetMethod("StopGameplayClock", instance)!.Invoke(Player.GameplayClockContainer, null);
            Player.GameplayClockContainer.Seek(2998);
        });
        AddUntilStep("AS simulation at start", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 2998) < .001);
    }
}
