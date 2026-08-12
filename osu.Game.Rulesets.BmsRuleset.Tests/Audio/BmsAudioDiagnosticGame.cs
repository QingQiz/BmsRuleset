#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using ManagedBass;
using osu.Framework.Allocation;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Native;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

// ReSharper disable LocalizableElement

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

internal partial class BmsAudioDiagnosticGame : osu.Framework.Game
{
    private const double start_delay = 1000;
    private const double finish_tail = 3000;
    private const double scheduling_lead = 200;

    private readonly string chartPath;
    private readonly double requestedStartTime;
    private readonly double requestedDuration;
    private readonly double requestedVolume;
    private readonly double requestedSampleGain;
    private readonly HashSet<ushort>? requestedSampleKeys;
    private readonly bool traceEvents;
    private readonly bool traceScheduling;
    private readonly bool liveBatch;
    private readonly bool detectArtifacts;
    private readonly bool silentCapture;
    private readonly bool forceWasapi;
    private readonly bool captureGlobal;
    private readonly string? capturePath;
    private readonly string? reportPath;
    private readonly string? loopbackDevice;
    private readonly string? loopbackCapturePath;

    private ScheduledSample[] events = [];
    private BmsSamplePlayback samplePlayback = null!;
    private int nextEventIndex;
    private double playbackStartTime;
    private double playbackEndTime;
    private bool playbackStarted;
    private bool exitRequested;
    private int nextProgressSecond;
    private BmsAudioCaptureSession? captureSession;
    private BmsAudioCaptureSession? preLimiterCaptureSession;
    private BmsWasapiLoopbackCaptureSession? loopbackCaptureSession;

    internal int ResultCode { get; private set; }

