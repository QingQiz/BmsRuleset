#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

internal readonly record struct BmsAudioArtifact(
    double Time,
    string Kind,
    double Score,
    float Magnitude);

internal sealed record BmsAudioArtifactReport(
    float Peak,
    IReadOnlyList<BmsAudioArtifact> Artifacts)
{
    public bool HasArtifacts => Artifacts.Count > 0;
}

internal static class BmsAudioArtifactAnalyzer
{
    private const float minimum_jump = 0.035f;
    private const double minimum_jump_score = 9;
    private const float clipping_threshold = 0.9995f;

    internal static BmsAudioArtifactReport Analyze(ReadOnlySpan<float> samples, int sampleRate, int channels)
    {
        if (sampleRate <= 0 || channels <= 0 || samples.Length % channels != 0)
            throw new ArgumentException("Audio data must contain complete frames with a valid sample rate.");

        var frameCount = samples.Length / channels;
        var peak = 0f;
        var candidates = new List<BmsAudioArtifact>();
        var localRadius = Math.Max(16, sampleRate / 200); // 5 ms on either side.
        var exclusionRadius = Math.Max(2, sampleRate / 4000);

        for (var i = 0; i < samples.Length; i++)
            peak = Math.Max(peak, Math.Abs(samples[i]));

        for (var frame = 1; frame < frameCount; frame++)
        {
            var jump = 0f;

            for (var channel = 0; channel < channels; channel++)
            {
                var index = frame * channels + channel;
                jump = Math.Max(jump, Math.Abs(samples[index] - samples[index - channels]));
            }

            if (jump < minimum_jump)
                continue;

            var start = Math.Max(1, frame - localRadius);
            var end = Math.Min(frameCount - 1, frame + localRadius);
            double sumSquares = 0;
            var count = 0;

            for (var localFrame = start; localFrame <= end; localFrame++)
            {
                if (Math.Abs(localFrame - frame) <= exclusionRadius)
                    continue;

                for (var channel = 0; channel < channels; channel++)
                {
                    var index = localFrame * channels + channel;
                    var difference = samples[index] - samples[index - channels];
                    sumSquares += difference * difference;
                    count++;
                }
            }

            var localDerivativeRms = count == 0 ? 0 : Math.Sqrt(sumSquares / count);
            var score = jump / Math.Max(localDerivativeRms, 0.0001);

            if (score >= minimum_jump_score)
                candidates.Add(new BmsAudioArtifact((double)frame / sampleRate, "discontinuity", score, jump));
        }

        var clippedFrames = 0;
        for (var frame = 0; frame < frameCount; frame++)
        {
            var clipped = false;

            for (var channel = 0; channel < channels; channel++)
                clipped |= Math.Abs(samples[frame * channels + channel]) >= clipping_threshold;

            clippedFrames = clipped ? clippedFrames + 1 : 0;

            if (clippedFrames == Math.Max(2, sampleRate / 2000))
            {
                candidates.Add(new BmsAudioArtifact(
                    (double)(frame - clippedFrames + 1) / sampleRate,
                    "clipping",
                    clippedFrames,
                    clipping_threshold));
            }
        }

        var grouped = candidates
            .OrderBy(candidate => candidate.Time)
            .Aggregate(new List<BmsAudioArtifact>(), (result, candidate) =>
            {
                if (result.Count == 0 || candidate.Time - result[^1].Time > 0.005 || candidate.Kind != result[^1].Kind)
                    result.Add(candidate);
                else if (candidate.Score > result[^1].Score)
                    result[^1] = candidate;

                return result;
            });

        return new BmsAudioArtifactReport(
            peak,
            grouped);
    }

    internal static IReadOnlyList<BmsAudioArtifact> CompareExactStages(
        ReadOnlySpan<float> before,
        ReadOnlySpan<float> after,
        int sampleRate,
        int channels)
    {
        var frameCount = Math.Min(before.Length, after.Length) / channels;
        var windowFrames = Math.Max(1, sampleRate / 200);
        var artifacts = new List<BmsAudioArtifact>();

        for (var windowStart = 0; windowStart + windowFrames <= frameCount; windowStart += windowFrames)
        {
            double signalEnergy = 0;
            double differenceEnergy = 0;
            var maximumDifference = 0f;

            for (var frame = windowStart; frame < windowStart + windowFrames; frame++)
            {
                for (var channel = 0; channel < channels; channel++)
                {
                    var index = frame * channels + channel;
                    var difference = after[index] - before[index];
                    signalEnergy += before[index] * before[index];
                    differenceEnergy += difference * difference;
                    maximumDifference = Math.Max(maximumDifference, Math.Abs(difference));
                }
            }

            var signalRms = Math.Sqrt(signalEnergy / (windowFrames * channels));
            var differenceRms = Math.Sqrt(differenceEnergy / (windowFrames * channels));
            var score = differenceRms / Math.Max(signalRms, 0.000001);

            if (maximumDifference > 0.002f && score > 0.0005)
            {
                artifacts.Add(new BmsAudioArtifact(
                    (double)windowStart / sampleRate,
                    "limiter_difference",
                    score,
                    maximumDifference));
            }
        }

        return artifacts
            .Aggregate(new List<BmsAudioArtifact>(), (grouped, artifact) =>
            {
                if (grouped.Count == 0 || artifact.Time - grouped[^1].Time > 0.02)
                    grouped.Add(artifact);
                else if (artifact.Score > grouped[^1].Score)
                    grouped[^1] = artifact;

                return grouped;
            });
    }

