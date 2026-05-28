using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
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
using osu.Game.Skinning;

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

    private readonly record struct BgmEvent(double Time, string SampleKey, BmsSampleInfo SampleInfo);

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

    protected override PassThroughInputManager CreateInputManager() => new BmsInputManager(Ruleset.RulesetInfo, Variant);

    protected override ReplayInputHandler CreateReplayInputHandler(Replay replay) => new BmsFramedReplayInputHandler(replay);

    protected override ReplayRecorder CreateReplayRecorder(Score score) => new BmsReplayRecorder(score);

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
            .Select(e => new BgmEvent(e.Time, e.SampleKey, new BmsSampleInfo(beatmap.SampleDefinitions[e.SampleKey])))
            .ToList();

        if (events.Count > 0)
            FrameStableComponents.Add(new BmsBackgroundAudioPlayer(events));
    }

    /// <summary>
    ///     Single component that drives ALL BGM auto-play events.
    ///     Replaces the old design of one <see cref="BmsChartSampleSound"/> per event,
    ///     which created thousands of <c>SkinReloadableDrawable</c> instances and
    ///     caused the async load to time out.
    /// </summary>
    private partial class BmsBackgroundAudioPlayer(IReadOnlyList<BgmEvent> sortedEvents) : SkinReloadableDrawable
    {
        /// <summary>Maximum age of a BGM event that will still be played on a normal (non-seek) frame.</summary>
        private const double allowable_late_start = 100;

        /// <summary>
        ///     On a seek (clock jumped by more than <see cref="allowable_late_start"/>), rewind
        ///     <see cref="nextIndex"/> this many ms before the target time so events near the
        ///     seek destination are replayed from their beginning (SampleChannel cannot seek).
        /// </summary>
        private const double seek_lookback = 3000;

        private readonly BindableBool isPaused = new();
        private readonly BindableDouble pauseFrequency = new(1);
        private readonly BindableDouble requestedVolume = new(1);

        // One ISample per unique sample key – resolved lazily on first play.
        // Null value means "looked up but not found in the beatmap skin".
        private readonly Dictionary<string, ISample?> samples = new();
        private readonly List<ActiveBgmChannel> activeChannels = new();

        private int nextIndex;
        private double previousTime;
        private bool hasSeenFrame;

        [Resolved(CanBeNull = true)]
        private AudioManager? audioManager { get; set; }

        #region Disposal

        protected override void Dispose(bool isDisposing)
        {
            stopAll();
            samples.Clear();
            base.Dispose(isDisposing);
        }

        #endregion

        protected override void SkinChanged(ISkinSource skin)
        {
            base.SkinChanged(skin);

            // Invalidate the sample cache so stale ISample references aren't used
            // after a skin change, but do NOT load any audio files here.
            // Actual audio loading is deferred to the first time each event fires.
            // This keeps SkinChanged() fast whether it is called on the async-load
            // thread or the update thread.
            samples.Clear();
        }

        protected override void LoadAsyncComplete()
        {
            base.LoadAsyncComplete();

            if (this.FindClosestParent<BmsDrawableRuleset>() is { } ruleset)
            {
                isPaused.BindTo(ruleset.IsPaused);
                isPaused.BindValueChanged(v =>
                {
                    if (v.NewValue)
                        pauseAll();
                    else
                        resumeAll();
                }, true);
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            LifetimeStart = double.MinValue;
            LifetimeEnd = double.MaxValue;
        }

        protected override void Update()
        {
            base.Update();

            if (!hasSeenFrame)
            {
                hasSeenFrame = true;
                previousTime = Time.Current;
            }

            double delta = Time.Current - previousTime;
            previousTime = Time.Current;

            // ── Seek detection ───────────────────────────────────────────────────────
            // Large delta means the clock jumped (intro skip, replay scrub, etc.).
            // Stop any in-flight channels and rewind nextIndex to SEEK_LOOKBACK before
            // the new position so events near the target are replayed from their start.
            bool seeked = Math.Abs(delta) > allowable_late_start;

            if (seeked)
            {
                stopAll();
                nextIndex = 0;

                while (nextIndex < sortedEvents.Count
                       && sortedEvents[nextIndex].Time < Time.Current - seek_lookback)
                    nextIndex++;
            }

            if (isPaused.Value)
                return;

            double tolerance = seeked ? seek_lookback : allowable_late_start;

            while (nextIndex < sortedEvents.Count)
            {
                var evt = sortedEvents[nextIndex];
                if (Time.Current < evt.Time) break;

                if (Time.Current - evt.Time < tolerance)
                    playEvent(evt);

                nextIndex++;
            }

            cleanupFinishedChannels();
        }

        private static LegacyBeatmapSkin? extractBeatmapSkin(ISkin skin) => skin switch
        {
            LegacyBeatmapSkin s => s,
            SkinTransformer t => t.Skin as LegacyBeatmapSkin,
            _ => null,
        };

        /// <summary>
        ///     Returns the <see cref="ISample"/> for <paramref name="evt"/>,
        ///     resolving it from the beatmap skin on first access and caching the result.
        /// </summary>
        private ISample? resolveSample(BgmEvent evt)
        {
            if (samples.TryGetValue(evt.SampleKey, out var cached))
                return cached;

            ISample? sample = null;

            foreach (var source in CurrentSkin.AllSources.Select(extractBeatmapSkin).Where(s => s != null))
            {
                sample = source!.GetSample(evt.SampleInfo);
                if (sample != null) break;
            }

            // Cache even if null so we don't retry a missing file every frame.
            samples[evt.SampleKey] = sample;
            return sample;
        }

        private void playEvent(BgmEvent evt)
        {
            var sample = resolveSample(evt);
            if (sample == null)
                return;

            var channel = sample.GetChannel();
            channel.ManualFree = true;
            channel.AddAdjustment(AdjustableProperty.Frequency, pauseFrequency);

            // Play() must be called before bindBgmVolumeAdjustments because Play() triggers
            // sample.AddItem(channel) → channel.BindAdjustments(sample), which would otherwise
            // re-add the VolumeSample chain that bindBgmVolumeAdjustments strips out.
            channel.Play();

            // 1. Synchronous cleanup — covers the case where all volumes are 1.0 and the
            //    aggregate never changes (handler below would never fire in that case, but a
            //    factor of 1.0 from VolumeSample is harmless then, so this is belt-and-braces).
            bindBgmVolumeAdjustments(channel);

            // 2. One-shot handler — fires when the audio-thread BindAdjustments call (enqueued
            //    by AudioCollectionManager.AddItem inside Play()) changes AggregateVolume.
            //    It unsubscribes itself and re-strips VolumeSample from the chain.
            Action<ValueChangedEvent<double>>? isolateOnBind = null;
            isolateOnBind = _ =>
            {
                channel.AggregateVolume.ValueChanged -= isolateOnBind!;
                bindBgmVolumeAdjustments(channel);
            };
            channel.AggregateVolume.ValueChanged += isolateOnBind;

            activeChannels.Add(new ActiveBgmChannel(channel));
        }

        /// <summary>
        ///     Strips all inherited volume adjustments (including the <c>VolumeSample</c> /
        ///     effects-volume adjustment inherited through the <c>SampleStore</c> → <c>Sample</c>
        ///     parent chain) and applies master × music-volume instead.
        /// </summary>
        /// <remarks>
        ///     Called immediately after <see cref="SampleChannel.Play"/> (synchronous pass) and
        ///     again via a one-shot <c>AggregateVolume.ValueChanged</c> handler (audio-thread pass)
        ///     to cover the race where <c>AudioCollectionManager.AddItem</c> re-adds <c>VolumeSample</c>
        ///     asynchronously after the synchronous cleanup.
        ///     BGM audio (BMS channel 01) should follow the music-volume slider, not
        ///     the effects/hitsound-volume slider.
        /// </remarks>
        private void bindBgmVolumeAdjustments(IAdjustableAudioComponent component)
        {
            // RemoveAllAdjustments strips every source from the aggregate (including the
            // parent-propagated VolumeSample) and re-adds only the component's own Volume.
            component.RemoveAllAdjustments(AdjustableProperty.Volume);
            component.AddAdjustment(AdjustableProperty.Volume, requestedVolume);

            if (audioManager != null)
            {
                component.AddAdjustment(AdjustableProperty.Volume, audioManager.Volume);      // master
                component.AddAdjustment(AdjustableProperty.Volume, audioManager.VolumeTrack); // music
            }
        }

        private void stopAll()
        {
            foreach (var ac in activeChannels)
            {
                if (!ac.Channel.IsDisposed)
                {
                    ac.Channel.Stop();
                    ac.Channel.Dispose();
                }
            }

            activeChannels.Clear();
            pauseFrequency.Value = 1;
        }

        private void pauseAll()
        {
            pauseFrequency.Value = 0;

            foreach (var ac in activeChannels)
                ac.Paused = true;
        }

        private void resumeAll()
        {
            pauseFrequency.Value = 1;

            foreach (var ac in activeChannels)
            {
                if (ac.Paused && !ac.Channel.IsDisposed)
                {
                    ac.Channel.Play();
                    ac.Paused = false;
                }
            }
        }

        private void cleanupFinishedChannels()
        {
            for (int i = activeChannels.Count - 1; i >= 0; i--)
            {
                var ac = activeChannels[i];

                if (ac.Channel.IsDisposed)
                {
                    activeChannels.RemoveAt(i);
                    continue;
                }

                if (ac.Paused)
                    continue;

                if (ac.Channel.Played && !ac.Channel.Playing)
                {
                    ac.Channel.Dispose();
                    activeChannels.RemoveAt(i);
                }
            }
        }

        private sealed class ActiveBgmChannel(SampleChannel channel)
        {
            public SampleChannel Channel { get; } = channel;

            public bool Paused { get; set; }
        }
    }
}
