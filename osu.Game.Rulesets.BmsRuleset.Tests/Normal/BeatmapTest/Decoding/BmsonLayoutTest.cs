using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Models;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Filter;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.BeatmapTest.Decoding;

[TestFixture]
public class BmsonLayoutTest
{
    [TestCase("popn-5k", 5)]
    [TestCase("keyboard-24k", 26)]
    [TestCase("keyboard-24k-double", 52)]
    [TestCase("generic-8keys", 8)]
    [TestCase("generic-64keys", 64)]
    public void LayoutSurvivesImportMetadataConversionInputAndFiltering(string mode, int columns)
    {
        var bytes = BmsonParserTest.Chart($$"""[{"name":"a.wav","notes":[{"x":{{columns}},"y":240}]}]""", info: $$""", "mode_hint":"{{mode}}" """);
        var summary = BmsonParser.ParseImportSummary(bytes);
        var info = createInfo();
        BmsDifficultyInfo.FromChartMetadata(summary.Metadata).WriteToOsuDifficulty(info);
        var beatmap = convert(bytes, info);
        Assert.That(beatmap.TotalColumns, Is.EqualTo(columns));
        Assert.That(beatmap.HitObjects.Single().Column, Is.EqualTo(columns - 1));
        Assert.That(BmsDifficultyInfo.GetLayout(info.Difficulty), Is.EqualTo(beatmap.LayoutVariant));
        Assert.That(new BmsRuleset().GetVariantForBeatmap(info), Is.EqualTo((int)beatmap.LayoutVariant));
        Assert.That(new BmsFilterCriteria(null).Matches(info, new FilterCriteria()), Is.True);
        for (var column = 0; column < columns; column++)
        {
            var action = BmsKeyBindingConfiguration.ActionForColumn(beatmap.LayoutVariant, column);
            Assert.That(action, Is.Not.Null);
            Assert.That(BmsKeyBindingConfiguration.ActionToColumn(action!.Value, beatmap.LayoutVariant), Is.EqualTo(column));
            Assert.That(BmsKeyBindingConfiguration.GetDefaultKeyBindings((int)beatmap.LayoutVariant).Any(binding => binding.Action.Equals(action.Value)), Is.True);
        }

        var replay = new BmsAutoGenerator(beatmap).Generate();
        var simulated = BmsReplaySimulation.Run(beatmap, replay, new ScoreInfo { BeatmapInfo = info });
        Assert.That(simulated.Judgements.Count(judgement => judgement.Result == HitResult.Perfect), Is.EqualTo(1));
    }

    [TestCase("keyboard-24k", 26)]
    [TestCase("keyboard-24k-double", 52)]
    [TestCase("generic-8keys", 8)]
    public void KeyboardChargeTailsUseKeyReleaseAndKeyboardWindows(string mode, int lane)
    {
        var beatmap = convert(BmsonParserTest.Chart($$"""[{"name":"a.wav","notes":[{"x":{{lane}},"y":480,"l":240}]}]""",
            info: $$""", "mode_hint":"{{mode}}" """));
        beatmap.LockedLongNoteMode = BmsLongNoteMode.ChargeNote;
        var note = beatmap.HitObjects.OfType<BmsLongNote>().Single();
        var windows = osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.BmsJudgementProfileProvider.GetTable(note, false);
        Assert.That(windows.ResultForOffset(30), Is.EqualTo(HitResult.Perfect));
        Assert.That(windows.ResultForOffset(31), Is.EqualTo(HitResult.Great));
        var replay = new BmsAutoGenerator(beatmap).Generate();
        var result = BmsReplaySimulation.Run(beatmap, replay, new ScoreInfo { BeatmapInfo = beatmap.BeatmapInfo });
        Assert.That(result.Judgements.Count(judgement => judgement.Result == HitResult.Perfect), Is.EqualTo(2));
    }

    [TestCase("popn-5k", 1, 4)]
    [TestCase("generic-8keys", 1, 7)]
    [TestCase("keyboard-24k", 1, 23)]
    [TestCase("keyboard-24k", 26, 25)]
    public void MirrorUsesTheDeclaredLayout(string mode, int lane, int expectedColumn)
    {
        var beatmap = convert(BmsonParserTest.Chart($$"""[{"name":"a.wav","notes":[{"x":{{lane}},"y":240}]}]""",
            info: $$""", "mode_hint":"{{mode}}" """));
        new BmsModMirror().ApplyToBeatmap(beatmap);
        Assert.That(beatmap.HitObjects.Single().Column, Is.EqualTo(expectedColumn));
    }

    private static BeatmapInfo createInfo()
    {
        var info = new BeatmapInfo(new BmsRuleset().RulesetInfo) { Hash = "chart", BeatmapSet = new BeatmapSetInfo() };
        info.BeatmapSet.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = info.Hash }, "chart.bmson"));
        return info;
    }

    private static BmsBeatmap convert(byte[] bytes, BeatmapInfo? info = null) =>
        (BmsBeatmap)new BmsBeatmapConverter(BmsBeatmapDecoder.DecodeBytes(bytes, info ?? createInfo()), new BmsRuleset()).Convert();
}