    internal static IReadOnlyList<BmsAudioArtifact> CompareResampledOutput(
        ReadOnlySpan<float> reference,
        int referenceSampleRate,
        int referenceChannels,
        ReadOnlySpan<float> output,
        int outputSampleRate,
        int outputChannels)
    {
        if (referenceSampleRate <= 0 || outputSampleRate <= 0 || referenceChannels <= 0 || outputChannels <= 0)
            throw new ArgumentException("Audio data must use valid sample rates and channel counts.");

        var resampled = resample(reference, referenceSampleRate, referenceChannels, outputSampleRate, outputChannels);
        var lag = findAlignmentLag(resampled, output, outputSampleRate, outputChannels);
        var referenceStart = Math.Max(0, -lag);
        var outputStart = Math.Max(0, lag);
        var availableFrames = Math.Min(resampled.Length / outputChannels - referenceStart, output.Length / outputChannels - outputStart);
        var windowFrames = Math.Max(1, outputSampleRate / 100); // 10 ms.
        var artifacts = new List<BmsAudioArtifact>();

        for (var start = 0; start + windowFrames <= availableFrames; start += windowFrames)
        {
            double referenceEnergy = 0;
            double outputEnergy = 0;
            double dot = 0;

            for (var frame = 0; frame < windowFrames; frame++)
            {
                for (var channel = 0; channel < outputChannels; channel++)
                {
                    var referenceSample = resampled[(referenceStart + start + frame) * outputChannels + channel];
                    var outputSample = output[(outputStart + start + frame) * outputChannels + channel];
                    referenceEnergy += referenceSample * referenceSample;
                    outputEnergy += outputSample * outputSample;
                    dot += referenceSample * outputSample;
                }
            }

            if (referenceEnergy < windowFrames * outputChannels * 0.000004)
                continue;

            var correlation = dot / Math.Sqrt(referenceEnergy * Math.Max(outputEnergy, 0.000000000001));
            var rmsRatio = Math.Sqrt(outputEnergy / Math.Max(referenceEnergy, 0.000000000001));

            // The device's band-limited resampler and this diagnostic's linear resampler can
            // disagree in phase on dense high-frequency material while preserving its energy.
            // Added noise and output gaps change both correlation and short-window energy.
            if (correlation < 0.90 && rmsRatio is < 0.75 or > 1.25)
            {
                artifacts.Add(new BmsAudioArtifact(
                    (double)(referenceStart + start) / outputSampleRate,
                    "output_mismatch",
                    1 - correlation,
                    (float)correlation));
            }
        }

        return artifacts.Aggregate(new List<BmsAudioArtifact>(), (grouped, artifact) =>
        {
            if (grouped.Count == 0 || artifact.Time - grouped[^1].Time > 0.03)
                grouped.Add(artifact);
            else if (artifact.Score > grouped[^1].Score)
                grouped[^1] = artifact;

            return grouped;
        });
    }

    private static float[] resample(
        ReadOnlySpan<float> input,
        int inputSampleRate,
        int inputChannels,
        int outputSampleRate,
        int outputChannels)
    {
        var inputFrames = input.Length / inputChannels;
        var outputFrames = (int)Math.Floor((double)inputFrames * outputSampleRate / inputSampleRate);
        var result = new float[outputFrames * outputChannels];

        for (var outputFrame = 0; outputFrame < outputFrames; outputFrame++)
        {
            var sourcePosition = (double)outputFrame * inputSampleRate / outputSampleRate;
            var firstFrame = Math.Min((int)sourcePosition, inputFrames - 1);
            var secondFrame = Math.Min(firstFrame + 1, inputFrames - 1);
            var fraction = (float)(sourcePosition - firstFrame);

            for (var channel = 0; channel < outputChannels; channel++)
            {
                var sourceChannel = Math.Min(channel, inputChannels - 1);
                var first = input[firstFrame * inputChannels + sourceChannel];
                var second = input[secondFrame * inputChannels + sourceChannel];
                result[outputFrame * outputChannels + channel] = first + (second - first) * fraction;
            }
        }

        return result;
    }

    private static int findAlignmentLag(ReadOnlySpan<float> reference, ReadOnlySpan<float> output, int sampleRate, int channels)
    {
        var stride = Math.Max(1, sampleRate / 4000);
        var maxLag = sampleRate / 2;
        var comparisonFrames = Math.Min(reference.Length, output.Length) / channels - maxLag;
        if (comparisonFrames <= 0)
            return 0;

        // Sampling the whole capture avoids choosing a periodic or silent match near startup.
        var comparisonStride = Math.Max(stride, comparisonFrames / 8000);
        var bestScore = double.NegativeInfinity;
        var bestLag = 0;

        for (var lag = -maxLag; lag <= maxLag; lag += stride)
        {
            var referenceStart = Math.Max(0, -lag);
            var outputStart = Math.Max(0, lag);
            double dot = 0;
            double referenceEnergy = 0;
            double outputEnergy = 0;

            for (var frame = 0; frame < comparisonFrames; frame += comparisonStride)
            {
                var referenceSample = reference[(referenceStart + frame) * channels];
                var outputSample = output[(outputStart + frame) * channels];
                dot += referenceSample * outputSample;
                referenceEnergy += referenceSample * referenceSample;
                outputEnergy += outputSample * outputSample;
            }

            var score = dot / Math.Sqrt(Math.Max(referenceEnergy * outputEnergy, 0.000000000001));
            if (score > bestScore)
            {
                bestScore = score;
                bestLag = lag;
            }
        }

        return bestLag;
    }
}
