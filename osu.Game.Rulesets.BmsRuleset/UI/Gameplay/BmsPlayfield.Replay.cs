using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Timing;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

public sealed partial class BmsPlayfield
{
    internal bool IsRebuildingReplay { get; private set; }

    private bool IsReplaySeekFrame => replaySeekTime == Time.Current;

    private double? replaySeekTime;
    private bool replayVisualsPending;
    private bool restoringReplayInput;

    internal bool SeekReplay(Replay replay, IReadOnlyList<Mod> mods, double time, Func<bool>? allowFailure = null)
    {
        if (scoreProcessor is not { } score || healthProcessor is not { } health)
            return false;

        var scoreClock = score.Clock;
        var healthClock = health.Clock;
        var manualClock = new ManualClock();
        var clock = new FramedClock(manualClock);
        var fromTime = double.IsFinite(judgementEngine.CurrentTime) && time > judgementEngine.CurrentTime
            ? judgementEngine.CurrentTime : (double?)null;
        using (new BmsReplayRebuildScope(score, health, allowFailure))
        {
            IsRebuildingReplay = true;
            try
            {
                score.Clock = health.Clock = clock;
                if (fromTime == null)
                {
                    score.ResetReplayState();
                    health.ResetReplayState();
                    foreach (var column in Stage.Columns.Cast<BmsColumn>())
                        column.JudgementBridge.ResetReplayResults();
                    initialiseJudgementEngine();
                }
                else
                {
                    foreach (var column in Stage.Columns.Cast<BmsColumn>())
                        column.JudgementBridge.PrepareReplayResults();
                }
                BmsReplaySimulation.AdvanceTo(Beatmap, replay, mods, judgementEngine, TotalColumns, time, currentTime =>
                {
                    manualClock.CurrentTime = currentTime;
                    clock.ProcessFrame();
                }, fromTime: fromTime);
            }
            finally
            {
                score.Clock = scoreClock;
                health.Clock = healthClock;
                IsRebuildingReplay = false;
            }
        }

        replaySeekTime = time;
        replayVisualsPending = true;
        // Empty replay handlers are inactive and cannot deliver the completion input.
        restoringReplayInput = replay.Frames.Count > 0;
        return true;
    }

    internal void FinishReplaySeekInput() => restoringReplayInput = false;

    internal void ApplyRebuiltResult(BmsJudgementResult result)
    {
        healthProcessor?.ApplyResult(result);
        scoreProcessor?.ApplyResult(result);
    }
}
