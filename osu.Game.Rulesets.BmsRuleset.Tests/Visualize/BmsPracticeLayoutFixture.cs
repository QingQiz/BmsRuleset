using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using osu.Game.Beatmaps;
using osu.Game.Models;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

internal static class BmsPracticeLayoutFixture
{
    internal static BmsBeatmap Create(BmsLayoutVariant layout)
    {
        var channels = channelsFor(layout);
        List<string> lines =
        [
            "#BPM 120",
            "#LNTYPE 1",
            "#LNMODE 2",
            "#RANK 2",
            "#TOTAL 200",
            $"#TITLE Layout test fixture: {layout}",
            "#ARTIST Deterministic generated test chart",
        ];

        for (var measure = 2; measure < 50; measure++)
        {
            for (var column = 0; column < channels.Length; column++)
            {
                var channel = channels[column];
                if (measure == 3 || measure % 8 == 4 && column % 3 == measure / 8 % 3)
                {
                    var cells = new bool[16];
                    cells[0] = cells[12] = true;
                    lines.Add($"#{measure:000}{(char)(channel[0] + 4)}{channel[1]}:{payload(cells)}");
                    continue;
                }

                var notes = new bool[16];
                var phase = measure / 8 % 3;
                var offset = (column * 3 + measure) % notes.Length;
                if (measure == 2 || phase > 0 || (column + measure) % 3 == 0)
                    notes[offset] = true;
                if (phase == 1 && column % 4 == measure % 4)
                    notes[(offset + 8) % notes.Length] = true;
                if (phase == 2)
                {
                    for (var step = 1; step < 4; step++)
                        notes[(offset + step * 4) % notes.Length] = true;
                }
                if (notes.Any(value => value))
                    lines.Add($"#{measure:000}{channel}:{payload(notes)}");

                if (measure == 6)
                    lines.Add($"#{measure:000}{(channel[0] == '1' ? 'D' : 'E')}{channel[1]}:0000000A");
            }
        }

        var path = layout is BmsLayoutVariant.Pms9K or BmsLayoutVariant.Pms9KDouble ? "layout-fixture.pms" : "layout-fixture.bms";
        var info = new BeatmapInfo { Hash = path, BeatmapSet = new BeatmapSetInfo() };
        info.BeatmapSet.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = info.Hash }, path));
        var decoded = BmsBeatmapDecoder.DecodeBytes(Encoding.UTF8.GetBytes(string.Join('\n', lines)), info, _ => 1);
        var beatmap = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()) { BranchRandomValueSelector = _ => 1 }.Convert();
        if (BmsLayout.Is2P(layout))
            new BmsModSecondPlayer().ApplyToBeatmap(beatmap);
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, new BmsRuleset().RulesetInfo, bpm: 120);
        beatmap.BeatmapInfo.DifficultyName = layout.ToString();
        return beatmap;
    }

    internal static Mod[] ModsFor(BmsLayoutVariant layout) => BmsLayout.Is2P(layout) ? [new BmsModSecondPlayer()] : [];

    private static string payload(bool[] cells) => string.Concat(cells.Select(value => value ? "01" : "00"));

    private static string[] channelsFor(BmsLayoutVariant layout) => layout switch
    {
        BmsLayoutVariant.Bms5K or BmsLayoutVariant.Bms5K2P => ["16", "11", "12", "13", "14", "15"],
        BmsLayoutVariant.Bme7K or BmsLayoutVariant.Bme7K2P => ["16", "11", "12", "13", "14", "15", "18", "19"],
        BmsLayoutVariant.Pms9K => ["11", "12", "13", "14", "15", "18", "19", "16", "17"],
        BmsLayoutVariant.Bms5KDouble => ["16", "11", "12", "13", "14", "15", "21", "22", "23", "24", "25", "26"],
        BmsLayoutVariant.Bme7KDouble => ["16", "11", "12", "13", "14", "15", "18", "19", "21", "22", "23", "24", "25", "28", "29", "26"],
        BmsLayoutVariant.Pms9KDouble => ["11", "12", "13", "14", "15", "16", "17", "18", "19", "21", "22", "23", "24", "25", "26", "27", "28", "29"],
        _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, "Unsupported practice layout fixture."),
    };
}