    public BmsAudioDiagnosticGame(string[] args)
    {
        chartPath = getRequiredArgument(args, "--chart");
        requestedStartTime = getDoubleArgument(args, "--start", 0) * 1000;
        requestedDuration = getDoubleArgument(args, "--duration", 0) * 1000;
        requestedVolume = getDoubleArgument(args, "--volume", 1);
        requestedSampleGain = getDoubleArgument(args, "--sample-gain", 1);
        requestedSampleKeys = getHexArguments(args, "--sample-key");
        traceEvents = args.Contains("--trace-events");
        traceScheduling = args.Contains("--trace-scheduling");
        liveBatch = args.Contains("--live-batch");
        detectArtifacts = args.Contains("--detect-artifacts");
        silentCapture = args.Contains("--silent");
        forceWasapi = args.Contains("--wasapi");
        captureGlobal = args.Contains("--capture-global");
        capturePath = getOptionalArgument(args, "--capture");
        reportPath = getOptionalArgument(args, "--artifact-report");
        loopbackDevice = getOptionalArgument(args, "--loopback-device");
        loopbackCapturePath = getOptionalArgument(args, "--loopback-capture");

        if (requestedVolume > 1 || requestedSampleGain > 1)
            throw new ArgumentException("Arguments --volume and --sample-gain must be between 0 and 1.");
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var fullPath = Path.GetFullPath(chartPath);
        var parsed = BmsChartParser.Parse(
            BmsChartParser.ReadAllLines(File.ReadAllBytes(fullPath)),
            fullPath,
            _ => 1);

        events = collectEvents(parsed)
            .Where(evt => evt.Time >= requestedStartTime)
            .Where(evt => requestedSampleKeys == null || requestedSampleKeys.Contains(evt.SampleKey))
            .Where(evt => parsed.SampleDefinitions.TryGetValue(evt.SampleKey, out var path) && !string.IsNullOrEmpty(path))
            .OrderBy(evt => evt.Time)
            .ToArray();

        if (events.Length == 0)
            throw new InvalidOperationException("The requested interval contains no sample events.");

        var lastEventTime = events[^1].Time;
        playbackEndTime = requestedDuration > 0
            ? Math.Min(lastEventTime, requestedStartTime + requestedDuration)
            : lastEventTime;
        events = events.TakeWhile(evt => evt.Time <= playbackEndTime).ToArray();

        if (traceEvents)
        {
            foreach (var evt in events)
            {
                parsed.SampleDefinitions.TryGetValue(evt.SampleKey, out var samplePath);
                Console.WriteLine($"BMS_AUDIO_EVENT time={evt.Time:0.###} key={evt.SampleKey:X2} volume={evt.Volume} path={samplePath}");
            }
        }

        samplePlayback = new BmsSamplePlayback(
            parsed.SampleDefinitions,
            Path.GetDirectoryName(fullPath),
            sampleUsages: events.Select(evt => new BmsSampleUsage(evt.SampleKey, evt.Time - requestedStartTime)));
        Add(samplePlayback);

        Console.WriteLine($"BMS_AUDIO_DIAGNOSTIC chart={fullPath}");
        Console.WriteLine($"BMS_AUDIO_DIAGNOSTIC events={events.Length} keys={events.Select(evt => evt.SampleKey).Distinct().Count()}");
        Console.WriteLine($"BMS_AUDIO_DIAGNOSTIC interval={requestedStartTime / 1000:0.###}-{playbackEndTime / 1000:0.###}s");
        Console.WriteLine($"BMS_AUDIO_DIAGNOSTIC volume={requestedVolume:0.###}");
        Console.WriteLine($"BMS_AUDIO_DIAGNOSTIC sample_gain={requestedSampleGain:0.###}");
        var sampleKeys = requestedSampleKeys == null
            ? "all"
            : string.Join(",", requestedSampleKeys.Select(key => key.ToString("X")));
        Console.WriteLine($"BMS_AUDIO_DIAGNOSTIC sample_key={sampleKeys}");
        Console.WriteLine($"BMS_AUDIO_DIAGNOSTIC pcm_mixer_patch={BmsPcmMixerPatcher.IsInstalled}");
        Console.WriteLine("BMS_AUDIO_DIAGNOSTIC limiter=true");
        Console.WriteLine("BMS_AUDIO_DIAGNOSTIC native_scheduling=true");
        Console.WriteLine($"BMS_AUDIO_DIAGNOSTIC live_batch={liveBatch}");
        Console.WriteLine($"BMS_AUDIO_DIAGNOSTIC pcm_backend={samplePlayback.DiagnosticMixer != null}");

        if (Bass.GetInfo(out var deviceInfo))
        {
            Console.WriteLine($"BMS_AUDIO_DIAGNOSTIC device_latency={deviceInfo.Latency}ms");
            Console.WriteLine($"BMS_AUDIO_DIAGNOSTIC minimum_buffer={deviceInfo.MinBufferLength}ms");
        }

        Console.WriteLine($"BMS_AUDIO_DIAGNOSTIC limiter_state={formatLimiterDiagnostics()}");
        Console.WriteLine($"BMS_AUDIO_DIAGNOSTIC pcm_state={formatPcmDiagnostics()}");
    }

    protected override void LoadComplete()
    {
        Audio.Volume.Value = requestedVolume;

        if (forceWasapi)
            Audio.UseExperimentalWasapi.Value = true;

        base.LoadComplete();
    }

