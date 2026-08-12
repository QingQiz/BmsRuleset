using System;
using System.Collections.Generic;
using System.Threading;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Native;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

/// <summary>
///     Owns the PCM backend used by gameplay sample playback.
/// </summary>
/// <remarks>
///     Keeping backend construction and teardown here leaves <see cref="BmsSamplePlayback"/> as a
///     small Component-facing facade.
/// </remarks>
internal sealed class BmsPcmPlaybackSession : IDisposable
{
    private readonly IReadOnlyDictionary<ushort, string> sampleDefinitions;
    private readonly string? basePath;
    private readonly double rate;
    private readonly IEnumerable<BmsSampleUsage>? sampleUsages;
    private readonly AudioManager audioManager;
    private readonly Func<double> currentTime;

    private BmsPcmVoiceMixer? pcmMixer;
    private BmsBassMixerBridge? pcmBridge;
    private bool disposed;

    internal BmsPcmPlaybackSession(
        IReadOnlyDictionary<ushort, string> sampleDefinitions,
        string? basePath,
        double rate,
        IEnumerable<BmsSampleUsage>? sampleUsages,
        AudioManager audioManager,
        Func<double> currentTime)
    {
        this.sampleDefinitions = sampleDefinitions;
        this.basePath = basePath;
        this.rate = rate;
        this.sampleUsages = sampleUsages;
        this.audioManager = audioManager;
        this.currentTime = currentTime;
    }

    internal bool IsInitialised => Controller?.IsInitialised == true;

    internal BmsPcmPlaybackController? Controller { get; private set; }

    internal AudioMixer? DiagnosticMixer { get; private set; }

    internal BmsSamplePlaybackDiagnostics DiagnosticSnapshot => new(
        pcmMixer?.GetDiagnostics() ?? default,
        Controller?.GetCacheDiagnostics() ?? default,
        Controller?.PreloadUnderflows ?? 0,
        pcmBridge?.CallbackFailures ?? 0);

    internal void Initialise(CancellationToken cancellationToken, double chartTime)
    {
        BmsPcmMixerPatcher.InstallOnce();

        if (!BmsPcmMixerPatcher.IsInstalled)
            return;

        DiagnosticMixer = audioManager.CreateAudioMixer(BmsPcmMixerPatcher.MIXER_IDENTIFIER);
        pcmMixer = new BmsPcmVoiceMixer();
        pcmBridge = new BmsBassMixerBridge(DiagnosticMixer, pcmMixer);
        Controller = new BmsPcmPlaybackController(
            sampleDefinitions,
            basePath,
            rate,
            sampleUsages,
            audioManager.AggregateVolume,
            currentTime,
            pcmMixer);
        Controller.Initialise(cancellationToken, chartTime);
        pcmBridge.EnsureAttached();
    }

    internal void Update(double chartTime)
    {
        pcmBridge?.EnsureAttached();
        Controller?.Update(chartTime);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        Controller?.Dispose();
        Controller = null;
        pcmBridge?.Dispose();
        pcmBridge = null;
        pcmMixer = null;
        DiagnosticMixer?.Dispose();
        DiagnosticMixer = null;
    }
}
