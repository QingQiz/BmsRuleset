using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Input;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

public partial class BmsDrawableRuleset : DrawableRuleset<BmsHitObject>
{
    public BmsDrawableRuleset(Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod>? mods = null)
        : base(ruleset, beatmap, mods)
    {
    }

    protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
    {
        var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
        // Player compiles after constructing the drawable ruleset. Standalone previews have no processor.
        var compiled = (parent.Get<ScoreProcessor>() as BmsScoreProcessor)?.EngineChart
                       ?? new BmsEngineChart((BmsBeatmap)Beatmap, GameplayProfile.FromMods(Mods));
        dependencies.CacheAs(compiled);
        audioController = createGameplayAudioController((BmsBeatmap)Beatmap, Mods, compiled);
        dependencies.CacheAs(audioController.SamplePlayback);
        return dependencies;
    }

    internal BmsStageHudController StageHudController => field ??= new BmsStageHudController((BmsPlayfield)Playfield);

    internal BmsSamplePlayback SamplePlayback => audioController!.SamplePlayback;

    internal BindableBool BackgroundAudioPaused => audioController!.BackgroundAudioPaused;

    public new PassThroughInputManager KeyBindingInputManager => base.KeyBindingInputManager;

    public override int Variant => (int)((BmsBeatmap)Beatmap).LayoutVariant;

    public string BeatmapSourceDirectory => BmsGameplayAudioController.ResolveBeatmapSource((BmsBeatmap)Beatmap);

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

    private BmsGameplayAudioController? audioController;
    private BmsGameplayCompletionController? completionController;
    private BmsGameplaySettingsController? settingsController;
    private double previousVisualUpdateMs;

    // Resolved from Player's DI cache — available after Player.LoadComplete registers them.
    [Resolved(CanBeNull = true)]
    private HealthProcessor? healthProcessor { get; set; }

    [Resolved(CanBeNull = true)]
    private ScoreProcessor? scoreProcessor { get; set; }

    [Resolved(CanBeNull = true)]
    private GameplayState? gameplayState { get; set; }

    [Resolved(CanBeNull = true)]
    private GameplayClockContainer? gameplayClockContainer { get; set; }

    [Resolved(CanBeNull = true)]
    private BmsPracticeSession? practiceSession { get; set; }

    [Resolved]
    private GameHost host { get; set; } = null!;

    [Resolved]
    private FrameworkConfigManager frameworkConfig { get; set; } = null!;

    protected override void Dispose(bool isDisposing)
    {
        var previewRestoreTime = isDisposing ? gameplayClockContainer?.CurrentTime : null;

        base.Dispose(isDisposing);

        if (!isDisposing)
            return;

        completionController?.Dispose();
        completionController = null;
        settingsController?.Dispose();
        settingsController = null;
        if (gameplayClockContainer != null)
            gameplayClockContainer.OnSeek -= onReplaySeek;
        gameplayEvents.ReplaySeeked -= synchroniseReplayState;
        audioController?.Dispose(previewRestoreTime, restorePreview: practiceSession == null);
    }

    public static double ComputeScrollTime(double scrollSpeed) => BmsGameplayScrollController.ComputeScrollTime(scrollSpeed);

    public override DrawableHitObject<BmsHitObject>? CreateDrawableRepresentation(BmsHitObject h) => null;

    public override void SetReplayScore(Score? replayScore)
    {
        if (IsLoaded && replayScore != null)
            ((BmsPlayfield)Playfield).ResetReplayState();
        base.SetReplayScore(replayScore);

        updateJudgementAlgorithm();
        if (IsLoaded) restoreReplayHealth();

        if (replayScore != null && Beatmap is BmsBeatmap bmsBeatmap)
            BmsBranchReplayState.EnsureBranchReplayMod(replayScore, bmsBeatmap.BranchDecisions);
    }

    private void updateJudgementAlgorithm()
    {
        // Snapshot once per play; a live config binding would make recorded inputs ambiguous.
        ((BmsPlayfield)Playfield).JudgementAlgorithm = ReplayScore != null
            ? BmsReplayFrame.GetAlgorithm(ReplayScore.Replay)
            : (Config as BmsRulesetConfigManager)?.Get<BmsJudgementAlgorithm>(BmsRulesetSetting.JudgementAlgorithm) ?? BmsJudgementAlgorithm.Combo;
    }

