using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Input;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Input.Handlers;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Audio;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Mods;
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
    public const double MAX_TIME_RANGE = 11485;

    public override int Variant => (int)((BmsBeatmap)Beatmap).LayoutVariant;

    // Resolved from Player's DI cache — available after Player.LoadComplete registers them.
    [Resolved(CanBeNull = true)]
    private HealthProcessor? healthProcessor { get; set; }

    [Resolved(CanBeNull = true)]
    private GameplayState? gameplayState { get; set; }

    [Resolved(CanBeNull = true)]
    private ScoreManager? scoreManager { get; set; }

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        if (healthProcessor != null)
            healthProcessor.Failed -= onHealthFailed;
        base.Dispose(isDisposing);
    }

    #endregion

    public static double ComputeScrollTime(double scrollSpeed) => MAX_TIME_RANGE / Math.Max(1, scrollSpeed);

    public override DrawableHitObject<BmsHitObject>? CreateDrawableRepresentation(BmsHitObject h) => null;

    protected override Playfield CreatePlayfield()
    {
        var beatmap = (BmsBeatmap)Beatmap;
        return new BmsPlayfield(beatmap, Mods.OfType<BmsModAutoplay>().Any());
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (Config is BmsRulesetConfigManager config)
        {
            var speed = config.Get<double>(BmsRulesetSetting.ScrollSpeed);
            ((BmsPlayfield)Playfield).SetConfiguredScrollSpeed(speed);
        }

        // BMS convention: save every play to the local DB, including failed ones.
        // Player hard-codes SoloPlayer and has no Ruleset.CreatePlayer() hook, so we
        // hook the HealthProcessor.Failed event from inside DrawableRuleset instead.
        // We defer import by 500 ms so that Player.ConcludeFailedScore (which stamps
        // ScoreInfo.Rank = F) has already run by the time we read the score.
        if (healthProcessor != null && gameplayState != null && scoreManager != null && ReplayScore == null)
        {
            healthProcessor.Failed += onHealthFailed;
        }
    }

    protected override PassThroughInputManager CreateInputManager() => new BmsInputManager(Ruleset.RulesetInfo, Variant);

    protected override ReplayInputHandler CreateReplayInputHandler(Replay replay) => new BmsFramedReplayInputHandler(replay);

    protected override ReplayRecorder CreateReplayRecorder(Score score)
    {
        if (Beatmap is BmsBeatmap bmsBeatmap)
            BmsBranchReplayState.EnsureBranchReplayMod(score, bmsBeatmap.BranchDecisions);

        return new BmsReplayRecorder(score);
    }

    public override void SetReplayScore(Score replayScore)
    {
        base.SetReplayScore(replayScore);

        if (replayScore != null && Beatmap is BmsBeatmap bmsBeatmap)
            BmsBranchReplayState.EnsureBranchReplayMod(replayScore, bmsBeatmap.BranchDecisions);
    }

    private bool onHealthFailed()
    {
        // Do not block the fail — return true to allow it to proceed.
        // Defer import so ConcludeFailedScore (rank = F stamp) has run first.
        Scheduler.AddDelayed(() =>
        {
            if (gameplayState == null || scoreManager == null)
                return;

            var scoreCopy = gameplayState.Score.ScoreInfo.DeepClone();
            Task.Run(() => scoreManager.Import(scoreCopy))
                .ContinueWith(
                    t => Logger.Error(t.Exception, "BMS: failed to save failed score to database."),
                    TaskContinuationOptions.OnlyOnFaulted);
        }, 500);

        return true;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var beatmap = (BmsBeatmap)Beatmap;

        var events = beatmap.BackgroundSampleEvents
            .OrderBy(e => e.Time)
            .Where(e => beatmap.SampleDefinitions.ContainsKey(e.SampleKey))
            .Select(e => new BmsBackgroundAudioPlayer.BgmEvent(e.Time, e.SampleKey, new BmsSampleInfo(beatmap.SampleDefinitions[e.SampleKey])))
            .ToList();

        if (events.Count > 0)
            FrameStableComponents.Add(new BmsBackgroundAudioPlayer(events, IsPaused));
    }
}
