using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Input;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Input.Handlers;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.BmsRuleset.UI;

public partial class BmsDrawableRuleset : DrawableRuleset<BmsHitObject>
{
    public BmsDrawableRuleset(Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod>? mods = null)
        : base(ruleset, beatmap, mods)
    {
        var bmsBeatmap = (BmsBeatmap)beatmap;
        audioController = new BmsGameplayAudioController(bmsBeatmap, Mods);
        samplePlayback = audioController.SamplePlayback;
    }

    internal BmsStageHudController StageHudController => field ??= new BmsStageHudController((BmsPlayfield)Playfield);

    internal BmsSamplePlayback SamplePlayback => samplePlayback;

    internal BindableBool BackgroundAudioPaused => audioController.BackgroundAudioPaused;

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

    private readonly BmsGameplayAudioController audioController;
    private BmsGameplayPauseController? pauseController;
    private BmsGameplayCompletionController? completionController;
    private BmsGameplaySettingsController? settingsController;

    [Cached]
    private readonly BmsSamplePlayback samplePlayback;

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
    private Player? player { get; set; }

    [Resolved]
    private GameHost host { get; set; } = null!;

    [Resolved]
    private FrameworkConfigManager frameworkConfig { get; set; } = null!;

    protected override void Dispose(bool isDisposing)
    {
        BmsJudgementProfileProvider.ClearActiveWindowMods(windowMods);

        var previewRestoreTime = isDisposing ? gameplayClockContainer?.CurrentTime : null;

        base.Dispose(isDisposing);

        if (!isDisposing)
            return;

        pauseController?.Dispose();
        pauseController = null;
        completionController?.Dispose();
        completionController = null;
        settingsController?.Dispose();
        settingsController = null;
        audioController.Dispose(previewRestoreTime);
    }

    public static double ComputeScrollTime(double scrollSpeed) => BmsGameplayScrollController.ComputeScrollTime(scrollSpeed);

    public override DrawableHitObject<BmsHitObject>? CreateDrawableRepresentation(BmsHitObject h) => null;

    public override void SetReplayScore(Score replayScore)
    {
        base.SetReplayScore(replayScore);

        if (Beatmap is BmsBeatmap bmsBeatmap)
            BmsBranchReplayState.EnsureBranchReplayMod(replayScore, bmsBeatmap.BranchDecisions);
    }

    private IReadOnlyList<IApplicableToJudgementWindow> windowMods = [];

    protected override Playfield CreatePlayfield()
    {
        windowMods = Mods.OfType<IApplicableToJudgementWindow>().ToArray();
        BmsJudgementProfileProvider.SetActiveWindowMods(windowMods);

        return new BmsPlayfield((BmsBeatmap)Beatmap);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (gameplayClockContainer != null)
        {
            audioController.BindPauseSource(gameplayClockContainer.IsPaused);
            pauseController = new BmsGameplayPauseController(
                this,
                (BmsPlayfield)Playfield,
                gameplayClockContainer,
                gameplayState,
                player,
                audioController.StopPreviewForGameplay,
                action => SchedulerAfterChildren.Add(action));
        }
        else
            audioController.BindPauseSource(IsPaused);

        if (scoreProcessor != null && healthProcessor != null && gameplayState != null)
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

        audioController.Update();
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();

        // Columns receive input independently while the playfield updates. Submitting here preserves
        // one mixer target for every keysound produced by the same ruleset update.
        audioController.SubmitLivePlayBatch();
    }

    protected override PassThroughInputManager CreateInputManager() => new BmsInputManager(Ruleset.RulesetInfo, Variant);

    protected override ReplayInputHandler CreateReplayInputHandler(Replay replay) => new BmsFramedReplayInputHandler(replay);

    protected override ReplayRecorder CreateReplayRecorder(Score score)
    {
        if (Beatmap is BmsBeatmap bmsBeatmap)
            BmsBranchReplayState.EnsureBranchReplayMod(score, bmsBeatmap.BranchDecisions);

        return new BmsReplayRecorder(score);
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var beatmap = (BmsBeatmap)Beatmap;

        Overlays.Add(StageHudController);

        // Sample playback follows the gameplay clock to load PCM samples before their first use.
        FrameStableComponents.Add(samplePlayback);

        // This component also coordinates pause/seek blocking for KeySounds in the shared Track
        // playback component, so it must exist even when the chart has no background sample events.
        FrameStableComponents.Add(audioController.CreateBackgroundAudioPlayer(beatmap));
        settingsController = new BmsGameplaySettingsController(
            Config as BmsRulesetConfigManager,
            (BmsPlayfield)Playfield,
            BgaDim,
            BmsGameplayAudioController.GetPlaybackRate(Mods),
            host,
            frameworkConfig);
    }
}
