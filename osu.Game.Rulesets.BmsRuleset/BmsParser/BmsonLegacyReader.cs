using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

internal static class BmsonLegacyReader
{
    internal static JsonElement Normalize(JsonElement source, CancellationToken cancellationToken)
    {
        if (!source.TryGetProperty("soundChannel", out _) || !source.GetProperty("info").TryGetProperty("initBPM", out _))
            throw new InvalidDataException("Legacy BMSON requires soundChannel and info.initBPM.");

        var root = JsonNode.Parse(source.GetRawText())!.AsObject();
        rename(root, "soundChannel", "sound_channels");
        rename(root, "bpmNotes", "bpm_events");
        rename(root, "stopNotes", "stop_events");
        rename(root, "stopEvents", "stop_events");
        var info = root["info"]!.AsObject();
        rename(info, "initBPM", "init_bpm");
        rename(info, "judgeRank", "judge_rank");
        info["resolution"] = 240;
        var lanes = root["sound_channels"]!.AsArray().SelectMany(channel => channel!["notes"]!.AsArray()).ToArray();
        var doublePlay = lanes.Any(note => (note!["x"]?.GetValue<int>() ?? 0) >= 11);
        var sevenKeys = lanes.Any(note => (note!["x"]?.GetValue<int>() ?? 0) is 6 or 7 or 16 or 17);
        info["mode_hint"] = doublePlay ? sevenKeys ? "beat-14k" : "beat-10k" : sevenKeys ? "beat-7k" : "beat-5k";
        foreach (var note in lanes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((note!["x"]?.GetValue<int>() ?? 0) >= 11)
                note["x"] = note["x"]!.GetValue<int>() - 2;
        }

        foreach (var field in new[] { "bpm_events", "stop_events" })
        {
            if (root[field] is JsonArray events)
                foreach (var evt in events)
                    rename(evt!.AsObject(), "v", field == "bpm_events" ? "bpm" : "duration");
        }

        if (root["bga"] is JsonObject bga)
        {
            foreach (var (oldName, newName) in new[] { ("bgaHeader", "bga_header"), ("bgaNotes", "bga_events"), ("layerNotes", "layer_events"), ("poorNotes", "poor_events") })
            {
                rename(bga, oldName, newName);
                if (bga[newName] is JsonArray values)
                    foreach (var value in values)
                        rename(value!.AsObject(), "ID", "id");
            }
        }

        return JsonSerializer.SerializeToElement(root);
    }

    private static void rename(JsonObject parent, string oldName, string newName)
    {
        if (parent.TryGetPropertyValue(oldName, out var value))
        {
            parent.Remove(oldName);
            parent[newName] = value;
        }
    }
}
