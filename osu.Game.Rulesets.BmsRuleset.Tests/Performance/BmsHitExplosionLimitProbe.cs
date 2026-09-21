#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using osu.Framework.Graphics.Pooling;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

internal enum BmsHitExplosionOverflowPolicy
{
    KeepExisting,
    ReplaceOldest,
}

// An opt-in experiment belongs to the diagnostic process, not the default gameplay policy.
internal sealed class BmsHitExplosionLimitProbe : IDisposable
{
    private static BmsHitExplosionLimitProbe? active;
    private static readonly FieldInfo normal_pool = typeof(BmsColumn).GetField("normalHitExplosionPool", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo long_note_pool = typeof(BmsColumn).GetField("longNoteHitExplosionPool", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly Func<BmsColumn, BmsPlayfield> get_playfield = typeof(BmsColumn).GetProperty("ParentPlayfield", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetGetMethod(true)!.CreateDelegate<Func<BmsColumn, BmsPlayfield>>();
    private readonly Dictionary<BmsColumn, ColumnState> columns = [];
    private readonly ScopedMethodProbe? triggerProbe;
    private readonly ScopedMethodProbe? prewarmProbe;
    private long triggers;
    private long replacements;
    private long dropped;
    private int peakPerKind;
    private int[] originalPrewarmSizes = [];

    public int Limit { get; }
    public BmsHitExplosionOverflowPolicy Policy { get; }

    public BmsHitExplosionLimitProbe(int limit, BmsHitExplosionOverflowPolicy policy = BmsHitExplosionOverflowPolicy.KeepExisting)
    {
        if (limit is < 0 or > 256)
            throw new ArgumentOutOfRangeException(nameof(limit));
        Limit = limit;
        Policy = policy;
        if (limit == 0)
            return;
        if (active != null)
            throw new InvalidOperationException("Only one hit explosion experiment may run at once.");

        active = this;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        triggerProbe = new ScopedMethodProbe(typeof(BmsColumn).GetMethod(nameof(BmsColumn.TriggerHitExplosion))!,
            typeof(BmsHitExplosionLimitProbe).GetMethod(nameof(trigger), flags));
        prewarmProbe = new ScopedMethodProbe(typeof(BmsHitObjectPoolPlan).GetMethod(nameof(BmsHitObjectPoolPlan.CreateHitExplosionSizes), flags)!,
            postfix: typeof(BmsHitExplosionLimitProbe).GetMethod(nameof(limitPrewarm), flags));
    }

    private static void limitPrewarm(int[] __result)
    {
        active!.originalPrewarmSizes = (int[])__result.Clone();
        for (var i = 0; i < __result.Length; i++)
            __result[i] = Math.Min(__result[i], active!.Limit);
    }

    private static bool trigger(BmsColumn __instance, bool isLongNote)
    {
        var experiment = active!;
        experiment.triggers++;
        if (!experiment.columns.TryGetValue(__instance, out var state))
            experiment.columns.Add(__instance, state = new ColumnState(__instance));
        var pulses = isLongNote ? state.LongNote : state.Normal;
        var pool = isLongNote ? state.LongNotePool : state.NormalPool;
        while (pulses.TryPeek(out var expired) && (!expired.IsInUse || expired.Parent != __instance.HitExplosionArea))
            pulses.Dequeue();

        if (pulses.Count >= experiment.Limit)
        {
            if (experiment.Policy == BmsHitExplosionOverflowPolicy.KeepExisting)
            {
                experiment.dropped++;
                return false;
            }

            // Recycle through the framework so both the fade and the animation follow normal
            // pool preparation. Truncating this oldest pulse is the deliberate visual trade-off.
            __instance.HitExplosionArea.Remove(pulses.Dequeue(), false);
            experiment.replacements++;
        }

        var explosion = pool.Get();
        explosion.ApplyPositionOffset(state.Playfield.Stage.HitTargetPositionOffset);
        __instance.HitExplosionArea.Add(explosion);
        pulses.Enqueue(explosion);
        experiment.peakPerKind = Math.Max(experiment.peakPerKind, pulses.Count);
        return false;
    }

    public object Snapshot() => new
    {
        Limit, Policy = Policy.ToString(), Triggers = triggers, Replacements = replacements, Dropped = dropped,
        PeakPerColumnAndKind = peakPerKind, OriginalPrewarmSizes = originalPrewarmSizes,
    };

    public void Dispose()
    {
        triggerProbe?.Dispose();
        prewarmProbe?.Dispose();
        columns.Clear();
        if (ReferenceEquals(active, this))
            active = null;
    }

    private sealed class ColumnState(BmsColumn column)
    {
        public readonly BmsPlayfield Playfield = get_playfield(column);
        public readonly DrawablePool<BmsHitExplosion> NormalPool = (DrawablePool<BmsHitExplosion>)normal_pool.GetValue(column)!;
        public readonly DrawablePool<BmsHitExplosion> LongNotePool = (DrawablePool<BmsHitExplosion>)long_note_pool.GetValue(column)!;
        public readonly Queue<BmsHitExplosion> Normal = [];
        public readonly Queue<BmsHitExplosion> LongNote = [];
    }
}
