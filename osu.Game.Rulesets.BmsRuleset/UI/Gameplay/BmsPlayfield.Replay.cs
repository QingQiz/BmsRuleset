using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Timing;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.Mods;
using EngineTime = RhythmEngine.Core.Time;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

public sealed partial class BmsPlayfield
{
    internal bool IsRebuildingReplay { get; private set; }

    private bool isReplaySeekFrame => replaySeekTime == Time.Current;

    private double? replaySeekTime;
    private bool replayVisualsPending;
    private bool restoringReplayInput;

    internal void ResetReplayState()
    {
        scoreProcessor?.ResetReplayState();
        healthProcessor?.ResetReplayState();
        foreach (var column in Stage.Columns.Cast<BmsColumn>())
            column.JudgementBridge.ResetReplayResults();
        judgementEngine.Reset();
    }

    internal void RewindLiveInputs(Replay replay, IReadOnlyList<Mod> mods, double time)
    {
        if (time >= judgementEngine.CurrentTime || !double.IsFinite(judgementEngine.CurrentTime)) return;

        SeekReplay(replay, mods, time);
        // Live controls resume immediately; only a replay handler has a seek-completion callback.
        FinishReplaySeekInput();
    }

    internal bool SeekReplay(Replay replay, IReadOnlyList<Mod> mods, double time, BmsReplayPosition? position = null, Func<bool>? allowFailure = null)
    {
        if (scoreProcessor is not { } score || healthProcessor is not { } health)
            return false;

        var scoreClock = score.Clock;
        var healthClock = health.Clock;
        var manualClock = new ManualClock();
        var clock = new FramedClock(manualClock);
        // The view can be ahead of the last judgement step between two replay frames.
        var resume = position is { } current && time > current.Time && !judgementEngine.HasPendingInputs
                     && judgementEngine.CurrentTime <= EngineTime.ToMilliseconds(EngineTime.FromMilliseconds(current.Time))
            ? position
            : null;

        using (new BmsReplayRebuildScope(score, health, allowFailure))
        {
            IsRebuildingReplay = true;

            try
            {
                score.Clock = health.Clock = clock;

                if (resume == null)
                    ResetReplayState();
                else
                {
                    foreach (var column in Stage.Columns.Cast<BmsColumn>())
                        column.JudgementBridge.PrepareReplayResults();
                }

                BmsReplaySimulation.AdvanceTo(Beatmap, replay, mods, judgementEngine, TotalColumns, time, currentTime =>
                {
                    manualClock.CurrentTime = currentTime;
                    clock.ProcessFrame();
                }, resume: resume);
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
