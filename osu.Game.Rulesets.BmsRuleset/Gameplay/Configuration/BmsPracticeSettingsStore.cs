using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using osu.Framework.Platform;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;

public sealed class BmsPracticeSettingsStore
{
    private readonly Storage storage;

    public BmsPracticeSettingsStore(Storage storage)
    {
        this.storage = storage.GetStorageForDirectory("bms/practice");
    }

    public BmsPracticeSettings? Load(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return null;

        var filename = getFilename(hash);
        try
        {
            if (!storage.Exists(filename))
                return null;
            using var stream = storage.GetStream(filename, FileAccess.Read, FileMode.Open);
            var settings = JsonSerializer.Deserialize<BmsPracticeSettings>(stream);
            return settings is { IsValid: true } ? settings : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            BmsLogger.Error(exception, "BMS practice settings could not be read.");
            return null;
        }
    }

    public BmsPracticeSettings Load(string hash, BmsBeatmap beatmap)
        => Load(hash) is { } settings && settings.IsValidFor(beatmap) ? settings : BmsPracticeSettings.CreateDefault(beatmap);

    public void Save(string hash, BmsPracticeSettings settings)
    {
        if (string.IsNullOrWhiteSpace(hash) || !settings.IsValid)
            return;

        try
        {
            using var stream = storage.CreateFileSafely(getFilename(hash));
            JsonSerializer.Serialize(stream, settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            BmsLogger.Error(exception, "BMS practice settings could not be saved.");
        }
    }

    private static string getFilename(string hash) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hash))) + ".json";
}
