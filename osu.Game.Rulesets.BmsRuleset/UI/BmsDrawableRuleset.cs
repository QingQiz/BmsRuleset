using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
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

    private readonly BindableDouble configScrollSpeed = new(8);

    public static double ComputeScrollTime(double scrollSpeed) => MAX_TIME_RANGE / Math.Max(1, scrollSpeed);

    public override DrawableHitObject<BmsHitObject>? CreateDrawableRepresentation(BmsHitObject h) => null;

    // Resolved from Player's DI cache — available after Player.LoadComplete registers them.
    [Resolved(CanBeNull = true)]
    private HealthProcessor? healthProcessor { get; set; }

    [Resolved(CanBeNull = true)]
    private GameplayState? gameplayState { get; set; }

    [Resolved(CanBeNull = true)]
    private ScoreManager? scoreManager { get; set; }

    protected override Playfield CreatePlayfield()
    {
        var beatmap = (BmsBeatmap)Beatmap;
        return new BmsPlayfield(beatmap.HitObjects, beatmap.TotalColumns, beatmap.LayoutVariant, Mods.OfType<BmsModAutoplay>().Any());
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (Config is BmsRulesetConfigManager config)
            config.BindWith(BmsRulesetSetting.ScrollSpeed, configScrollSpeed);

        configScrollSpeed.BindValueChanged(speed => ((BmsPlayfield)Playfield).TimeRange = ComputeScrollTime(speed.NewValue), true);

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

    protected override void Dispose(bool isDisposing)
    {
        if (healthProcessor != null)
            healthProcessor.Failed -= onHealthFailed;
        base.Dispose(isDisposing);
    }

    protected override PassThroughInputManager CreateInputManager() => new BmsInputManager(Ruleset.RulesetInfo, Variant);

    protected override ReplayInputHandler CreateReplayInputHandler(Replay replay) => new BmsFramedReplayInputHandler(replay);

    protected override ReplayRecorder CreateReplayRecorder(Score score) => new BmsReplayRecorder(score);

    [BackgroundDependencyLoader]
    private void load()
    {
        var beatmap = (BmsBeatmap)Beatmap;

        foreach (var sampleEvent in beatmap.BackgroundSampleEvents.OrderBy(e => e.Time))
        {
            if (beatmap.SampleDefinitions.TryGetValue(sampleEvent.SampleKey, out var samplePath))
                FrameStableComponents.Add(new BmsBackgroundSample(sampleEvent.Time, new BmsSampleInfo(samplePath)));
        }
    }

    private partial class BmsBackgroundSample(double startTime, BmsSampleInfo sampleInfo) : BmsChartSampleSound(sampleInfo)
    {
        private const double allowable_late_start = 100;

        private readonly BindableBool isPaused = new();

        private bool hasSeenClockFrame;
        private double previousTime;

        protected override void LoadAsyncComplete()
        {
            base.LoadAsyncComplete();

            if (this.FindClosestParent<BmsDrawableRuleset>() is { } ruleset)
            {
                isPaused.BindTo(ruleset.IsPaused);
                isPaused.BindValueChanged(paused =>
                {
                    if (paused.NewValue)
                        Pause();
                    else
                        Resume();
                }, true);
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            LifetimeStart = startTime;
            LifetimeEnd = double.MaxValue;
        }

        protected override void Update()
        {
            base.Update();

            if (!hasSeenClockFrame)
            {
                hasSeenClockFrame = true;
                // Initialise to current time so the very first frame does not
                // produce a spurious clock-jump when the player starts mid-song.
                previousTime = Time.Current;
            }

            var clockJumped = Time.Current - previousTime > allowable_late_start;

            if (Time.Current < startTime)
            {
                if (!isPaused.Value)
                    Stop();
            }
            else if (clockJumped && HasActiveChannels)
            {
                // The clock jumped forward (e.g. skip button) while this sample
                // was playing.  Stop it so it doesn't continue from the wrong
                // position.  Do NOT re-trigger it: the sample's window has passed.
                Stop();
            }
            else if (!isPaused.Value && !RequestedPlaying)
            {
                // Only play within the 100 ms window after startTime.
                // Do NOT use clockJumped here – a forward skip should never
                // restart a sample that was already in the past.
                if (Time.Current - startTime < allowable_late_start)
                    Play();
            }

            previousTime = Time.Current;

            LifetimeStart = double.MinValue;
            LifetimeEnd = (RequestedPlaying || HasActiveChannels || Time.Current < startTime + allowable_late_start)
                ? double.MaxValue
                : startTime;
        }
    }
}
