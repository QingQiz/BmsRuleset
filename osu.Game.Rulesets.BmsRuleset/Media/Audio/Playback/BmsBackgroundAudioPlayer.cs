using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Audio;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Playback;

/// <summary>
///     Submits BMS background sample events to the shared PCM voice mixer.
/// </summary>
public partial class BmsBackgroundAudioPlayer(
    IReadOnlyList<BmsBackgroundAudioPlayer.BgmEvent> sortedEvents,
    Bindable<bool> sourcePaused)
    : Component
{
    public readonly record struct BgmEvent(double Time, ushort SampleKey, int Volume = 100);

    internal readonly record struct SeekedBgm(BgmEvent Event, double Offset);

    private const double allowable_late_start = 100;

    private readonly BindableBool sourceIsPaused = new();
    private readonly IBindable<bool> samplePlaybackDisabled = new BindableBool();

    private int nextIndex;
    private double previousTime;
    private double playbackBlockedAt;
    private bool hasSeenFrame;
    private bool playbackBlocked;
    private bool resyncRequired;

    [Resolved]
    private BmsSampleStore sampleStore { get; set; } = null!;

    protected override void Dispose(bool isDisposing)
    {
        sampleStore.StopAll();
        base.Dispose(isDisposing);
    }

    protected override void LoadAsyncComplete()
    {
        base.LoadAsyncComplete();

        sourceIsPaused.BindTo(sourcePaused);
        sourceIsPaused.BindValueChanged(_ => updatePlaybackBlocked(), true);
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

        if (playbackBlocked)
        {
            if (hasSeenFrame && Math.Abs(Time.Current - playbackBlockedAt) >= allowable_late_start)
                resyncRequired = true;

            previousTime = Time.Current;
            return;
        }

        if (!hasSeenFrame)
        {
            hasSeenFrame = true;
            previousTime = Time.Current;
            handleSeek(Time.Current);
            return;
        }

        var seeked = resyncRequired
                     || Math.Abs(Time.Current - previousTime) >= allowable_late_start
                     || (nextIndex < sortedEvents.Count && IsEventTooLateForDirectStart(sortedEvents[nextIndex].Time, Time.Current));
        previousTime = Time.Current;
        resyncRequired = false;

        if (seeked)
            handleSeek(Time.Current);

        while (nextIndex < sortedEvents.Count)
        {
            var evt = sortedEvents[nextIndex];

            // Undefined background keys are silent, but must not hold valid events at the same
            // timestamp behind the scheduling boundary.
            if (!sampleStore.HasSampleDefinition(evt.SampleKey))
            {
                nextIndex++;
                continue;
            }

            if (Time.Current < evt.Time)
                break;

            // Background samples and player-triggered keysounds from this ruleset update must
            // enter one mixer batch. Pre-scheduling BGM lets it reach the output buffer before a
            // simultaneous key press can be submitted, making the background layer sound early.
            if (Time.Current - evt.Time < allowable_late_start)
                sampleStore.QueueLivePlay(evt.SampleKey, evt.Volume);

            nextIndex++;
        }
    }

    [BackgroundDependencyLoader(true)]
    private void load(ISamplePlaybackDisabler? samplePlaybackDisabler)
    {
        if (samplePlaybackDisabler == null)
            return;

        samplePlaybackDisabled.BindTo(samplePlaybackDisabler.SamplePlaybackDisabled);
        samplePlaybackDisabled.BindValueChanged(_ => updatePlaybackBlocked(), true);
    }

    private void handleSeek(double currentTime)
    {
        sampleStore.StopAll();
        nextIndex = findFirstEventAfter(currentTime);

        foreach (var seeked in SelectEventsForSeek(
                     sortedEvents,
                     nextIndex,
                     currentTime,
                     sampleStore.MaxSampleLengthMilliseconds,
                     sampleStore.GetSampleLength))
        {
            sampleStore.Play(seeked.Event.SampleKey, seeked.Event.Volume, seeked.Offset);
        }
    }

    internal static IReadOnlyList<SeekedBgm> SelectEventsForSeek(
        IReadOnlyList<BgmEvent> events,
        int nextEventIndex,
        double currentTime,
        double maxLength,
        Func<ushort, double> getLength)
    {
        List<SeekedBgm> result = [];

        if (maxLength <= 0)
            return result;

        HashSet<ushort> seenKeys = [];

        for (var i = nextEventIndex - 1; i >= 0; i--)
        {
            var evt = events[i];
            var offset = currentTime - evt.Time;

            if (offset < 0)
                continue;

            if (offset >= maxLength)
                break;

            // A later trigger cuts the earlier instance even when the later instance has already
            // ended at the seek target, so older events for this definition must stay suppressed.
            if (!seenKeys.Add(evt.SampleKey))
                continue;

            if (ShouldResumeSampleAfterSeek(offset, getLength(evt.SampleKey)))
                result.Add(new SeekedBgm(evt, offset));
        }

        return result;
    }

    internal static bool ShouldResumeSampleAfterSeek(double offset, double length)
        => length > 0 && offset < length;

    internal static bool IsEventTooLateForDirectStart(double eventTime, double currentTime)
        => currentTime - eventTime >= allowable_late_start;

    private int findFirstEventAfter(double time)
    {
        var low = 0;
        var high = sortedEvents.Count;

        while (low < high)
        {
            var middle = low + (high - low) / 2;

            if (sortedEvents[middle].Time <= time)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private void updatePlaybackBlocked()
    {
        var blocked = sourceIsPaused.Value || samplePlaybackDisabled.Value;

        if (blocked == playbackBlocked)
            return;

        playbackBlocked = blocked;
        sampleStore.SetPlaybackBlocked(playbackBlocked);

        if (playbackBlocked)
        {
            playbackBlockedAt = Time.Current;

            // Future mixer commands need a fresh epoch after the gameplay clock stops advancing.
            if (sampleStore.SupportsScheduling)
                resyncRequired = true;

            return;
        }

        if (!hasSeenFrame)
            return;

        // Catch-up can advance the gameplay clock while samples are disabled. Reconstruct once
        // on the next update instead of briefly resuming voices from stale positions.
        if (resyncRequired || Math.Abs(Time.Current - playbackBlockedAt) >= allowable_late_start)
        {
            resyncRequired = true;
            return;
        }

        sampleStore.ResumeAll();
    }
}
