// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using osu.Framework;
using osu.Framework.Configuration;
using osu.Framework.Platform;
using osu.Game.Rulesets.BmsRuleset.Tests.Audio;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.Tests.Visualize;
using osu.Game.Tests;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

public static class VisualTestRunner
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.FirstOrDefault() is "--practice-result-screenshot" or "--practice-countdown-screenshot")
        {
            using var resultHost = Host.GetSuitableDesktopHost("bms-practice-result", new HostOptions { PortableInstallation = true });
            var result = new BmsPracticeResultScreenshotGame(getOptionalArgument(args, "--output") ?? "artifacts/practice-results",
                getOptionalArgument(args, "--locale") ?? "en", args[0] == "--practice-countdown-screenshot");
            resultHost.Run(result);
            return result.ResultCode;
        }

        if (args.FirstOrDefault() == "--practice-setup-screenshot")
        {
            using var setupHost = Host.GetSuitableDesktopHost("bms-practice-setup", new HostOptions { PortableInstallation = true });
            var setup = new BmsPracticeSetupScreenshotGame(getOptionalArgument(args, "--output") ?? "artifacts/practice-setup",
                getOptionalArgument(args, "--locale") ?? "en", int.Parse(getOptionalArgument(args, "--width") ?? "1280"),
                int.Parse(getOptionalArgument(args, "--height") ?? "900"), getOptionalArgument(args, "--chart"),
                getOptionalArgument(args, "--start") is { } practiceStart ? double.Parse(practiceStart, CultureInfo.InvariantCulture) * 1000 : null,
                getOptionalArgument(args, "--end") is { } practiceEnd ? double.Parse(practiceEnd, CultureInfo.InvariantCulture) * 1000 : null,
                args.Contains("--logical-size"),
                getOptionalArgument(args, "--view-duration") is { } practiceView ? double.Parse(practiceView, CultureInfo.InvariantCulture) * 1000 : null,
                getOptionalArgument(args, "--focus") == "end",
                getOptionalArgument(args, "--layout") is { } practiceLayout ? Enum.Parse<BmsLayoutVariant>(practiceLayout, true) : null,
                args.Contains("--skip-results"), int.Parse(getOptionalArgument(args, "--count-in") ?? "3"),
                getOptionalArgument(args, "--extend-minutes") is { } extendedMinutes ? int.Parse(extendedMinutes) : null,
                getOptionalArgument(args, "--ui-scale") is { } practiceUiScale ? float.Parse(practiceUiScale, CultureInfo.InvariantCulture) : null);
            setupHost.Run(setup);
            return setup.ResultCode;
        }

        if (args.FirstOrDefault() == "--gameplay-diagnostic")
        {
            var options = BmsGameplayDiagnosticOptions.Parse(args.Skip(1).ToArray());
            using var diagnosticHost = options.Headless
                ? new HeadlessGameHost("bms-gameplay-diagnostic", realtime: true)
                : Host.GetSuitableDesktopHost("bms-gameplay-diagnostic");
            var diagnostic = new BmsGameplayDiagnosticGame(options);
            try
            {
                diagnosticHost.Run(diagnostic);
            }
            catch (Exception error)
            {
                diagnostic.Finish(error.ToString());
                Console.Error.WriteLine(error);
            }

            return diagnostic.ResultCode;
        }

        if (args.FirstOrDefault() == "--leaderboard-preview")
        {
            using var previewHost = Host.GetSuitableDesktopHost("bms-leaderboard-preview");
            var preview = new BmsLeaderboardPreviewGame(getOptionalArgument(args, "--output") ?? "artifacts/leaderboard", getOptionalArgument(args, "--locale") ?? "en");
            previewHost.Run(preview);
            return preview.ResultCode;
        }

        if (args.FirstOrDefault() == "--list-wasapi-devices")
        {
            BmsWasapiDeviceEnumerator.PrintDevices();
            return 0;
        }

        if (args.FirstOrDefault() == "--list-bass-devices")
        {
            BmsWasapiDeviceEnumerator.PrintBassDevices();
            return 0;
        }

        if (args.FirstOrDefault() == "--audio-diagnostic")
        {
            using var host = new BmsAudioDiagnosticHost(getOptionalArgument(args, "--audio-device"));
            var game = new BmsAudioDiagnosticGame(args.Skip(1).ToArray());
            host.Run(game);
            return game.ResultCode;
        }

        using (var host = Host.GetSuitableDesktopHost("osu-development"))
        {
            host.Run(new OsuTestBrowser());
            return 0;
        }
    }

    private static string? getOptionalArgument(IReadOnlyList<string> args, string name)
    {
        var index = -1;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == name)
            {
                index = i;
                break;
            }
        }

        return index >= 0 && index + 1 < args.Count ? args[index + 1] : null;
    }

    private sealed class BmsAudioDiagnosticHost(string? audioDevice) : HeadlessGameHost("bms-audio-diagnostic", realtime: true)
    {
        protected override void SetupConfig(IDictionary<FrameworkSetting, object> defaultOverrides)
        {
            base.SetupConfig(defaultOverrides);

            // Headless hosts normally force BASS's no-sound device. The diagnostic needs the same
            // real-time Windows output path as gameplay while still avoiding a visible window.
            var selectedDevice = audioDevice ?? string.Empty;
            defaultOverrides[FrameworkSetting.AudioDevice] = selectedDevice;
            Config.SetValue(FrameworkSetting.AudioDevice, selectedDevice);
        }
    }
}
