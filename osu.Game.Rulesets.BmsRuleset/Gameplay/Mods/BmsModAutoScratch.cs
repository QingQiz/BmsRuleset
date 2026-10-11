using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Icons;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;

public partial class BmsModAutoScratch : Mod, IApplicableToDrawableRuleset<BmsHitObject>, IApplicableToScoreSelection, IBmsActionProducer, IBmsActionFilter
{
    private static readonly BmsAction[] scratchActions = [BmsAction.Scratch, BmsAction.ScratchReverse, BmsAction.P2Scratch, BmsAction.P2ScratchReverse];
    private (double Time, uint Actions)[] frames = [];
    private int nextFrame;
    private uint pressedActions;
    private Action<BmsAction> press = null!;
    private Action<BmsAction> release = null!;
    private double lastTime = double.NegativeInfinity;
    private double nextTime = double.PositiveInfinity;
    private BmsPlayfield playfield = null!;
    private BmsInputManager input = null!;

    private bool usesRecordedInputs => input.ReplayInputHandler is BmsFramedReplayInputHandler { IncludesGeneratedInputs: true };

    void IBmsActionProducer.UpdateActions(double time)
    {
        if (!playfield.IsResumeRewinding && !usesRecordedInputs)
            UpdateActions(time);
    }

    bool IBmsActionFilter.AllowPhysicalAction(BmsAction action) => !isScratch(action);
    bool IBmsActionFilter.AllowReplayAction(BmsAction action) => usesRecordedInputs || !isScratch(action);

    private static bool isScratch(BmsAction action)
        => action is BmsAction.Scratch or BmsAction.ScratchReverse or BmsAction.P2Scratch or BmsAction.P2ScratchReverse;

    public override string Name => BmsStrings.ModAutoScratchName.ToString();

    public override string Acronym => BmsStrings.ModAutoScratchAcronym.ToString();

    public override IconUsage? Icon => BmsIcons.AutoScratch;

    public override LocalisableString Description => BmsStrings.ModAutoScratch;

    public override ModType Type => ModType.Automation;

    public IScoreSelectionRule ScoreSelectionRule => BmsModTypeScoreSelectionRule<BmsModAutoScratch>.REDUCTION;

    public void ApplyToDrawableRuleset(DrawableRuleset<BmsHitObject> drawableRuleset)
    {
        playfield = (BmsPlayfield)drawableRuleset.Playfield;
        input = (BmsInputManager)((BmsDrawableRuleset)drawableRuleset).KeyBindingInputManager;
        PrepareActions(playfield.Beatmap, action => input.KeyBindingContainer.TriggerPressed(action), input.KeyBindingContainer.TriggerReleased);
    }

    internal void PrepareActions(BmsBeatmap beatmap, Action<BmsAction> onPress, Action<BmsAction> onRelease)
    {
        press = onPress;
        release = onRelease;
        var schedule = new List<(double Time, uint Actions)>();
        var actions = 0u;
        foreach (var point in new BmsAutoGenerator(beatmap, scratchOnly: true).GenerateActionPoints().OrderBy(p => p.Time).ThenBy(p => p.Press))
        {
            var direction = 1u << Array.IndexOf(scratchActions, point.Action);
            actions = point.Press ? actions | direction : actions & ~direction;
            // Adjacent holds need a release and a new press even at the same timestamp.
            schedule.Add((point.Time, actions));
        }
        frames = schedule.ToArray();
        nextFrame = 0;
        pressedActions = 0;
        lastTime = double.NegativeInfinity;
        nextTime = frames.Length == 0 ? double.PositiveInfinity : frames[0].Time;
    }

    internal void UpdateActions(double time)
    {
        if (time < lastTime)
        {
            // A seek restores held inputs without replaying every intervening tap.
            var low = 0;
            var high = frames.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (frames[middle].Time <= time) low = middle + 1;
                else high = middle;
            }
            nextFrame = low;
            applyActions(low == 0 ? 0 : frames[low - 1].Actions);
            nextTime = nextFrame < frames.Length ? frames[nextFrame].Time : double.PositiveInfinity;
        }
        lastTime = time;
        if (time < nextTime) return;

        while (nextFrame < frames.Length && frames[nextFrame].Time <= time)
            applyActions(frames[nextFrame++].Actions);
        nextTime = nextFrame < frames.Length ? frames[nextFrame].Time : double.PositiveInfinity;
    }

    private void applyActions(uint targetActions)
    {
        // Release before pressing so tail reversals follow the framework's ordinary input order.
        while (pressedActions != targetActions)
        {
            var released = pressedActions & ~targetActions;
            var index = BitOperations.TrailingZeroCount(released != 0 ? released : targetActions & ~pressedActions);
            pressedActions ^= 1u << index;
            if (released != 0) release(scratchActions[index]);
            else press(scratchActions[index]);
        }
    }
}