    private void restoreReplayHealth()
    {
        if (ReplayScore != null && healthProcessor is BmsHealthProcessor health)
        {
            var mods = ReplayScore.ScoreInfo.Mods;
            health.ApplyReplayMods(mods);
            if (mods.OfType<BmsModAutoGauge>().FirstOrDefault() is { } autoGauge)
                autoGauge.ApplyToHealthProcessor(health);
            else
                foreach (var mod in mods.OfType<BmsModGauge>())
                    mod.ApplyToHealthProcessor(health);
        }
    }

    protected override Playfield CreatePlayfield()
    {
        // Replay setup can create the playfield before Player publishes its compiled chart.
        return new BmsPlayfield((BmsBeatmap)Beatmap, () => Dependencies.Get<BmsEngineChart>());
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        restoreReplayHealth();
        gameplayEvents.ReplaySeeked += synchroniseReplayState;
        if (gameplayClockContainer != null)
            gameplayClockContainer.OnSeek += onReplaySeek;

        audioController!.BindPauseSource(gameplayClockContainer?.IsPaused ?? IsPaused);

        if (practiceSession == null && scoreProcessor != null && healthProcessor != null && gameplayState != null)
            completionController = new BmsGameplayCompletionController(
                scoreProcessor,
                healthProcessor,
                gameplayState,
                ReplayScore,
                Config as BmsRulesetConfigManager);
    }

    protected override void Update()
    {
        base.Update();

        // Offset/source-clock changes can move live play backwards without raising OnSeek.
        if (ReplayScore == null && gameplayState != null && gameplayClockContainer != null)
            ((BmsPlayfield)Playfield).RewindLiveInputs(gameplayState.Score.Replay, Mods, gameplayClockContainer.CurrentTime);

        audioController!.Update();
        SamplePlayback.BeginGameplayFrame();
        ((BmsPlayfield)Playfield).BeginGameplayFrame();
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();

        // A ten-millisecond replay slice cannot recover if each deferred visual pass costs
        // much longer. Give simulation a bounded share of that cost before visiting skins again.
        var catchUpStart = Stopwatch.GetTimestamp();
        var catchUpBudget = Math.Min(50, previousVisualUpdateMs);

        while (ReplayScore != null && FrameStableClock is FrameStabilityContainer { IsRunning: true, IsRewinding: false } stability
                                   && FrameStableClock.IsCatchingUp.Value && ((BmsPlayfield)Playfield).HasPendingVisualUpdate
                                   && Stopwatch.GetElapsedTime(catchUpStart).TotalMilliseconds < catchUpBudget)
        {
            var previousTime = stability.CurrentTime;
            stability.UpdateSubTree();
            if (stability.CurrentTime <= previousTime)
                break;
        }

        var visualStart = Stopwatch.GetTimestamp();
        ((BmsPlayfield)Playfield).EndGameplayFrame();
        previousVisualUpdateMs = Stopwatch.GetElapsedTime(visualStart).TotalMilliseconds;
        SamplePlayback.EndGameplayFrame();

        // Columns receive input independently while the playfield updates. Submitting here preserves
        // one mixer target for every keysound produced by the same ruleset update.
        audioController!.SubmitLivePlayBatch();
    }

    protected override PassThroughInputManager CreateInputManager() => new BmsInputManager(Ruleset.RulesetInfo, Variant);

    protected override ResumeOverlay CreateResumeOverlay() => new DelayedResumeOverlay();

    [BackgroundDependencyLoader]
    private void load()
    {
        var beatmap = (BmsBeatmap)Beatmap;
        if (healthProcessor is BmsHealthProcessor health)
            health.Mods = Mods;

        updateJudgementAlgorithm();

        Overlays.Add(StageHudController);
        ((BmsInputManager)KeyBindingInputManager).ConfigureInputMods(Mods);

        // Sample playback follows the gameplay clock to load PCM samples before their first use.
        FrameStableComponents.Add(SamplePlayback);

        // This component also coordinates pause/seek blocking for KeySounds in the shared Track
        // playback component, so it must exist even when the chart has no background sample events.
        FrameStableComponents.Add(audioController!.CreateBackgroundAudioPlayer(beatmap));
        settingsController = new BmsGameplaySettingsController(
            Config as BmsRulesetConfigManager,
            (BmsPlayfield)Playfield,
            BgaDim,
            BmsGameplayAudioController.GetPlaybackRate(Mods),
            host,
            frameworkConfig);
    }
}
