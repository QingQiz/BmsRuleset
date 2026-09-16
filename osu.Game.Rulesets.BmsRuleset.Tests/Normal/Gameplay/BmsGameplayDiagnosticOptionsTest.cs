using System;
using System.IO;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
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
        Assert.That(options.Headless, Is.True);
        Assert.That(options.AudioOutput, Is.False);
        Assert.That(BmsGameplayDiagnosticOptions.Parse([.. required, "--audio-output"]).AudioOutput, Is.True);
    }

    [TestCase("--duration", "NaN")]
    [TestCase("--start", "-1")]
    [TestCase("--duration", "2")]
    [TestCase("--scroll-speed", "Infinity")]
    [TestCase("--reference-bpm", "999")]
    [TestCase("--long-note-mode", "missing")]
    [TestCase("--skin", "999")]
    [TestCase("--unknown", "value")]
    [TestCase("--filter", "another-entry")]
    public void RejectsInvalidCaptureSettings(string argument, string value)
        => Assert.Throws<ArgumentException>(() => BmsGameplayDiagnosticOptions.Parse([.. required, argument, value]));

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
