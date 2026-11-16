using RhythmEngine.Profiles.Bms;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

/// <summary>Maps host layouts, mod selections and imported objects to engine-owned BMS definitions.</summary>
internal static class BmsEngineRules
{
    internal static BmsJudgementRules ForLayout(BmsLayoutVariant layout, BmsJudgementModifiers modifiers = BmsJudgementModifiers.None)
        => BmsJudgementRules.For(layout switch
        {
            BmsLayoutVariant.Bms5K or BmsLayoutVariant.Bms5K2P or BmsLayoutVariant.Bms5KDouble => BmsJudgementMode.FiveKey,
            BmsLayoutVariant.Popn5K or BmsLayoutVariant.Popn9K or BmsLayoutVariant.Popn9K2P or BmsLayoutVariant.Popn9KDouble => BmsJudgementMode.Pms,
            BmsLayoutVariant.Keyboard24K or BmsLayoutVariant.Keyboard24KDouble => BmsJudgementMode.Keyboard,
            _ when BmsLayout.IsGeneric(layout) => BmsJudgementMode.Keyboard,
            _ => BmsJudgementMode.SevenKey,
        }, modifiers);

    internal static BmsJudgementRules ForBeatmap(IBmsBeatmap beatmap, BmsJudgementModifiers modifiers = BmsJudgementModifiers.None) => ForLayout(beatmap.LayoutVariant, modifiers);

    internal static double RateFor(BmsHitObject note, BmsJudgementModifiers modifiers = BmsJudgementModifiers.None)
        => ForBeatmap(note.Beatmap, modifiers).ResolveRate(note.Beatmap.Rank, note.ExRank ?? note.Beatmap.ExRank);

    internal static double RateForRank(BmsLayoutVariant layout, int rank) => ForLayout(layout).RateForRank(rank);

    internal static double RateForExRank(BmsLayoutVariant layout, double exRank) => ForLayout(layout).RateForExRank(exRank);

    internal static BmsTimingDefinition GetTiming(BmsLayoutVariant layout, int column, int rank, bool tail,
                                                  BmsJudgementModifiers modifiers = BmsJudgementModifiers.None)
        => GetTiming(layout, column, ForLayout(layout, modifiers).RateForRank(rank), tail, modifiers);

    internal static BmsTimingDefinition GetTiming(BmsLayoutVariant layout, int column, double judgementRate, bool tail,
                                                  BmsJudgementModifiers modifiers = BmsJudgementModifiers.None)
        => ForLayout(layout, modifiers).GetProfile(judgementRate).GetTiming(BmsLayout.IsScratchColumn(column, layout), tail);

    internal static BmsTimingDefinition GetTiming(BmsHitObject note, bool tail, BmsJudgementModifiers modifiers = BmsJudgementModifiers.None)
        => GetTiming(note.Beatmap.LayoutVariant, note.Column, RateFor(note, modifiers), tail, modifiers);
}
