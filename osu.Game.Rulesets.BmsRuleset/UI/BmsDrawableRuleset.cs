using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Input;
using osu.Game.Beatmaps;
using osu.Game.Input.Handlers;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Audio;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring;
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

    public override int Variant => (int)((BmsBeatmap)Beatmap).LayoutVariant;

    public string BeatmapSourceDirectory => getSource((BmsBeatmap)Beatmap);

    public BindableDouble BgaDim { get; } = new(0.7)
    {
        MinValue = 0,
        MaxValue = 1,
        Precision = 0.01,
    };

    // HUD components live in Player.HUDOverlay (a sibling of this DrawableRuleset under Player),
    // so they cannot resolve the local [Cached] above. They reach this instance via the
    // DrawableRuleset the Player caches for them — same cast pattern as BmsJudgementDisplay.
    public IBmsGameplayEvents GameplayEvents => gameplayEvents;

    [Cached(typeof(IBmsGameplayEvents))]
    private readonly BmsGameplayEvents gameplayEvents = new();

    private BmsPreviewTrack? previewTrackBeforePlay;

    [Cached]
    private BmsSampleStore sampleStore = new(
        ((BmsBeatmap)beatmap).SampleDefinitions.Values,
        getSource((BmsBeatmap)beatmap),
        getRate(mods)
    );

    // Resolved from Player's DI cache — available after Player.LoadComplete registers them.
    [Resolved(CanBeNull = true)]
    private HealthProcessor? healthProcessor { get; set; }

    [Resolved(CanBeNull = true)]
    private ScoreProcessor? scoreProcessor { get; set; }

    [Resolved(CanBeNull = true)]
    private GameplayState? gameplayState { get; set; }

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);

        if (!isDisposing || previewTrackBeforePlay == null)
            return;

        // Only restore if the preview track hasn't been replaced for a
        // different beatmap since play started.
        if (BmsWorkingBeatmap.ActivePreviewTrack == previewTrackBeforePlay)
            BmsWorkingBeatmap.RestoreActivePreview();

        previewTrackBeforePlay = null;
    }

    #endregion

    public static double ComputeScrollTime(double scrollSpeed) => BmsScrollController.ComputeScrollTime(scrollSpeed);

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
    ///     Resolve the chart directory path from the DB-backed beatmap (where the importer stored
    ///     it), bypassing the decoder's unconditional <c>Source = "BMS"</c> override.
    ///     <c>WorkingBeatmap.loadBeatmapAsync</c> copies <see cref="BeatmapInfo.BeatmapSet"/>
    ///     and <see cref="BeatmapInfo.ID"/> from the database but <b>not</b>
    ///     <see cref="BeatmapInfo.Metadata"/>, so the decoder override survives into gameplay.
    /// </summary>
    private static string getSource(BmsBeatmap b) =>
        b.BeatmapInfo.BeatmapSet?.Beatmaps.FirstOrDefault(b2 => b2.ID == b.BeatmapInfo.ID)
            ?.Metadata.Source ?? b.BeatmapInfo.Metadata.Source;

    /// <summary>
    ///     The active rate mod's SpeedChange (1.0 when no rate mod is selected). Used to pre-stretch
    ///     BMS samples at load so audio follows HT/DT pitch-preserving, in sync with the rate-scaled
    ///     chart clock.
    /// </summary>
    private static double getRate(IReadOnlyList<Mod>? mods)
    {
        var rateMod = mods?.OfType<ModRateAdjust>().FirstOrDefault();
        return rateMod?.SpeedChange.Value ?? 1.0;
    }

    /// <summary>
    ///     Called when all hit objects have been judged (play completed).
    ///     If the gauge is failed (HP ever hit 0, even under NF survival) or the final
    ///     HP is below the Normal-mode clear threshold (80 %), stamps
    ///     <see cref="ScoreRank.F"/> on the score without triggering a gameplay fail
    ///     (no fail animation, results screen shows normally with F rank).
    /// </summary>
    private void onPlayCompleted()
    {
        if (scoreProcessor == null || healthProcessor == null || gameplayState == null)
            return;

        if (healthProcessor is BmsHealthProcessor bmsHp)
        {
            var passed = bmsHp.HasPassedAtEnd();
            scoreProcessor.PopulateScore(gameplayState.Score.ScoreInfo);

            if (!passed)
                scoreProcessor.FailScore(gameplayState.Score.ScoreInfo);

            return;
        }

        if (healthProcessor.Health.Value < 0.8)
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
            .Select(e => new BmsBackgroundAudioPlayer.BgmEvent(e.Time, beatmap.SampleDefinitions[e.SampleKey], e.Volume))
            .ToList();

        if (events.Count > 0)
            FrameStableComponents.Add(new BmsBackgroundAudioPlayer(events, IsPaused, getRate(Mods)));

        if (Config is BmsRulesetConfigManager config)
        {
            config.BindWith(BmsRulesetSetting.BgaDim, BgaDim);
            ((BmsPlayfield)Playfield).ScrollController.SetConfiguredScrollSpeed(config.Get<double>(BmsRulesetSetting.ScrollSpeed));
        }

        previewTrackBeforePlay = BmsWorkingBeatmap.ActivePreviewTrack;
        BmsWorkingBeatmap.StopActivePreview();
    }
}