    protected override void Update()
    {
        base.Update();

        if (!playbackStarted)
        {
            if (!samplePlayback.IsLoaded || forceWasapi && !Audio.UsingGlobalMixer.Value)
                return;

            playbackStarted = true;
            var captureDuration = TimeSpan.FromMilliseconds(start_delay + playbackEndTime - requestedStartTime + finish_tail + 5000);

            if (detectArtifacts || !string.IsNullOrEmpty(capturePath))
            {
                var capturePair = captureGlobal
                    ? BmsAudioCaptureSession.StartGlobalPair(Audio, captureDuration, silentCapture)
                    : BmsAudioCaptureSession.StartPair(
                        samplePlayback.DiagnosticMixer ?? throw new InvalidOperationException("The BMS diagnostic mixer is unavailable."),
                        captureDuration,
                        silentCapture);
                preLimiterCaptureSession = capturePair.Before;
                captureSession = capturePair.After;
            }

            if (!string.IsNullOrEmpty(loopbackDevice) || !string.IsNullOrEmpty(loopbackCapturePath))
                loopbackCaptureSession = BmsWasapiLoopbackCaptureSession.Start(loopbackDevice ?? "55", captureDuration);

            playbackStartTime = Time.Current + start_delay;
            Console.WriteLine("BMS_AUDIO_DIAGNOSTIC ready");
        }

        var chartTime = requestedStartTime + Time.Current - playbackStartTime;

        var currentSchedulingLead = liveBatch ? 0 : scheduling_lead;

        while (nextEventIndex < events.Length && events[nextEventIndex].Time - currentSchedulingLead <= chartTime)
        {
            var evt = events[nextEventIndex];
            var volume = (int)Math.Round(evt.Volume * requestedSampleGain);

            if (liveBatch)
            {
                samplePlayback.QueueLivePlay(evt.SampleKey, volume);
                nextEventIndex++;
                continue;
            }

            if (evt.Time > chartTime)
            {
                if (samplePlayback.CanSchedule(evt.SampleKey))
                    samplePlayback.SchedulePlay(evt.SampleKey, volume, evt.Time);
                else
                {
                    if (traceScheduling)
                    {
                        Console.WriteLine(
                            $"BMS_AUDIO_SCHEDULE_BLOCKED chart={evt.Time:0.###} now={chartTime:0.###} " +
                            $"key={evt.SampleKey:X2} pcm_state={samplePlayback.DiagnosticSnapshot.Cache}");
                    }

                    samplePlayback.Play(evt.SampleKey, volume);
                }
            }
            else
            {
                if (traceScheduling && chartTime - evt.Time > scheduling_lead)
                {
                    Console.WriteLine(
                        $"BMS_AUDIO_SCHEDULE_LATE chart={evt.Time:0.###} now={chartTime:0.###} " +
                        $"late={chartTime - evt.Time:0.###} key={evt.SampleKey:X2}");
                }

                samplePlayback.Play(evt.SampleKey, volume);
            }

            nextEventIndex++;
        }

        if (liveBatch)
            samplePlayback.SubmitLivePlayBatch();

        var elapsedSecond = (int)Math.Max(0, (chartTime - requestedStartTime) / 1000);

        if (elapsedSecond >= nextProgressSecond)
        {
            Console.WriteLine($"BMS_AUDIO_DIAGNOSTIC progress={elapsedSecond}s triggered={nextEventIndex}/{events.Length}");
            nextProgressSecond = elapsedSecond + 10;
        }

        if (chartTime < playbackEndTime + finish_tail || exitRequested)
            return;

        exitRequested = true;
        completeCapture();
        Console.WriteLine("BMS_AUDIO_DIAGNOSTIC complete");
        Console.WriteLine($"BMS_AUDIO_DIAGNOSTIC limiter_state={formatLimiterDiagnostics()}");
        Console.WriteLine($"BMS_AUDIO_DIAGNOSTIC pcm_state={formatPcmDiagnostics()}");
        Exit();
    }

