using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.LongNoteMode;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

public class BmsGameplayDiagnosticOptionsTest
{
    private string directory = null!;
    private string[] required = null!;

    [SetUp]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), "bms-diagnostic-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var chart = Path.Combine(directory, "arbitrary chart.bms");
        File.WriteAllText(chart, "#BPM 120");
        required = ["--filter", "gameplay", "--chart", chart, "--output", Path.Combine(directory, "report")];
    }

    [TearDown]
    public void TearDown() => Directory.Delete(directory, true);

    [Test]
    public void SupportsAnyChartAndRepresentativeSettings()
    {
        var options = BmsGameplayDiagnosticOptions.Parse([.. required, "--skin", "Legacy", "--long-note-mode", "HellChargeNote", "--start", "12.5", "--headless"]);
        Assert.That(options.Skin, Is.EqualTo(BmsTestSkins.SkinKind.Legacy));
        Assert.That(options.LongNoteMode, Is.EqualTo(BmsLongNoteMode.HellChargeNote));
        Assert.That(options.Start, Is.EqualTo(12.5));
        Assert.That(options.WarmupSeconds, Is.EqualTo(2));
        Assert.That(options.Headless, Is.True);
        Assert.That(options.AudioOutput, Is.False);
        Assert.That(options.CaptureAudio, Is.False);
        Assert.That(options.OmitSnapshots, Is.False);
        Assert.That(BmsGameplayDiagnosticOptions.Parse([.. required, "--omit-snapshots"]).OmitSnapshots, Is.True);
        Assert.That(options.OmitAudioProbe, Is.False);
        Assert.That(options.InputOffsetMilliseconds, Is.Zero);
        Assert.That(BmsGameplayDiagnosticOptions.Parse([.. required, "--input-offset-ms", "5"]).InputOffsetMilliseconds, Is.EqualTo(5));
        Assert.That(BmsGameplayDiagnosticOptions.Parse([.. required, "--omit-audio-probe"]).OmitAudioProbe, Is.True);
        var capture = BmsGameplayDiagnosticOptions.Parse([.. required, "--capture-audio"]);
        Assert.That(capture.CaptureAudio, Is.True);
        Assert.That(capture.AudioOutput, Is.False, "Recording alone must keep diagnostic output muted.");
        Assert.That(BmsGameplayDiagnosticOptions.Parse([.. required, "--audio-output"]).AudioOutput, Is.True);
        var unprobedCapture = BmsGameplayDiagnosticOptions.Parse([.. required, "--omit-audio-probe", "--capture-audio", "--audio-output"]);
        Assert.That(unprobedCapture.OmitAudioProbe, Is.True);
        Assert.That(unprobedCapture.CaptureAudio, Is.True);
        Assert.That(unprobedCapture.AudioOutput, Is.True);
    }

    [Test]
    public void InvertUsesExplicitRepeatableSettings()
    {
        Assert.That(BmsGameplayDiagnosticOptions.Parse(required).CreateMods(), Is.Empty);
        var fixedLength = BmsGameplayDiagnosticOptions.Parse([.. required, "--invert"]).CreateMods().OfType<BmsModInvert>().Single();
        Assert.That(fixedLength.RandomiseLength.Value, Is.False);
        var options = BmsGameplayDiagnosticOptions.Parse([.. required, "--invert", "--invert-random-seed", "12345", "--long-note-mode", "HellChargeNote"]);
        var mods = options.CreateMods();
        var invert = mods.OfType<BmsModInvert>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(invert.RandomiseLength.Value, Is.True);
            Assert.That(invert.Seed.Value, Is.EqualTo(12345));
            Assert.That(mods.OfType<BmsModHellChargeNote>(), Has.Exactly(1).Items);
            Assert.That(options.CreateMods()[0], Is.Not.SameAs(mods[0]));
        });
        Assert.Throws<ArgumentException>(() => BmsGameplayDiagnosticOptions.Parse([.. required, "--invert-random-seed", "12345"]));
    }

    [Test]
    public void FrameRatesDefaultToUnlimitedAndCanBeCappedExplicitly()
    {
        var unlimited = BmsGameplayDiagnosticOptions.Parse(required);
        Assert.That(unlimited.UpdateHz, Is.Zero);
        Assert.That(unlimited.DrawHz, Is.Zero);
        var limited = BmsGameplayDiagnosticOptions.Parse([.. required, "--update-hz", "1000", "--draw-hz", "240"]);
        Assert.That(limited.UpdateHz, Is.EqualTo(1000));
        Assert.That(limited.DrawHz, Is.EqualTo(240));
    }

    [Test]
    public void VisualSettingsDefaultToNormalGameplay()
    {
        var options = BmsGameplayDiagnosticOptions.Parse(required);
        Assert.That(options.NoteHeightScale, Is.EqualTo(1));
        Assert.That(options.SoftConstant, Is.False);
        Assert.That(options.SoftConstantFadeIn, Is.Zero);
    }

    [TestCase("0.01", "-1000", 0.01, -1000)]
    [TestCase("1.37", "350", 1.37, 350)]
    [TestCase("5", "1000", 5, 1000)]
    public void SupportsNonDefaultVisualSettings(string height, string fadeIn, double expectedHeight, double expectedFadeIn)
    {
        var options = BmsGameplayDiagnosticOptions.Parse([.. required,
            "--note-height-scale", height, "--soft-constant", "--soft-constant-fade-in", fadeIn]);
        Assert.That(options.NoteHeightScale, Is.EqualTo(expectedHeight));
        Assert.That(options.SoftConstant, Is.True);
        Assert.That(options.SoftConstantFadeIn, Is.EqualTo(expectedFadeIn));
        Assert.That(BmsGameplayDiagnosticOptions.Parse([.. required, "--soft-constant-fade-in", fadeIn]).SoftConstant, Is.False);
    }

    [TestCase("--warmup-seconds", "NaN")]
    [TestCase("--warmup-seconds", "Infinity")]
    [TestCase("--warmup-seconds", "-1")]
    [TestCase("--warmup-seconds", "120.1")]
    [TestCase("--duration", "NaN")]
    [TestCase("--start", "-1")]
    [TestCase("--duration", "2")]
    [TestCase("--scroll-speed", "Infinity")]
    [TestCase("--input-offset-ms", "NaN")]
    [TestCase("--input-offset-ms", "501")]
    [TestCase("--note-height-scale", "NaN")]
    [TestCase("--note-height-scale", "Infinity")]
    [TestCase("--note-height-scale", "0.009")]
    [TestCase("--note-height-scale", "5.01")]
    [TestCase("--soft-constant-fade-in", "NaN")]
    [TestCase("--soft-constant-fade-in", "Infinity")]
    [TestCase("--soft-constant-fade-in", "-1001")]
    [TestCase("--soft-constant-fade-in", "1001")]
    [TestCase("--update-hz", "NaN")]
    [TestCase("--update-hz", "-1")]
    [TestCase("--draw-hz", "Infinity")]
    [TestCase("--reference-bpm", "999")]
    [TestCase("--long-note-mode", "missing")]
    [TestCase("--skin", "999")]
    [TestCase("--unknown", "value")]
    [TestCase("--filter", "another-entry")]
    public void RejectsInvalidCaptureSettings(string argument, string value)
        => Assert.Throws<ArgumentException>(() => BmsGameplayDiagnosticOptions.Parse([.. required, argument, value]));

    [TestCase("0", 0)]
    [TestCase("10.5", 10.5)]
    [TestCase("120", 120)]
    public void SupportsPlaybackWarmup(string value, double expected)
    {
        var options = BmsGameplayDiagnosticOptions.Parse([.. required, "--start", "120", "--warmup-seconds", value]);
        Assert.That(options.WarmupSeconds, Is.EqualTo(expected));
        Assert.That(options.Start, Is.EqualTo(120));
        Assert.That(options.Duration, Is.EqualTo(30));
    }

    [Test]
    public void WarmupMustLeaveMeasuredPlayback()
    {
        Assert.That(BmsGameplayDiagnosticOptions.Parse([.. required, "--warmup-seconds", "29.5"]).WarmupSeconds, Is.EqualTo(29.5));
        Assert.Throws<ArgumentException>(() => BmsGameplayDiagnosticOptions.Parse([.. required, "--warmup-seconds", "30"]));
        Assert.Throws<ArgumentException>(() => BmsGameplayDiagnosticOptions.Parse([.. required, "--start", "10", "--duration", "3", "--warmup-seconds", "13"]));
    }

    [Test]
    public void PreservesExistingReports()
    {
        Directory.CreateDirectory(required[^1]);
        File.WriteAllText(Path.Combine(required[^1], "summary.json"), "baseline");
        Assert.Throws<ArgumentException>(() => BmsGameplayDiagnosticOptions.Parse(required));
    }

    [Test]
    public void RequiresExplicitFilterAndCompleteArguments()
    {
        Assert.Throws<ArgumentException>(() => BmsGameplayDiagnosticOptions.Parse(required[2..]));
        Assert.Throws<ArgumentException>(() => BmsGameplayDiagnosticOptions.Parse([.. required, "--duration"]));
    }
}
