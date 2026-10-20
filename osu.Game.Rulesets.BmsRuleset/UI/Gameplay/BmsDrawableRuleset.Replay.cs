using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Input.Handlers;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.HUD;
using osu.Game.Screens.Play.HUD.JudgementCounter;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

public partial class BmsDrawableRuleset
{
    private Player? replayPlayer;
    private HUDOverlay? replayHud;

    private void onReplaySeek()
    {
        if (FrameStableClock is FrameStabilityContainer { ReplayInputHandler: BmsFramedReplayInputHandler handler })
            handler.RequestSeek();
    }

    private void configureReplayClock()
    {
        if (FrameStableClock is not FrameStabilityContainer stability)
            return;

        ref var reference = ref frameStabilityReferenceClock(stability);
        if (reference == null)
            return;
        if (reference is StreamingReplayClock previous)
            reference = previous.Source;
        if (ReplayScore?.Replay is { HasReceivedAllFrames: false } replay)
            reference = new StreamingReplayClock(reference, replay, stability.CurrentTime);
    }

    // Frame stability updates its exposed clock even when the replay returns no usable time.
    // Bound its reference so waiting for streamed input cannot advance visuals beyond known state.
    private sealed class StreamingReplayClock(IClock source, Replay replay, double emptyTime) : IClock
    {
        internal IClock Source { get; } = source;

        public double CurrentTime => replay.HasReceivedAllFrames ? Source.CurrentTime
            : Math.Min(Source.CurrentTime, replay.Frames.Count > 0 ? replay.Frames[^1].Time : emptyTime);

        public double Rate => Source.Rate;

        public bool IsRunning => Source.IsRunning;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "referenceClock")]
    private static extern ref IClock frameStabilityReferenceClock(FrameStabilityContainer stability);

    protected override ReplayInputHandler CreateReplayInputHandler(Replay replay)
    {
        var playfield = (BmsPlayfield)Playfield;
        var handler = new BmsFramedReplayInputHandler(replay, playfield.CompleteAtEnd);
        handler.ConfigureSeeking(() => gameplayClockContainer?.CurrentTime,
            time => seekReplay(replay, time),
            playfield.FinishReplaySeekInput);
        return handler;
    }

    private bool seekReplay(Replay replay, double time)
    {
        resolveReplayHud();
        var counters = replayHud?.ChildrenOfType<GameplayAccuracyCounter>().ToArray() ?? [];
        // These native counters create transforms directly in their bindable callbacks.
        foreach (var counter in counters)
            counter.Current.UnbindBindings();
        try
        {
            return ((BmsPlayfield)Playfield).SeekReplay(replay, Mods, time, allowReplayFailure);
        }
        finally
        {
            foreach (var counter in counters)
            {
                counter.AccuracyDisplay.TriggerChange();
                counter.StopRolling();
            }
        }
    }

    private void resolveReplayHud()
    {
        // Player builds its sibling HUD after loading the ruleset, so resolve it at the first seek.
        replayPlayer ??= this.FindClosestParent<Player>();
        replayHud ??= replayPlayer?.ChildrenOfType<HUDOverlay>().FirstOrDefault();
    }

    private bool allowReplayFailure()
    {
        replayPlayer ??= this.FindClosestParent<Player>();
        return replayPlayer == null || checkModsAllowFailure(replayPlayer);
    }

    private void synchroniseReplayState()
    {
        if (scoreProcessor == null)
            return;

        if (gameplayState != null)
        {
            if (healthProcessor?.HasFailed == true)
                scoreProcessor.FailScore(gameplayState.Score.ScoreInfo);
            else
                scoreProcessor.PopulateScore(gameplayState.Score.ScoreInfo);
        }

        resolveReplayHud();
        if (replayHud == null)
            return;

        // Native counters normally accumulate notifications; reconstruction publishes no intermediate judgements.
        foreach (var controller in replayHud.ChildrenOfType<JudgementCountController>())
            foreach (var count in controller.Counters)
                count.ResultCount.Value = count.Types.Sum(type => scoreProcessor.Statistics.GetValueOrDefault(type));
        foreach (var counter in replayHud.ChildrenOfType<BmsScoreCounter>())
            counter.StopRolling();
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "CheckModsAllowFailure")]
    private static extern bool checkModsAllowFailure(Player player);

    protected override ReplayRecorder CreateReplayRecorder(Score score)
    {
        if (Beatmap is BmsBeatmap bmsBeatmap)
            BmsBranchReplayState.EnsureBranchReplayMod(score, bmsBeatmap.BranchDecisions);

        return new BmsReplayRecorder(score, ((BmsPlayfield)Playfield).JudgementAlgorithm ?? BmsJudgementAlgorithm.Combo);
    }
}