    private void completeCapture()
    {
        if (captureSession == null && loopbackCaptureSession == null)
            return;

        captureSession?.Dispose();
        preLimiterCaptureSession?.Dispose();
        loopbackCaptureSession?.Dispose();

        var mixerSkipFrames = captureSession == null ? 0 : (int)Math.Round(start_delay / 1000 * captureSession.SampleRate);
        var samples = captureSession?.GetSamples() ?? [];
        var preLimiterSamples = preLimiterCaptureSession?.GetSamples() ?? [];
        var mixerChannels = captureSession?.Channels ?? 0;
        var skipSamples = Math.Min(samples.Length, mixerSkipFrames * mixerChannels);
        var mixerCaptured = samples.AsSpan(skipSamples).ToArray();
        var preLimiterSkipSamples = Math.Min(preLimiterSamples.Length, mixerSkipFrames * mixerChannels);
        var preLimiterCaptured = preLimiterSamples.AsSpan(preLimiterSkipSamples).ToArray();

        if (!string.IsNullOrEmpty(capturePath) && captureSession != null)
            captureSession.WriteWave(capturePath, mixerSkipFrames);

        var loopbackSkipFrames = loopbackCaptureSession == null ? 0 : (int)Math.Round(start_delay / 1000 * loopbackCaptureSession.SampleRate);
        var loopbackSamples = loopbackCaptureSession?.GetSamples() ?? [];
        var loopbackChannels = loopbackCaptureSession?.Channels ?? 0;
        var loopbackSkipSamples = Math.Min(loopbackSamples.Length, loopbackSkipFrames * loopbackChannels);
        var loopbackCaptured = loopbackSamples.AsSpan(loopbackSkipSamples).ToArray();

        if (!string.IsNullOrEmpty(loopbackCapturePath) && loopbackCaptureSession != null)
            loopbackCaptureSession.WriteWave(loopbackCapturePath, loopbackSkipFrames);

        if (detectArtifacts)
        {
            var useLoopback = loopbackCaptureSession != null;
            var captured = useLoopback ? loopbackCaptured : mixerCaptured;
            var sampleRate = useLoopback ? loopbackCaptureSession!.SampleRate : captureSession!.SampleRate;
            var channels = useLoopback ? loopbackCaptureSession!.Channels : captureSession!.Channels;

            if (captured.Length == 0)
                throw new InvalidOperationException(useLoopback ? "WASAPI loopback captured no audio." : "The mixer captured no audio.");

            var report = BmsAudioArtifactAnalyzer.Analyze(captured, sampleRate, channels);

            // PCM source files can legitimately contain drum transients with a larger
            // one-frame derivative than their local neighbourhood. Retrigger continuity is
            // covered by the offline voice-mixer tests; end-to-end capture should report
            // clipping and divergence from the pre-device reference instead.
            report = report with
            {
                Artifacts = report.Artifacts.Where(artifact => artifact.Kind != "discontinuity").ToArray(),
            };

            if (useLoopback && captureSession != null && mixerCaptured.Length > 0)
            {
                var outputArtifacts = BmsAudioArtifactAnalyzer.CompareResampledOutput(
                    mixerCaptured,
                    captureSession.SampleRate,
                    captureSession.Channels,
                    loopbackCaptured,
                    sampleRate,
                    channels);
                report = report with
                {
                    Artifacts = report.Artifacts
                        .Concat(outputArtifacts)
                        .OrderBy(artifact => artifact.Time)
                        .ToArray(),
                };
            }
            else if (!useLoopback)
            {
                var limiterArtifacts = BmsAudioArtifactAnalyzer.CompareExactStages(
                    preLimiterCaptured,
                    mixerCaptured,
                    sampleRate,
                    channels);
                report = report with
                {
                    Artifacts = report.Artifacts
                        .Concat(limiterArtifacts)
                        .OrderBy(artifact => artifact.Time)
                        .ToArray(),
                };
            }

            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });

            Console.WriteLine(
                $"BMS_AUDIO_ARTIFACT_RESULT source={(useLoopback ? "wasapi-loopback" : "mixer")} " +
                $"detected={report.HasArtifacts} count={report.Artifacts.Count} peak={report.Peak:0.######}");
            foreach (var artifact in report.Artifacts.Take(100))
            {
                Console.WriteLine(
                    $"BMS_AUDIO_ARTIFACT time={artifact.Time:0.######} kind={artifact.Kind} " +
                    $"score={artifact.Score:0.###} magnitude={artifact.Magnitude:0.######}");
            }

            if (!string.IsNullOrEmpty(reportPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
                File.WriteAllText(reportPath, json);
            }

            ResultCode = report.HasArtifacts ? 2 : 0;
        }

