using System;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Audio.Track;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Processing;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;

internal sealed class BmsPcmPreviewVoiceTrack : Track
{
    private static long nextVoiceId;

    private readonly BmsPcmAssetLease lease;
    private readonly BmsPcmVoiceMixer mixer;
    private readonly double fixedRate;
    private readonly long voiceId = Interlocked.Increment(ref nextVoiceId);

    private ushort sampleKey;
    private double position;
    private double startPosition;
    private long startOutputFrame;
    private bool running;
    private bool disposed;
    private float submittedGain = -1;

    public override bool IsDummyDevice => false;

    public override double CurrentTime
    {
        get
        {
            if (!running)
                return position;

            var elapsedFrames = Math.Max(0, mixer.RenderedFrames - startOutputFrame);
            return Math.Min(Length, startPosition + elapsedFrames * 1000 * fixedRate / BmsFixedRatePcmProcessor.OUTPUT_SAMPLE_RATE);
        }
    }

    public override bool IsRunning => running && CurrentTime < Length;

    internal BmsPcmPreviewVoiceTrack(BmsPcmAssetLease lease, BmsPcmVoiceMixer mixer, double fixedRate, string name)
        : base(name)
    {
        this.lease = lease;
        this.mixer = mixer;
        this.fixedRate = fixedRate;
        Length = lease.Asset.OriginalDurationMilliseconds
                 ?? Math.Max(0, lease.Asset.TotalFrameCount * 1000d / lease.Asset.SampleRate * fixedRate);
    }

    internal void ConfigureTerminationDomain(ushort key) => sampleKey = key;

    internal void SynchroniseGain()
    {
        if (!running)
            return;

        var gain = double.IsFinite(AggregateVolume.Value) ? (float)Math.Max(0, AggregateVolume.Value) : 0;
        if (Math.Abs(gain - submittedGain) <= 0.000001f)
            return;

        submittedGain = gain;
        mixer.SubmitVoiceControl(BmsVoiceCommandType.SetVoiceGain, voiceId, mixer.RenderedFrames, 0, gain);
    }

    public override bool Seek(double seek)
    {
        var clamped = Math.Clamp(seek, 0, Length);
        var wasRunning = running;

        if (wasRunning)
            stopVoice();

        position = clamped;

        if (wasRunning && position < Length)
            startVoice();

        return clamped == seek;
    }

    public override Task<bool> SeekAsync(double seek) => Task.FromResult(Seek(seek));

    public override void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (running || position >= Length)
            return;

        startVoice();
    }

    public override Task StartAsync()
    {
        Start();
        return Task.CompletedTask;
    }

    public override void Stop()
    {
        if (!running)
            return;

        position = CurrentTime;
        stopVoice();
    }

    public override Task StopAsync()
    {
        Stop();
        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposed)
            return;

        disposed = true;
        Stop();
        lease.Dispose();
        base.Dispose(disposing);
    }

    private void startVoice()
    {
        var gain = double.IsFinite(AggregateVolume.Value) ? (float)Math.Max(0, AggregateVolume.Value) : 0;
        var sourceOffset = (long)Math.Round(position * BmsFixedRatePcmProcessor.OUTPUT_SAMPLE_RATE / (1000 * fixedRate));
        var targetFrame = mixer.RenderedFrames;
        var play = new BmsVoicePlay(
            lease.Asset,
            new BmsTerminationDomain(sampleKey),
            targetFrame,
            gain,
            sourceOffset,
            VoiceId: voiceId);

        if (!mixer.SubmitPlayBatch([play]))
            return;

        startPosition = position;
        startOutputFrame = targetFrame;
        submittedGain = gain;
        running = true;
    }

    private void stopVoice()
    {
        mixer.SubmitVoiceControl(BmsVoiceCommandType.StopVoice, voiceId, mixer.RenderedFrames, 0);
        running = false;
        submittedGain = -1;
    }
}
