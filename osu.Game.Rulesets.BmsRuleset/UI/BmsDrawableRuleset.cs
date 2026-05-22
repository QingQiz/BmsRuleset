using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Input;
using osu.Game.Beatmaps;
using osu.Game.Input.Handlers;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Audio;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI;

/// <inheritdoc />
/// <summary>
///     Minimal native BMS gameplay surface.
/// </summary>
/// <remarks>
///     This class is the first step away from the old mania-backed implementation. It deliberately
///     avoids any mania types and wires BMS hit objects into a native playfield. The visuals are simple
///     placeholders that currently use projected object time for vertical positioning.
/// </remarks>
public partial class BmsDrawableRuleset(Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod>? mods = null) : DrawableRuleset<BmsHitObject>(ruleset, beatmap, mods)
{
    public const double MAX_TIME_RANGE = 11485;

    public override int Variant => (int)((BmsBeatmap)Beatmap).LayoutVariant;

    private readonly BindableDouble configScrollSpeed = new(8);

    public static double ComputeScrollTime(double scrollSpeed) => MAX_TIME_RANGE / Math.Max(1, scrollSpeed);

    public override DrawableHitObject<BmsHitObject>? CreateDrawableRepresentation(BmsHitObject h) => null;

    protected override Playfield CreatePlayfield()
    {
        var beatmap = (BmsBeatmap)Beatmap;
        return new BmsPlayfield(beatmap.HitObjects, beatmap.TotalColumns, beatmap.LayoutVariant, Mods.OfType<BmsModAutoplay>().Any());
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (Config is BmsRulesetConfigManager config)
            config.BindWith(BmsRulesetSetting.ScrollSpeed, configScrollSpeed);

        configScrollSpeed.BindValueChanged(speed => ((BmsPlayfield)Playfield).TimeRange = ComputeScrollTime(speed.NewValue), true);
    }

    protected override PassThroughInputManager CreateInputManager() => new BmsInputManager(Ruleset.RulesetInfo, Variant);

    protected override ReplayInputHandler CreateReplayInputHandler(Replay replay) => new BmsFramedReplayInputHandler(replay);

    protected override ReplayRecorder CreateReplayRecorder(Score score) => new BmsReplayRecorder(score);

    [BackgroundDependencyLoader]
    private void load()
    {
        foreach (var sampleEvent in ((BmsBeatmap)Beatmap).BackgroundSampleEvents.OrderBy(e => e.Time))
        {
            if (((BmsBeatmap)Beatmap).SampleDefinitions.TryGetValue(sampleEvent.SampleKey, out var samplePath))
                FrameStableComponents.Add(new BmsBackgroundSample(sampleEvent.Time, new BmsSampleInfo(samplePath)));
        }
    }

    private partial class BmsBackgroundSample(double startTime, BmsSampleInfo sampleInfo) : BmsSkinnableSound(sampleInfo)
    {
        private const double allowable_late_start = 100;

        private bool hasSeenClockFrame;
        private double previousTime;

        protected override void LoadComplete()
        {
            base.LoadComplete();
            LifetimeStart = startTime;
            LifetimeEnd = double.MaxValue;
        }

        protected override void Update()
        {
            base.Update();

            if (!hasSeenClockFrame)
            {
                hasSeenClockFrame = true;
                previousTime = double.IsFinite(Time.Current - Time.Elapsed) ? Time.Current - Time.Elapsed : Time.Current;
            }

            if (Time.Current < startTime)
            {
                Stop();
                LifetimeStart = startTime;
                LifetimeEnd = double.MaxValue;
                previousTime = Time.Current;
                return;
            }

            if (previousTime < startTime && Time.Current >= startTime && !RequestedPlaying && Time.Current - startTime < allowable_late_start)
                Play();

            previousTime = Time.Current;

            LifetimeStart = double.MinValue;
            LifetimeEnd = RequestedPlaying || HasActiveChannels ? double.MaxValue : startTime;
        }
    }
}
