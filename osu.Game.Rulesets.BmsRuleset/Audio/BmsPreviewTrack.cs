using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Audio.Track;
using osu.Framework.IO.Stores;
using osu.Framework.Logging;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Audio;

/// <summary>
///     A <see cref="Track" /> that generates audio in real-time from BMS BGM events instead of
///     playing a pre-recorded file.  Each BGM sample is played from the chart's filesystem directory
///     at the correct moment using the track's virtual clock position.
/// </summary>
/// <remarks>
///     <para>
///         This track is used by <c>MusicController</c> during song select to provide a preview of
///         a BMS chart.  Because individual BGM samples are short keysounds, the preview plays them
///         in sequence according to the chart's timing, which gives a representative audio impression.
///     </para>
///     <para>
///         Sample resolution is filesystem-only (Tier 1 of <see cref="BmsSampleStore" /> semantics).
///         The chart directory path comes from <c>BeatmapInfo.Metadata.Source</c>, which the BMS
///         decoder sets to the original chart folder on disk.
///     </para>
///     <para>
///         Volume follows the same isolation as <see cref="BmsBackgroundAudioPlayer" />:
///         only the aggregate track volume chain applies — no effect volume.
///     </para>
/// </remarks>
public class BmsPreviewTrack : Track
{
    private readonly StopwatchClock clock = new StopwatchClock();
    private readonly List<BgmEvent> sortedEvents = [];
    private readonly ISampleStore? sampleStore;
    private readonly List<SampleChannel> activeChannels = [];

    private int nextEventIndex;
    private double seekOffset;

    /// <param name="bgmEvents">BGM events (channel #01) from the parsed BMS chart.</param>
    /// <param name="sampleDefinitions">Maps sample keys to filenames from the BMS chart.</param>
    /// <param name="basePath">
    ///     The chart directory on disk (from <c>BeatmapInfo.Metadata.Source</c>).
    ///     May be <c>null</c> in fully-imported mode (no filesystem fallback available).
    /// </param>
    /// <param name="audioManager">Framework audio manager, used to create a filesystem-backed sample store.</param>
    public BmsPreviewTrack(
        IReadOnlyList<BmsSampleEvent> bgmEvents,
        IReadOnlyDictionary<string, string> sampleDefinitions,
        string? basePath,
        AudioManager audioManager)
        : base("bms-preview")
    {
        // Resolve sample keys to filenames and sort by event time.
        foreach (var evt in bgmEvents)
        {
            if (sampleDefinitions.TryGetValue(evt.SampleKey, out var samplePath))
                sortedEvents.Add(new BgmEvent(evt.Time, samplePath));
        }

        sortedEvents.Sort((a, b) => a.Time.CompareTo(b.Time));

        // Set up a filesystem-backed sample store reading from the chart directory.
        if (basePath != null)
        {
            var fileResources = new ResourceStore<byte[]>(new BmsFileResourceStore(basePath));
            fileResources.AddExtension("wav");
            fileResources.AddExtension("mp3");
            fileResources.AddExtension("ogg");

            sampleStore = audioManager.GetSampleStore(fileResources);

            // Pre-warm the sample store (synchronous) so the first preview playback is smooth.
            foreach (var evt in sortedEvents)
            {
                var sample = sampleStore.Get(evt.SamplePath);
                // Sample is now cached in the store; discard the reference.
            }
        }

        // Length: 5 seconds after the last BGM event, minimum 30 s.
        double length = sortedEvents.Count > 0
            ? sortedEvents[^1].Time + 5000
            : 30000;
        Length = length;
    }

    public override bool IsRunning
    {
        get
        {
            lock (clock) return clock.IsRunning;
        }
    }

    public override double CurrentTime
    {
        get
        {
            lock (clock) return Math.Min(Length, seekOffset + clock.CurrentTime);
        }
    }

    public override void Start()
    {
        if (Length == 0 || CurrentTime >= Length)
            return;

        lock (clock) clock.Start();
    }

    public override void Stop()
    {
        lock (clock) clock.Stop();
        stopAllChannels();
    }

    public override bool Seek(double seek)
    {
        seekOffset = Math.Clamp(seek, 0, Length);

        bool success = seekOffset == seek;

        lock (clock)
        {
            if (success && IsRunning)
                clock.Restart();
            else
                clock.Reset();
        }

        nextEventIndex = findFirstEventAfter(seekOffset);
        stopAllChannels();

        return success;
    }

    public override Task<bool> SeekAsync(double seek) => Task.FromResult(Seek(seek));

    public override Task StartAsync()
    {
        Start();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        Stop();
        return Task.CompletedTask;
    }

    public override void Reset()
    {
        lock (clock) clock.Reset();
        seekOffset = 0;
        nextEventIndex = 0;
        stopAllChannels();

        base.Reset();
    }

    protected override void UpdateState()
    {
        base.UpdateState();

        lock (clock)
        {
            if (clock.IsRunning && CurrentTime >= Length)
            {
                if (Looping)
                    Restart();
                else
                {
                    Stop();
                    RaiseCompleted();
                }
            }
        }

        if (!IsRunning)
            return;

        double currentTime = CurrentTime;

        while (nextEventIndex < sortedEvents.Count)
        {
            var evt = sortedEvents[nextEventIndex];

            if (currentTime < evt.Time)
                break;

            playSample(evt);
            nextEventIndex++;
        }

        cleanupChannels();
    }

    private void playSample(BgmEvent evt)
    {
        if (sampleStore == null)
            return;

        var sample = sampleStore.Get(evt.SamplePath);

        if (sample == null)
            return;

        var channel = sample.GetChannel();
        channel.ManualFree = true;

        // Inherit the track's aggregate volume chain (master × music, no effect volume).
        channel.AddAdjustment(AdjustableProperty.Volume, AggregateVolume);

        channel.Play();
        activeChannels.Add(channel);
    }

    private void stopAllChannels()
    {
        foreach (var ch in activeChannels)
        {
            ch.Stop();
            ch.Dispose();
        }

        activeChannels.Clear();
    }

    private void cleanupChannels()
    {
        activeChannels.RemoveAll(ch => ch.IsDisposed || (ch.Played && !ch.Playing));
    }

    private int findFirstEventAfter(double time)
    {
        int low = 0;
        int high = sortedEvents.Count;

        while (low < high)
        {
            int middle = low + (high - low) / 2;

            if (sortedEvents[middle].Time <= time)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    protected override void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            Stop();
            sampleStore?.Dispose();
        }

        base.Dispose(disposing);
    }

    private readonly record struct BgmEvent(double Time, string SamplePath);
}
