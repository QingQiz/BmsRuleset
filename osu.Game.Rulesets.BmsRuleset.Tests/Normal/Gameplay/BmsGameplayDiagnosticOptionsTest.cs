using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Mods.LongNoteMode;
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
    public void HitExplosionLimitIsExplicitAndDefaultsToUnlimited()
    {
        Assert.That(BmsGameplayDiagnosticOptions.Parse(required).HitExplosionLimit, Is.Zero);
        Assert.That(BmsGameplayDiagnosticOptions.Parse([.. required, "--hit-explosion-limit", "64"]).HitExplosionLimit, Is.EqualTo(64));
        Assert.That(BmsGameplayDiagnosticOptions.Parse([.. required, "--hit-explosion-limit", "0"]).HitExplosionLimit, Is.Zero);
        Assert.That(BmsGameplayDiagnosticOptions.Parse([.. required, "--hit-explosion-limit", "64", "--hit-explosion-policy", "ReplaceOldest"]).HitExplosionPolicy,
            Is.EqualTo(BmsHitExplosionOverflowPolicy.ReplaceOldest));
    }

    [TestCase("--hit-explosion-limit", "-1")]
    [TestCase("--hit-explosion-limit", "257")]
    [TestCase("--hit-explosion-limit", "1.5")]
    [TestCase("--hit-explosion-policy", "missing")]
    [TestCase("--duration", "NaN")]
    [TestCase("--start", "-1")]
    [TestCase("--duration", "2")]
    [TestCase("--scroll-speed", "Infinity")]
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
