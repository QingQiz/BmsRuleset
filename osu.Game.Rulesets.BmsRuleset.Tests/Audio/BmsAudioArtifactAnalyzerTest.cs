using System;
using NUnit.Framework;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[TestFixture]
public class BmsAudioArtifactAnalyzerTest
{
    private const int sample_rate = 44100;

    [Test]
    public void CleanSineDoesNotReportArtifact()
    {
        var samples = createSine(1, 440);
        var report = BmsAudioArtifactAnalyzer.Analyze(samples, sample_rate, 2);

        Assert.That(report.HasArtifacts, Is.False);
    }

    [Test]
    public void DetectsSingleSampleDiscontinuity()
    {
        var samples = createSine(1, 440);
        var frame = sample_rate / 2;
        samples[frame * 2] += 0.8f;
        samples[frame * 2 + 1] += 0.8f;

        var report = BmsAudioArtifactAnalyzer.Analyze(samples, sample_rate, 2);

        Assert.That(report.Artifacts, Has.Some.Matches<BmsAudioArtifact>(artifact =>
            artifact.Kind == "discontinuity" && Math.Abs(artifact.Time - 0.5) < 0.001));
    }

    [Test]
    public void DetectsSustainedClipping()
    {
        var samples = createSine(1, 440);
        Array.Fill(samples, 1f, sample_rate, 100);

        var report = BmsAudioArtifactAnalyzer.Analyze(samples, sample_rate, 2);

        Assert.That(report.Artifacts, Has.Some.Property(nameof(BmsAudioArtifact.Kind)).EqualTo("clipping"));
    }

    [Test]
    public void IdenticalMixerStagesDoNotReportDifference()
    {
        var samples = createSine(1, 440);
        var artifacts = BmsAudioArtifactAnalyzer.CompareExactStages(samples, samples, sample_rate, 2);

        Assert.That(artifacts, Is.Empty);
    }

    [Test]
    public void DetectsLimiterDifferenceAtExactTime()
    {
        var before = createSine(1, 440);
        var after = (float[])before.Clone();
        var frame = sample_rate / 2;

        for (var offset = 0; offset < 20; offset++)
        {
            before[(frame + offset) * 2] = 1.2f;
            before[(frame + offset) * 2 + 1] = 1.2f;
            after[(frame + offset) * 2] = 0.98f;
            after[(frame + offset) * 2 + 1] = 0.98f;
        }

        var artifacts = BmsAudioArtifactAnalyzer.CompareExactStages(before, after, sample_rate, 2);

        Assert.That(artifacts, Has.Some.Matches<BmsAudioArtifact>(artifact =>
            artifact.Kind == "limiter_difference" && Math.Abs(artifact.Time - 0.5) < 0.01));
    }

    [Test]
    public void CleanResampledOutputDoesNotReportMismatch()
    {
        var reference = createSine(2, 440, sample_rate);
        var output = createSine(2, 440, 48000);

        var artifacts = BmsAudioArtifactAnalyzer.CompareResampledOutput(reference, sample_rate, 2, output, 48000, 2);

        Assert.That(artifacts, Is.Empty);
    }

    [Test]
    public void DetectsNoiseAddedAfterMixer()
    {
        const int output_rate = 48000;
        var reference = createSine(2, 440, sample_rate);
        var output = createSine(2, 440, output_rate);
        addSyncPulse(reference, sample_rate);
        addSyncPulse(output, output_rate);
        var noiseStart = output_rate;

        for (var frame = noiseStart; frame < noiseStart + output_rate / 100; frame++)
        {
            var noise = frame % 2 == 0 ? 0.2f : -0.2f;
            output[frame * 2] += noise;
            output[frame * 2 + 1] += noise;
        }

        var artifacts = BmsAudioArtifactAnalyzer.CompareResampledOutput(reference, sample_rate, 2, output, output_rate, 2);

        Assert.That(artifacts, Has.Some.Matches<BmsAudioArtifact>(artifact =>
            artifact.Kind == "output_mismatch" && Math.Abs(artifact.Time - 1) < 0.02));
    }

    private static void addSyncPulse(float[] samples, int rate)
    {
        var start = rate / 4;
        for (var frame = start; frame < start + rate / 100; frame++)
        {
            var value = (float)Math.Sin(2 * Math.PI * 1733 * frame / rate) * 0.5f;
            samples[frame * 2] += value;
            samples[frame * 2 + 1] += value;
        }
    }

    private static float[] createSine(double duration, double frequency, int rate = sample_rate)
    {
        var frames = (int)(rate * duration);
        var samples = new float[frames * 2];

        for (var frame = 0; frame < frames; frame++)
        {
            var value = (float)(Math.Sin(2 * Math.PI * frequency * frame / rate) * 0.25);
            samples[frame * 2] = value;
            samples[frame * 2 + 1] = value;
        }

        return samples;
    }
}
