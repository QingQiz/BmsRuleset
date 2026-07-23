using System;
using System.IO;
using System.Linq;
using FFmpeg.AutoGen;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Media.FFmpeg;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents.Bga.Video.Supplemental;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Bga;

[TestFixture]
public class BmsSupplementalFFmpegLoaderTest
{
    [Test]
    public void TestLoaderReportsAvailabilityWithoutThrowing()
    {
        Assert.DoesNotThrow(() => BmsSupplementalFFmpegFuncs.TryCreate(out _, out _));
    }

    [Test]
    public void TestDeclinesCleanlyWhenNativeBackendNotEmbedded()
    {
        // A dev build without the native FFmpeg backend embedded must decline (not throw) and
        // name the missing artifact so logs stay actionable; the provider then falls through
        // the chain (framework, then missing-video) without affecting gameplay.
        bool created = BmsSupplementalFFmpegFuncs.TryCreate(out _, out var error);

        if (created)
            Assert.Ignore("Supplemental FFmpeg native backend is embedded in this build.");

        Assert.That(error, Is.Not.Empty);
        Assert.That(error, Does.Contain("bms-ffmpeg"));
        Assert.That(BmsSupplementalFFmpegFuncs.IsAvailable, Is.False);
    }

    [Test]
    public void TestEmbeddedBackendLoadsAndDispatchesNativeCalls()
    {
        if (!BmsSupplementalFFmpegFuncs.IsAvailable)
            Assert.Ignore("Supplemental FFmpeg native backend is not embedded in this build.");

        // TryCreate returning true means the embedded DLL was extracted, NativeLibrary.Loaded,
        // and every imported symbol resolved. Drive a real alloc/free round-trip through the
        // resolved delegates to prove the exports actually dispatch into native code, not just
        // that the function pointers are non-null.
        bool created = BmsSupplementalFFmpegFuncs.TryCreate(out var funcs, out var error);
        Assert.That(created, Is.True, error ?? "TryCreate returned false.");
        Assert.That(funcs, Is.Not.Null);

        unsafe
        {
            AVPacket* pkt = funcs!.av_packet_alloc();
            Assert.That((IntPtr)pkt, Is.Not.EqualTo(IntPtr.Zero), "av_packet_alloc must return non-null.");
            funcs.av_packet_free(&pkt);
            Assert.That((IntPtr)pkt, Is.EqualTo(IntPtr.Zero), "av_packet_free must null the caller's pointer.");
        }
    }

    [Test]
    public void TestDecoderInitsOnRealFixtureMatchingNativeAbi()
    {
        if (!BmsSupplementalFFmpegFuncs.IsAvailable)
            Assert.Ignore("Supplemental FFmpeg native backend is not embedded in this build.");

        // The native DLL must share the FFmpeg.AutoGen 4.3 ABI the managed decoder is compiled
        // against: TryCreate dereferences AVFormatContext/AVStream/AVCodecParameters fields at
        // managed offsets, so a native build from a different FFmpeg major version corrupts the
        // heap and crashes during avformat_open_input/find_stream_info/avcodec_open2. A clean
        // init on real fixture bytes is the tightest proof the embedded backend matches.
        byte[] data = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "bga_fixtures", "mpeg1.mpg"));

        bool created = BmsSupplementalVideoDecoder.TryCreate(data, out var decoder, out var error);
        Assert.That(created, Is.True, error ?? "TryCreate returned false.");
        Assert.That(decoder, Is.Not.Null);
        decoder!.Dispose();
    }

    [Test]
    public void TestDecoderDecodesFrameOnRealFixtureMatchingNativeAbi()
    {
        if (!BmsSupplementalFFmpegFuncs.IsAvailable)
            Assert.Ignore("Supplemental FFmpeg native backend is not embedded in this build.");

        // Init only proves avformat/avcodec open. The decode loop additionally drives
        // avcodec_send_packet/avcodec_receive_frame and sws_scale — and libswscale's x86 asm
        // is linked under -Bsymbolic on Linux, so this is the tightest end-to-end check that
        // the whole pipeline (not just the resolved symbol table) matches the 4.3 ABI.
        byte[] data = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "bga_fixtures", "mpeg1.mpg"));

        bool created = BmsSupplementalVideoDecoder.TryCreate(data, out var decoder, out var error);
        Assert.That(created, Is.True, error ?? "TryCreate returned false.");

        using (decoder!)
        {
            Assert.That(decoder.TryDecodeNextFrame(out var frame, out error), Is.True, error ?? "TryDecodeNextFrame returned false.");
            Assert.That(frame, Is.Not.Null);
            Assert.That(frame!.Width, Is.GreaterThan(0));
            Assert.That(frame.Height, Is.GreaterThan(0));
            Assert.That(frame.PixelCount, Is.EqualTo(frame.Width * frame.Height));
            frame.Dispose();
        }
    }

    [Test]
    public void TestOnlyHostPlatformNativeBackendIsEmbedded()
    {
        // The csproj gates each native EmbeddedResource on the build host's OS so the
        // ruleset DLL carries only the one backend the runtime can load — embedding every
        // platform's would bloat the assembly with dead bytes per unused platform and ship
        // a .so/.dylib the loader never touches. Assert no foreign artifact leaked in.
        string[] resources = typeof(BmsSupplementalFFmpegFuncs).Assembly.GetManifestResourceNames();

        string hostPlatform = OperatingSystem.IsWindows() ? "win-x64"
            : OperatingSystem.IsLinux() ? "linux-x64"
            : OperatingSystem.IsMacOS() ? "osx"
            : "none";

        string[] foreign = resources
            .Where(n => n.Contains("bms-ffmpeg.") && !n.Contains(hostPlatform))
            .ToArray();

        Assert.That(foreign, Is.Empty,
            $"Foreign native backend(s) [{string.Join(", ", foreign)}] are embedded but the host is '{hostPlatform}'. " +
            "The csproj must gate each native EmbeddedResource on the host OS, not just on Exists(...).");
    }
}
