using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Media.FFmpeg;
using osu.Game.Rulesets.BmsRuleset.Media.Video.Supplemental;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Bga;

[TestFixture]
public class BmsSupplementalFFmpegLoaderTest
{

    [Test]
    public void TestDeclinesCleanlyWhenNativeBackendNotEmbedded()
    {
        // A dev build without the native FFmpeg backend embedded must decline (not throw) and
        // name the missing artifact so logs stay actionable; the provider then falls through
        // the chain (framework, then missing-video) without affecting gameplay.
        var created = BmsSupplementalFFmpegFuncs.TryCreate(out _, out var error);

        if (created)
            Assert.Ignore("Supplemental FFmpeg native backend is embedded in this build.");

        Assert.That(error, Is.Not.Empty);
        Assert.That(error, Does.Contain("bms-ffmpeg"));
        Assert.That(BmsSupplementalFFmpegFuncs.IsAvailable, Is.False);
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
        var data = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "bga_fixtures", "mpeg1.mpg"));

        var created = BmsSupplementalVideoDecoder.TryCreate(data, out var decoder, out var error);
        Assert.That(created, Is.True, error ?? "TryCreate returned false.");

        using (var activeDecoder = decoder!)
        {
            Assert.That(activeDecoder.TryDecodeNextFrame(out var frame, out error), Is.True, error ?? "TryDecodeNextFrame returned false.");
            Assert.That(frame, Is.Not.Null);
            Assert.That(frame!.Width, Is.GreaterThan(0));
            Assert.That(frame.Height, Is.GreaterThan(0));
            frame.Dispose();
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
        var data = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "bga_fixtures", "mpeg1.mpg"));

        var created = BmsSupplementalVideoDecoder.TryCreate(data, out var decoder, out var error);
        Assert.That(created, Is.True, error ?? "TryCreate returned false.");
        Assert.That(decoder, Is.Not.Null);
        decoder!.Dispose();
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
        var created = BmsSupplementalFFmpegFuncs.TryCreate(out var funcs, out var error);
        Assert.That(created, Is.True, error ?? "TryCreate returned false.");
        Assert.That(funcs, Is.Not.Null);

        unsafe
        {
            var pkt = funcs!.av_packet_alloc();
            Assert.That((IntPtr)pkt, Is.Not.EqualTo(IntPtr.Zero), "av_packet_alloc must return non-null.");
            funcs.av_packet_free(&pkt);
            Assert.That((IntPtr)pkt, Is.EqualTo(IntPtr.Zero), "av_packet_free must null the caller's pointer.");
        }
    }

    [Test]
    public void TestLoaderReportsAvailabilityWithoutThrowing()
    {
        Assert.DoesNotThrow(() => BmsSupplementalFFmpegFuncs.TryCreate(out _, out _));
    }

    [Test]
    public void TestOnlyCompressedNativeBackendsAreEmbedded()
    {
        var resources = typeof(BmsSupplementalFFmpegFuncs).Assembly.GetManifestResourceNames();
        var uncompressed = resources
            .Where(name => name is "bms-ffmpeg.win-x64.dll" or "bms-ffmpeg.linux-x64.so" or "bms-ffmpeg.osx.dylib")
            .ToArray();

        Assert.That(uncompressed, Is.Empty);
    }
}
