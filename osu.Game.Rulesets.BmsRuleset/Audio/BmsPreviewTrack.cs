using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.IO.Stores;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Audio;

public class BmsPreviewTrack : Track
{
    public override bool IsRunning
    {
        get
        {
            lock (clock) return clock.IsRunning;
        }
    }

    public override bool IsDummyDevice => false;

    public override double CurrentTime
    {
        get
        {
            lock (clock) return Math.Min(Length, seekOffset + clock.CurrentTime);
        }
    }

    /// <summary>
    ///     When set, <see cref="UpdateState" /> skips BGM event processing so that
    ///     gameplay audio (driven by <see cref="BmsBackgroundAudioPlayer" />) is
    ///     the only source.  The track still provides clock timing for the
    ///     <see cref="osu.Game.Screens.Play.MasterGameplayClockContainer" />.
    /// </summary>
    public bool SuppressEventProcessing { get; set; }

    private readonly StopwatchClock clock = new();
    private readonly List<BgmEvent> sortedEvents = [];
    private readonly ISampleStore? sampleStore;
    private readonly ISample? previewSample;
    private readonly Dictionary<string, ISample?> resolvedSamples = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ActiveBgm> activeChannels = [];

    private readonly record struct BgmEvent(double Time, string SamplePath);

    private SampleChannel? previewChannel;

    private int nextEventIndex;
    private double seekOffset;

    /// <param name="bgmEvents">BGM events (channel #01) from the parsed BMS chart.</param>
    /// <param name="sampleDefinitions">Maps sample keys to filenames from the BMS chart.</param>
    /// <param name="basePath">
    ///     The chart directory on disk (from <c>BeatmapInfo.Metadata.Source</c>).
    ///     May be <c>null</c> in fully-imported mode (no filesystem fallback available).
    /// </param>
    /// <param name="audioManager">Framework audio manager, used to create filesystem-backed audio stores.</param>
    /// <param name="previewFile"></param>
    public BmsPreviewTrack(
        IReadOnlyList<BmsSampleEvent> bgmEvents,
        IReadOnlyDictionary<ushort, string> sampleDefinitions,
        string? basePath,
        AudioManager audioManager,
        string? previewFile = null)
        : base("bms-preview")
    {
        // Propagate the track's aggregate rate (populated by AdjustmentsFromMods in gameplay,
        // or by MusicController mod-track-adjustments elsewhere) into the inner StopwatchClock.
        // BmsPreviewTrack.CurrentTime is driven by this StopwatchClock, not by Track.Rate, so
        // without this the gameplay clock would not advance at the mod rate in the
        // DecouplingFramedClock's source-running branch.
        AggregateFrequency.ValueChanged += _ => updateClockRate();
        AggregateTempo.ValueChanged += _ => updateClockRate();
        updateClockRate();

        if (basePath == null) return;

        var fileResources = new ResourceStore<byte[]>(new BmsFileResourceStore(basePath));
        fileResources.AddExtension("wav");
        fileResources.AddExtension("mp3");
        fileResources.AddExtension("ogg");

        sampleStore = audioManager.GetSampleStore(fileResources);

        foreach (var candidate in getPreviewCandidates(basePath, previewFile))
        {
            previewSample = resolveSample(candidate);

            if (previewSample == null)
                continue;

            break;
        }

        if (previewSample == null)
        {
            foreach (var evt in bgmEvents)
            {
                if (sampleDefinitions.TryGetValue(evt.SampleKey, out var samplePath))
                    sortedEvents.Add(new BgmEvent(evt.Time, samplePath));
            }

            sortedEvents.Sort((a, b) => a.Time.CompareTo(b.Time));
            var length = sortedEvents.Count > 0
                ? sortedEvents[^1].Time + 5000
                : 30000;
            Length = length;
        }
        else
            Length = 30000;
    }

    #region Disposal

