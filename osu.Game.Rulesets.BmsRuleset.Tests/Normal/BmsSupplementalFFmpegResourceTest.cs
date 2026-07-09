using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents.Bga.Video.Supplemental;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal;

public class BmsSupplementalFFmpegResourceTest
{
    [Test]
    public void CurrentHostBuildEmbedsUsableSupplementalBackend()
    {
        Assert.That(BmsSupplementalFFmpegFuncs.IsAvailable, Is.True);
    }

    [Test]
    public void BuildEmbedsCompressedSupplementalBackendsForEveryPlatform()
    {
        var resources = Assembly.GetAssembly(typeof(BmsRuleset))!.GetManifestResourceNames();

        Assert.That(resources, Does.Contain("bms-ffmpeg.win-x64.dll.br"));
        Assert.That(resources, Does.Contain("bms-ffmpeg.linux-x64.so.br"));
        Assert.That(resources, Does.Contain("bms-ffmpeg.osx.dylib.br"));
        Assert.That(resources.Any(name => name is "bms-ffmpeg.win-x64.dll" or "bms-ffmpeg.linux-x64.so" or "bms-ffmpeg.osx.dylib"), Is.False);
    }
}
