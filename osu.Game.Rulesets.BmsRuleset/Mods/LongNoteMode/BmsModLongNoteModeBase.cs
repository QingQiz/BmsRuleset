using System;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods.LongNoteMode;

public abstract class BmsModLongNoteModeBase : Mod, IApplicableAfterBeatmapConversion
{

    public override ModType Type => ModType.Fun;

    public override Type[] IncompatibleMods =>
        new[]
        {
            typeof(BmsModLongNote),
            typeof(BmsModChargeNote),
            typeof(BmsModHellChargeNote),
        }.Where(t => t != GetType()).ToArray();

    protected abstract BmsLongNoteMode TargetMode { get; }

    public void ApplyToBeatmap(IBeatmap beatmap)
    {
        if (beatmap is not BmsBeatmap bmsBeatmap)
            return;

        if (bmsBeatmap.LockedLongNoteMode != BmsLongNoteMode.Undefined)
            return;

        bmsBeatmap.LockedLongNoteMode = TargetMode;
    }
}