    protected override void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            Stop();
            sampleStore?.Dispose();
        }

        base.Dispose(disposing);
    }

    #endregion

    public override void Start()
    {
        if (Length == 0 || CurrentTime >= Length)
            return;

        if (!SuppressEventProcessing)
            startPreviewChannel();
        lock (clock) clock.Start();
    }

    public override void Stop()
    {
        lock (clock) clock.Stop();
        stopAllChannels();
        stopPreviewChannel();
    }

    public override bool Seek(double seek)
    {
        seekOffset = Math.Clamp(seek, 0, Length);

        var success = seekOffset == seek;
        var wasRunning = IsRunning;

        lock (clock)
        {
            if (success && wasRunning)
                clock.Restart();
            else
                clock.Reset();
        }

        nextEventIndex = findFirstEventAfter(seekOffset);
        stopAllChannels();
        stopPreviewChannel();

        if (previewSample != null && wasRunning && !SuppressEventProcessing)
            startPreviewChannel();

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
        stopPreviewChannel();

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

        if (!IsRunning || SuppressEventProcessing)
            return;

        if (previewSample != null)
            return;

        var currentTime = CurrentTime;

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

    private static IEnumerable<string> getPreviewCandidates(string basePath, string? previewFile)
    {
        if (!string.IsNullOrWhiteSpace(previewFile))
            yield return previewFile;

        if (!Directory.Exists(basePath))
            yield break;

        foreach (var file in Directory.EnumerateFiles(basePath)
                     .Select(Path.GetFileName)
                     .Where(static name => name != null
                                           && name.StartsWith("preview", StringComparison.OrdinalIgnoreCase)
                                           && isSupportedPreviewExtension(name))
                     .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase))
        {
            yield return file!;
        }

        yield break;

        static bool isSupportedPreviewExtension(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase);
        }
    }

    private void updateClockRate()
    {
        lock (clock)
            clock.Rate = AggregateFrequency.Value * AggregateTempo.Value;
    }

    private void playSample(BgmEvent evt)
    {
        if (sampleStore == null)
            return;

        var sample = resolveSample(evt.SamplePath);

        if (sample == null)
            return;

        var channel = sample.GetChannel();
        channel.ManualFree = true;
        channel.Play();

        // channel.Play() may bind the decoded sample's aggregate chain after this call. BGM preview
        // must follow only this Track's aggregate chain, so strip sample/effect routing again once.
        bindPreviewVolumeAdjustments(channel);

        Action<ValueChangedEvent<double>>? isolateOnBind = null;
        isolateOnBind = _ =>
        {
            channel.AggregateVolume.ValueChanged -= isolateOnBind!;
            bindPreviewVolumeAdjustments(channel);
        };
        channel.AggregateVolume.ValueChanged += isolateOnBind;

        activeChannels.Add(new ActiveBgm(channel));
    }

    private ISample? resolveSample(string samplePath)
    {
        if (sampleStore == null)
            return null;

        if (resolvedSamples.TryGetValue(samplePath, out var cached))
            return cached;

        foreach (var lookup in new BmsSampleInfo(samplePath).LookupNames)
        {
            var sample = sampleStore.Get(lookup);

            if (sample != null)
                return resolvedSamples[samplePath] = sample;
        }

        return resolvedSamples[samplePath] = null;
    }

    private void bindPreviewVolumeAdjustments(IAdjustableAudioComponent component)
    {
        component.RemoveAllAdjustments(AdjustableProperty.Volume);
        component.AddAdjustment(AdjustableProperty.Volume, AggregateVolume);
    }

    private void startPreviewChannel()
    {
        if (previewSample == null || previewChannel != null)
            return;

        previewChannel = previewSample.GetChannel();
        previewChannel.ManualFree = true;
        previewChannel.Play();
        bindPreviewVolumeAdjustments(previewChannel);
    }

    private void stopPreviewChannel()
    {
        if (previewChannel == null)
            return;

        if (!previewChannel.IsDisposed)
        {
            previewChannel.Stop();
            previewChannel.Dispose();
        }

        previewChannel = null;
    }

    private void stopAllChannels()
    {
        foreach (var active in activeChannels.ToArray())
            active.StopAndDispose();

        activeChannels.Clear();
    }

    private void cleanupChannels()
    {
        for (var i = activeChannels.Count - 1; i >= 0; i--)
        {
            var active = activeChannels[i];

            if (active.IsDisposed)
            {
                activeChannels.RemoveAt(i);
                continue;
            }

            if (active.HasFinished)
            {
                active.StopAndDispose();
                activeChannels.RemoveAt(i);
            }
        }
    }

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

    private sealed class ActiveBgm(SampleChannel channel)
    {
        public bool IsDisposed => channel.IsDisposed;

        public bool HasFinished => channel.Played && !channel.Playing;

        public void StopAndDispose()
        {
            if (!channel.IsDisposed)
            {
                channel.Stop();
                channel.Dispose();
            }
        }
    }
}
