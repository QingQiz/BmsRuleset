using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Input.Handlers;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;
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
        else if (gameplayState != null && gameplayClockContainer != null)
            ((BmsPlayfield)Playfield).RewindLiveInputs(gameplayState.Score.Replay, Mods, gameplayClockContainer.CurrentTime);
    }

    protected override ReplayInputHandler CreateReplayInputHandler(Replay replay)
    {
        var playfield = (BmsPlayfield)Playfield;
        var cadence = Mods.Any(mod => mod is BmsModManiaProfile { UseLazer.Value: true })
            ? new BmsManiaReplayCadence(Math.Min(GameplayStartTime, replay.Frames.Count > 0 ? replay.Frames.Min(frame => frame.Time) : 0))
            : (BmsManiaReplayCadence?)null;
        var handler = new BmsFramedReplayInputHandler(replay, playfield.CompleteAtEnd) { ManiaCadence = cadence };
        handler.ConfigureSeeking(() => gameplayClockContainer?.CurrentTime,
            (time, position) => seekReplay(replay, time, position),
            playfield.FinishReplaySeekInput);
        if (IsLoaded)
            handler.RequestSeek();
        return handler;
    }

    private bool seekReplay(Replay replay, double time, BmsReplayPosition? position)
    {
        resolveReplayHud();
        var counters = replayHud?.ChildrenOfType<GameplayAccuracyCounter>().ToArray() ?? [];
        // These native counters create transforms directly in their bindable callbacks.
        foreach (var counter in counters)
            counter.Current.UnbindBindings();
        try
        {
            return ((BmsPlayfield)Playfield).SeekReplay(replay, Mods, time, position, allowReplayFailure);
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
