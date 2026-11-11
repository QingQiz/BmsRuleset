using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Difficulty;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Filter;

internal sealed class BmsSongSelectDifficultyCache
{
    private const int magic = 0x44534D42; // BMSD
    private const int record_size = 32 + sizeof(byte) + sizeof(double);
    private const int write_batch_size = 256;
    private readonly string path;
    private readonly Dictionary<(string Hash, bool HideScratch), double> ratings;
    private readonly List<(string Hash, bool HideScratch, double Stars)> pending = [];
    private readonly Lock sync = new();

    internal BmsSongSelectDifficultyCache(string path)
    {
        this.path = path;
        ratings = load(path);
    }

    internal bool Contains(string hash, bool hideScratch)
    {
        lock (sync)
            return ratings.ContainsKey((hash, hideScratch));
    }

    internal double GetStars(BeatmapInfo beatmap, bool hideScratch)
    {
        if (beatmap.Ruleset.ShortName == Constant.SHORT_NAME && !hideScratch)
            return beatmap.StarRating;

        lock (sync)
            return ratings.GetValueOrDefault((beatmap.Hash, hideScratch), -1);
    }

    internal void Store(string hash, bool hideScratch, double stars)
    {
        lock (sync)
        {
            if (!ratings.TryAdd((hash, hideScratch), stars))
                return;

            pending.Add((hash, hideScratch, stars));
            if (pending.Count >= write_batch_size)
                Flush();
        }
    }

    internal void Flush()
    {
        lock (sync)
        {
            if (pending.Count == 0)
                return;

            try
            {
                using (var writer = new BinaryWriter(File.Open(path, FileMode.Open, FileAccess.Write, FileShare.Read)))
                {
                    var previousLength = writer.BaseStream.Length;
                    writer.BaseStream.Position = previousLength;
                    try
                    {
                        foreach (var (hash, hideScratch, stars) in pending)
                            writeRecord(writer, hash, hideScratch, stars);
                        writer.Flush();
                    }
                    catch (IOException)
                    {
                        // Retry the whole batch without leaving a partial record between batches.
                        writer.BaseStream.SetLength(previousLength);
                        throw;
                    }
                }

                pending.Clear();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                BmsLogger.Error(exception, "Failed to append BMS song-select difficulty cache.");
            }
        }
    }

    private static Dictionary<(string Hash, bool HideScratch), double> load(string path)
    {
        var loaded = new Dictionary<(string Hash, bool HideScratch), double>();
        try
        {
            if (File.Exists(path))
            {
                using var reader = new BinaryReader(File.OpenRead(path));
                if (reader.BaseStream.Length >= 2 * sizeof(int) && reader.ReadInt32() == magic
                                                                && reader.ReadInt32() == BmsDifficultyCalculator.DIFFICULTY_VERSION)
                {
                    while (reader.BaseStream.Length - reader.BaseStream.Position >= record_size)
                        loaded[(Convert.ToHexStringLower(reader.ReadBytes(32)), reader.ReadBoolean())] = reader.ReadDouble();

                    if (reader.BaseStream.Position == reader.BaseStream.Length)
                        return loaded;
                    // An interrupted append may leave a partial final record. Retain completed records.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            BmsLogger.Error(exception, "Failed to read BMS song-select difficulty cache.");
        }

        rewrite(path, loaded);
        return loaded;
    }

    private static void rewrite(string path, Dictionary<(string Hash, bool HideScratch), double> loaded)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var writer = new BinaryWriter(File.Create(path + ".tmp")))
            {
                writer.Write(magic);
                writer.Write(BmsDifficultyCalculator.DIFFICULTY_VERSION);
                foreach (var (key, stars) in loaded)
                    writeRecord(writer, key.Hash, key.HideScratch, stars);
            }

            File.Move(path + ".tmp", path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            BmsLogger.Error(exception, "Failed to rewrite BMS song-select difficulty cache.");
        }
    }

    private static void writeRecord(BinaryWriter writer, string hash, bool hideScratch, double stars)
    {
        writer.Write(Convert.FromHexString(hash));
        writer.Write(hideScratch);
        writer.Write(stars);
    }
}
