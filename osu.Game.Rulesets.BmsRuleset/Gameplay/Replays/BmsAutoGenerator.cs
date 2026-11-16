using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;

public class BmsAutoGenerator : AutoGenerator<BmsReplayFrame>
{
    private readonly BmsEngineChart compiled;
    private readonly bool scratchOnly;

    public BmsAutoGenerator(BmsBeatmap beatmap, IReadOnlyList<Mod>? mods = null, bool scratchOnly = false)
        : this(beatmap, new BmsEngineChart(beatmap, GameplayProfile.FromMods(mods ?? [])), scratchOnly)
    {
    }

    internal BmsAutoGenerator(BmsBeatmap beatmap, BmsEngineChart compiled, bool scratchOnly = false)
        : base(beatmap)
    {
        this.compiled = compiled;
        this.scratchOnly = scratchOnly;
    }

    internal readonly record struct ActionPoint(double Time, BmsAction Action, bool Press);

    protected override void GenerateFrames()
    {
        var active = new List<BmsAction>();

        foreach (var point in GenerateActionPoints().OrderBy(p => p.Time).ThenBy(p => p.Press))
        {
            if (point.Press)
            {
                if (active.Contains(point.Action))
                    continue;

                active.Add(point.Action);
            }
            else if (!active.Remove(point.Action))
                continue;

            Frames.Add(new BmsReplayFrame(point.Time, active.ToArray()));
        }
    }

    internal IEnumerable<ActionPoint> GenerateActionPoints()
    {
        var beatmap = (BmsBeatmap)Beatmap;
        foreach (var point in compiled.Profile.GenerateAutoplayActions(beatmap, compiled))
        {
            if (!scratchOnly || (BmsKeyBindingConfiguration.ActionToColumn(point.Action, beatmap.LayoutVariant) is { } column && BmsLayout.IsScratchColumn(column, beatmap.LayoutVariant)))
                yield return point;
        }
    }
}
