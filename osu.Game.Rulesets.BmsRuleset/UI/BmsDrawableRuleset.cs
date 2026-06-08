using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Input;
using osu.Game.Beatmaps;
using osu.Game.Input.Handlers;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Audio;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.BmsRuleset.UI;

/// <inheritdoc />
/// <summary>
///     Minimal native BMS gameplay surface.
/// </summary>
/// <remarks>
///     This class is the first step away from the old mania-backed implementation. It deliberately
///     avoids any mania types and wires BMS hit objects into a native playfield. The visuals are simple
///     placeholders that currently use projected object time for vertical positioning.
/// </remarks>
public partial class BmsDrawableRuleset(Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod>? mods = null) : DrawableRuleset<BmsHitObject>(ruleset, beatmap, mods)
{
    public new PassThroughInputManager KeyBindingInputManager => base.KeyBindingInputManager;

    public const double MAX_TIME_RANGE = 11485;

    public override int Variant => (int)((BmsBeatmap)Beatmap).LayoutVariant;

    [Cached]
    private BmsSampleStore sampleStore = new(((BmsBeatmap)beatmap).SampleDefinitions.Values);

    // Resolved from Player's DI cache — available after Player.LoadComplete registers them.
    [Resolved(CanBeNull = true)]
    private HealthProcessor? healthProcessor { get; set; }

    [Resolved(CanBeNull = true)]
    private ScoreProcessor? scoreProcessor { get; set; }

    [Resolved(CanBeNull = true)]
    private GameplayState? gameplayState { get; set; }

    public static double ComputeScrollTime(double scrollSpeed) => MAX_TIME_RANGE / Math.Max(1, scrollSpeed);

    public override DrawableHitObject<BmsHitObject>? CreateDrawableRepresentation(BmsHitObject h) => null;

    public override void SetReplayScore(Score replayScore)
    {
        base.SetReplayScore(replayScore);

        if (Beatmap is BmsBeatmap bmsBeatmap)
            BmsBranchReplayState.EnsureBranchReplayMod(replayScore, bmsBeatmap.BranchDecisions);
    }

    protected override Playfield CreatePlayfield() => new BmsPlayfield((BmsBeatmap)Beatmap);

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (Config is BmsRulesetConfigManager config)
        {
            var speed = config.Get<double>(BmsRulesetSetting.ScrollSpeed);
            ((BmsPlayfield)Playfield).SetConfiguredScrollSpeed(speed);
        }

        // Subscribe to play completion for the end-of-song gauge check.
        // When health < 80% at song end, the rank must be F but the play should show
        // results normally (no fail animation).  This cannot live in RankFromScore
        // (which would lock the rank for subsequent accuracy updates) nor in
        // CheckDefaultFailCondition (which would trigger a gameplay fail).
        if (scoreProcessor != null && healthProcessor != null && gameplayState != null)
            scoreProcessor.HasCompleted.BindValueChanged(_ => onPlayCompleted());
    }

    protected override PassThroughInputManager CreateInputManager() => new BmsInputManager(Ruleset.RulesetInfo, Variant);

    protected override ReplayInputHandler CreateReplayInputHandler(Replay replay) => new BmsFramedReplayInputHandler(replay);

    protected override ReplayRecorder CreateReplayRecorder(Score score)
    {
        if (Beatmap is BmsBeatmap bmsBeatmap)
            BmsBranchReplayState.EnsureBranchReplayMod(score, bmsBeatmap.BranchDecisions);

        return new BmsReplayRecorder(score);
    }

    /// <summary>
    ///     Called when all hit objects have been judged (play completed).
    ///     If the final gauge is below the Normal-mode clear threshold (80 %),
    ///     stamps <see cref="ScoreRank.F"/> on the score without triggering a
    ///     gameplay fail (no fail animation, results screen shows normally with F rank).
    /// </summary>
    private void onPlayCompleted()
    {
        if (scoreProcessor == null || healthProcessor == null || gameplayState == null)
            return;

        if (healthProcessor.Health.Value >= 0.8)
            return;

        // FailScore sets rank.Value = ScoreRank.F and score.Passed = false directly,
        // bypassing RankFromScore so the updateRank guard never fires mid-play.
        scoreProcessor.FailScore(gameplayState.Score.ScoreInfo);
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var beatmap = (BmsBeatmap)Beatmap;

        // Add the shared sample cache to the tree so it async-loads (and pre-decodes every chart
        // sample) during the gameplay loading phase.
        FrameStableComponents.Add(sampleStore);

        var events = beatmap.BackgroundSampleEvents
            .OrderBy(e => e.Time)
            .Where(e => beatmap.SampleDefinitions.ContainsKey(e.SampleKey))
            .Select(e => new BmsBackgroundAudioPlayer.BgmEvent(e.Time, beatmap.SampleDefinitions[e.SampleKey]))
            .ToList();

        if (events.Count > 0)
            FrameStableComponents.Add(new BmsBackgroundAudioPlayer(events, IsPaused));
    }
}
