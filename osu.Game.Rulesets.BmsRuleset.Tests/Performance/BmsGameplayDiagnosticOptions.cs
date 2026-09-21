#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Mods.LongNoteMode;
using osu.Game.Rulesets.BmsRuleset.Tests.Visualize;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

internal sealed class BmsGameplayDiagnosticOptions
{
    public string Chart { get; private set; } = "";

    public string Output { get; private set; } = "";

    public double Start { get; private set; }

    public double Duration { get; private set; } = 30;

    public double ScrollSpeed { get; private set; } = 8;

    public double UpdateHz { get; private set; }

    public double DrawHz { get; private set; }

    public int HitExplosionLimit { get; private set; }

    public BmsHitExplosionOverflowPolicy HitExplosionPolicy { get; private set; }

    public bool Headless { get; private set; }

    public bool AudioOutput { get; private set; }

    public bool CaptureAudio { get; private set; }

    public bool ShowInvisibleNotes { get; private set; }

    public bool Invert { get; private set; }

    public int? InvertRandomSeed { get; private set; }

    public BmsTestSkins.SkinKind Skin { get; private set; } = BmsTestSkins.SkinKind.Argon;

    public BmsLongNoteMode LongNoteMode { get; private set; }

    public BmsReferenceBpmMode ReferenceBpm { get; private set; } = BmsReferenceBpmMode.MainBpm;

    public static BmsGameplayDiagnosticOptions Parse(string[] args)
    {
        var result = new BmsGameplayDiagnosticOptions();
        string? filter = null;
        for (var i = 0; i < args.Length; i++)
        {
            var argument = args[i];
            if (argument == "--invert")
            {
                result.Invert = true;
                continue;
            }

            if (argument == "--headless")
            {
                result.Headless = true;
                continue;
            }

            if (argument == "--audio-output")
            {
                result.AudioOutput = true;
                continue;
            }

            if (argument == "--capture-audio")
            {
                result.CaptureAudio = true;
                continue;
            }

            if (argument == "--show-invisible-notes")
            {
                result.ShowInvisibleNotes = true;
                continue;
            }

            if (++i == args.Length)
                throw new ArgumentException($"Missing value for {argument}.");

            var value = args[i];
            switch (argument)
            {
                case "--filter": filter = value; break;

                case "--chart": result.Chart = Path.GetFullPath(value); break;

                case "--output": result.Output = Path.GetFullPath(value); break;

                case "--start": result.Start = number(value, 0, 86400); break;

                case "--duration": result.Duration = number(value, 3, 3600); break;

                case "--scroll-speed": result.ScrollSpeed = number(value, 1, 50); break;

                case "--update-hz": result.UpdateHz = number(value, 0, 10000); break;

                case "--draw-hz": result.DrawHz = number(value, 0, 10000); break;

                case "--hit-explosion-limit":
                    var limit = number(value, 0, 256);
                    if (limit != Math.Truncate(limit))
                        throw new ArgumentException("Hit explosion limit must be an integer.");
                    result.HitExplosionLimit = (int)limit;
                    break;

                case "--hit-explosion-policy": result.HitExplosionPolicy = enumValue<BmsHitExplosionOverflowPolicy>(value); break;

                case "--skin": result.Skin = enumValue<BmsTestSkins.SkinKind>(value); break;

                case "--long-note-mode": result.LongNoteMode = enumValue<BmsLongNoteMode>(value); break;

                case "--invert-random-seed": result.InvertRandomSeed = int.Parse(value, CultureInfo.InvariantCulture); break;

                case "--reference-bpm":
                    result.ReferenceBpm = enumValue<BmsReferenceBpmMode>(value);
                    break;

                default: throw new ArgumentException($"Unknown argument: {argument}.");
            }
        }

        if (filter != "gameplay")
            throw new ArgumentException("Select this diagnostic explicitly with --filter gameplay.");
        if (result.InvertRandomSeed.HasValue && !result.Invert)
            throw new ArgumentException("--invert-random-seed requires --invert.");
        if (!File.Exists(result.Chart))
            throw new ArgumentException("--chart must name an existing BMS file with its resources alongside it.");
        if (string.IsNullOrEmpty(result.Output))
            throw new ArgumentException("--output must name a new report directory.");
        if (File.Exists(result.Output) || Directory.Exists(result.Output) && Directory.GetFileSystemEntries(result.Output).Length > 0)
            throw new ArgumentException("Use an empty output directory to preserve previous measurements.");

        return result;
    }

    public Mod[] CreateMods()
    {
        var mods = new List<Mod>();
        if (Invert)
        {
            mods.Add(new BmsModInvert
            {
                RandomiseLength = { Value = InvertRandomSeed.HasValue },
                Seed = { Value = InvertRandomSeed },
            });
        }

        Mod? mode = LongNoteMode switch
        {
            BmsLongNoteMode.LongNote => new BmsModLongNote(),
            BmsLongNoteMode.ChargeNote => new BmsModChargeNote(),
            BmsLongNoteMode.HellChargeNote => new BmsModHellChargeNote(),
            _ => null,
        };
        if (mode != null)
            mods.Add(mode);
        return [.. mods];
    }

    private static T enumValue<T>(string value) where T : struct, Enum
        => Enum.TryParse<T>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed : throw new ArgumentException($"Invalid {typeof(T).Name}: {value}.");

    private static double number(string value, double min, double max)
    {
        var number = double.Parse(value, CultureInfo.InvariantCulture);
        if (!double.IsFinite(number) || number < min || number > max)
            throw new ArgumentException($"Expected a finite number between {min} and {max}.");

        return number;
    }
}
