using System;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.UI;

public partial class BmsHitObjectContainer(BmsPlayfield playfield) : HitObjectContainer
{
    private readonly Dictionary<BmsHitObject, DrawableBmsHitObject> aliveDrawableMap = new();
    private readonly BmsHitObjectLifetimePlanner lifetimePlanner = new(playfield);

    // ReSharper disable once UnusedMethodReturnValue.Global
    public bool TryGetAliveDrawable(BmsHitObject hitObject, out DrawableBmsHitObject? drawable)
        => aliveDrawableMap.TryGetValue(hitObject, out drawable);

    public override void Add(HitObjectLifetimeEntry entry)
    {
        updateEntryLifetime(entry, force: true);
        base.Add(entry);
    }

    protected override void AddDrawable(HitObjectLifetimeEntry entry, DrawableHitObject drawable)
    {
        base.AddDrawable(entry, drawable);

        if (entry.HitObject is BmsHitObject hitObject && drawable is DrawableBmsHitObject bmsDrawable)
            aliveDrawableMap[hitObject] = bmsDrawable;
    }

    protected override void RemoveDrawable(HitObjectLifetimeEntry entry, DrawableHitObject drawable)
    {
        if (entry.HitObject is BmsHitObject hitObject)
            aliveDrawableMap.Remove(hitObject);

        base.RemoveDrawable(entry, drawable);
    }

    protected override void Update()
    {
        base.Update();

        if (!lifetimePlanner.RefreshIfNeeded())
            return;

        foreach (var entry in Entries)
            updateEntryLifetime(entry, refreshPlanner: false);
    }

    private void updateEntryLifetime(HitObjectLifetimeEntry entry, bool force = false, bool refreshPlanner = true)
    {
        if (entry.HitObject is not BmsHitObject hitObject)
            return;

        var plan = refreshPlanner
            ? lifetimePlanner.CreatePlan(hitObject)
            : lifetimePlanner.CreatePlanForCurrentSettings(hitObject);

        if (force || Math.Abs(entry.LifetimeStart - plan.LifetimeStart) >= 1)
            entry.LifetimeStart = plan.LifetimeStart;

        if (!entry.Judged && (force || Math.Abs(entry.LifetimeEnd - plan.LifetimeEnd) >= 1))
            entry.LifetimeEnd = plan.LifetimeEnd;
    }
}

internal sealed class BmsHitObjectLifetimeEntry(HitObject hitObject) : HitObjectLifetimeEntry(hitObject)
{
    protected override double InitialLifetimeOffset => 2500;
}