        captureSession = null;
        preLimiterCaptureSession = null;
        loopbackCaptureSession = null;
    }

    private string formatLimiterDiagnostics()
    {
        var diagnostics = samplePlayback.DiagnosticSnapshot.Audio;
        return $"limited_frames={diagnostics.LimitedFrames} input_peak={diagnostics.InputPeak:0.###} " +
               $"output_peak={diagnostics.OutputPeak:0.###} gain={diagnostics.LimiterGain:0.###}";
    }

    private string formatPcmDiagnostics()
    {
        var diagnostics = samplePlayback.DiagnosticSnapshot;
        var audio = diagnostics.Audio;
        var cache = diagnostics.Cache;
        return $"rendered={audio.RenderedFrames} active={audio.ActiveVoices} draining={audio.DrainingVoices} peak_voices={audio.PeakVoices} " +
               $"submitted_voices={audio.SubmittedVoices} folded_voices={audio.FoldedVoices} started_voices={audio.StartedVoices} " +
               $"queued={audio.QueuedCommands} queue_high_water={audio.QueueHighWater} queue_expansions={audio.QueueExpansions} " +
               $"voice_expansions={audio.VoicePoolExpansions} voice_overflows={audio.VoicePoolOverflows} preload_underflows={diagnostics.PreloadUnderflows} " +
               $"playback_underflows={audio.PlaybackUnderflows} callback_failures={diagnostics.BridgeCallbackFailures} " +
               $"assets={cache.LoadedAssets}/{cache.PreparingAssets}/{cache.FailedAssets} resident={cache.ResidentPcmBytes} " +
               $"peak_resident={cache.PeakResidentPcmBytes} evictions={cache.EvictionCount}";
    }

    private static IEnumerable<ScheduledSample> collectEvents(BmsParseResult parsed)
    {
        foreach (var evt in parsed.BackgroundSampleEvents)
            yield return new ScheduledSample(evt.Time, evt.SampleKey, evt.Volume);

        foreach (var hitObject in parsed.HitObjects.Where(hitObject => !hitObject.IsMine))
            yield return new ScheduledSample(hitObject.StartTime, hitObject.SampleKey, hitObject.SampleVolume);

        foreach (var evt in parsed.LongNoteTailSampleEvents)
            yield return new ScheduledSample(evt.Time, evt.SampleKey, evt.Volume);
    }

    private static string getRequiredArgument(IReadOnlyList<string> args, string name)
    {
        var index = findArgument(args, name);

        if (index < 0 || index + 1 >= args.Count)
            throw new ArgumentException($"Missing required argument {name}.");

        return args[index + 1];
    }

    private static string? getOptionalArgument(IReadOnlyList<string> args, string name)
    {
        var index = findArgument(args, name);
        return index >= 0 && index + 1 < args.Count ? args[index + 1] : null;
    }

    private static double getDoubleArgument(IReadOnlyList<string> args, string name, double defaultValue)
    {
        var index = findArgument(args, name);

        if (index < 0)
            return defaultValue;

        if (index + 1 >= args.Count
            || !double.TryParse(args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || value < 0)
            throw new ArgumentException($"Argument {name} must be a non-negative number.");

        return value;
    }

    private static HashSet<ushort>? getHexArguments(IReadOnlyList<string> args, string name)
    {
        var index = findArgument(args, name);

        if (index < 0)
            return null;

        if (index + 1 >= args.Count)
            throw new ArgumentException($"Argument {name} must be a hexadecimal sample key.");

        var values = new HashSet<ushort>();

        foreach (var argument in args[index + 1].Split(','))
        {
            if (!ushort.TryParse(argument, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                throw new ArgumentException($"Argument {name} must contain hexadecimal sample keys.");

            values.Add(value);
        }

        return values;
    }

    private static int findArgument(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == name)
                return i;
        }

        return -1;
    }

    private readonly record struct ScheduledSample(double Time, ushort SampleKey, int Volume);
}
