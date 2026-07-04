// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using osu.Framework.Graphics;
using NUnit.Framework;
using osu.Framework.Graphics.Video;
using osu.Framework.Logging;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents.Bga.Video.Supplemental;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

/// <summary>
///     Empirically probes whether the framework's bundled FFmpeg can decode the MPEG-1 BGA video
///     shipped with the Aleph-0 test song. The framework loads FFmpeg lazily when a <see cref="Video"/>
///     decoder is constructed, so we create one first (which reproduces the real BGA code path and the
///     fault seen in-game), then query FFmpeg.AutoGen directly to see which demuxers are registered.
/// </summary>
[TestFixture]
public partial class TestSceneBmsBgaVideoProbe : OsuTestScene
{
    // Demuxers of interest for MPEG-1 system multiplex (.mpg), plus known-present ones as sanity checks.
    private static readonly string[] demuxer_names =
    {
        "mpeg",
        "mpegts",
        "mpegtsraw",
        "mpegvideo",
        "avi",
        "flv",
        "asf",
        "mov",
        "matroska",
    };

    // Written from the game thread; read back by the test host. Temp path keeps it location-independent.
    private static readonly string results_file = Path.Combine(Path.GetTempPath(), "bms_bga_probe.txt");

    private string? bgaPath;
    private Video? video;
    private BmsSupplementalVideoDrawable? fallback;

    [Test]
    public void TestProbeBgaVideo()
    {
        AddStep("locate _bga.mpg", () => bgaPath = locateBga() ?? throw new FileNotFoundException("_bga.mpg not found from " + AppContext.BaseDirectory));
        AddStep("create Video from file", () => Child = video = new Video(bgaPath!, startAtCurrentTime: false));
        // The decoder runs on a background thread; wait until it reaches a terminal/running state so that
        // FFmpeg.AutoGen's avformat has been loaded via the framework's GetOrLoadLibrary hook.
        AddUntilStep("decoder settled", () =>
            video != null && (video.IsFaulted || video.State == VideoDecoder.DecoderState.Running || video.FramesProcessed > 0));

        AddStep("report state + probe registered demuxers", () =>
        {
            var lines = new List<string>
            {
                $"file: {bgaPath}",
                $"Video.IsFaulted={video?.IsFaulted}",
                $"Video.State={video?.State}",
                $"Video.FramesProcessed={video?.FramesProcessed}",
                $"Video.Duration={video?.Duration}",
                string.Empty,
                "av_find_input_format (NULL => demuxer NOT compiled into shipped FFmpeg):",
            };

            unsafe
            {
                foreach (var name in demuxer_names)
                {
                    var fmt = ffmpeg.av_find_input_format(name);
                    string extra = string.Empty;

                    if (fmt != null)
                    {
                        try
                        {
                            extra = $" name=\"{Marshal.PtrToStringAnsi((IntPtr)fmt->name) ?? "?"}\"";
                        }
                        catch
                        {
                            // If the name pointer can't be dereferenced for any reason, still report registration.
                        }
                    }

                    lines.Add($"  {name,-12} => {(fmt == null ? "NULL" : "registered" + extra)}");
                }
            }

            foreach (var l in lines)
                Logger.Log($"[probe] {l}", "bms-bga-probe");

            File.WriteAllLines(results_file, lines);
        });

        AddAssert("probe results written", () => File.Exists(results_file));

        AddStep("create MPEG fallback drawable", () =>
        {
            var stream = File.OpenRead(bgaPath!);
            Child = fallback = new BmsSupplementalVideoDrawable(stream, Clock, Clock.CurrentTime)
            {
                RelativeSizeAxes = Axes.Both,
                FillMode = FillMode.Stretch,
            };
        });

        AddUntilStep("fallback decoded a frame", () => fallback?.Stats.DecodedFrames > 0);
        AddUntilStep("fallback uploaded a frame", () => fallback?.UploadedFrames > 0);

        AddStep("report fallback stats", () =>
        {
            var stats = fallback!.Stats;
            var lines = File.ReadAllLines(results_file).ToList();
            lines.Add(string.Empty);
            lines.Add("MPEG fallback:");
            lines.Add($"  UploadedFrames={fallback.UploadedFrames}");
            lines.Add($"  DecodedFrames={stats.DecodedFrames}");
            lines.Add($"  DroppedFrames={stats.DroppedFrames}");
            lines.Add($"  IsFaulted={stats.IsFaulted}");
            lines.Add($"  FaultMessage={stats.FaultMessage ?? string.Empty}");
            File.WriteAllLines(results_file, lines);
        });
    }

    private static string? locateBga()
    {
        const string rel = "bms_test_songs/Aleph-0 (by LeaF)/_bga.mpg";

        if (File.Exists(rel))
            return Path.GetFullPath(rel);

        string baseDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(baseDir, rel)))
            return Path.GetFullPath(Path.Combine(baseDir, rel));

        // Walk up from the test assembly's directory until the test-song tree is found.
        var dir = new DirectoryInfo(baseDir);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        return null;
    }
}
