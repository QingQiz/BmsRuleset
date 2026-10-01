#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

/// <summary>
/// Observes the unmodified P3 path. Probes exist only while a non-parallel baseline test runs.
/// </summary>
internal sealed class BmsJudgementCheckpointRecorder : IDisposable
{
    private const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static BmsJudgementCheckpointRecorder? current;
    private readonly BmsPlayfield playfield;
    private readonly BmsScoreProcessor score;
    private readonly BmsHealthProcessor health;
    private readonly List<ScopedMethodProbe> probes = [];
    private readonly Dictionary<MethodBase, string> names = [];
    private readonly Dictionary<HitObject, int> identities;
    private readonly Dictionary<HitObject, int> generations = [];
    private readonly Dictionary<DrawableBmsHitObject, HitObject> bindings = [];
    private readonly Stack<string> sources = [];
    private readonly object engine;
    private int frame;
    private int step;
    private int batch;

    internal bool Recording { get; set; }
    internal List<string> Rows { get; } = [];

    internal BmsJudgementCheckpointRecorder(BmsPlayfield playfield, BmsScoreProcessor score, BmsHealthProcessor health)
    {
        if (current != null)
            throw new InvalidOperationException("Checkpoint probes require a non-parallel scope.");

        this.playfield = playfield;
        this.score = score;
        this.health = health;
        identities = playfield.Beatmap.HitObjects.Select((note, id) => (note, id)).ToDictionary(p => (HitObject)p.note, p => p.id);
        engine = typeof(BmsPlayfield).GetField("judgementEngine", instance)!.GetValue(playfield)!;
        foreach (var drawable in playfield.AllColumnAliveObjects().OfType<DrawableBmsHitObject>())
            bind(drawable);

        current = this;
        try
        {
            probe(typeof(BmsPlayfield), "BeginGameplayFrame", "frame.begin");
            probe(typeof(BmsPlayfield), "EndGameplayFrame", "frame.end");
            probe(typeof(BmsPlayfield), "UpdateSubTree", "simulation");
            probe(typeof(BmsPlayfield), "Update", "playfield.update");
            probe(typeof(BmsJudgementEngine), "QueueInput", "input.queue");
            probe(typeof(BmsJudgementEngine), "ProcessFrame", "batch");
            probe(typeof(BmsModAutoScratch), "Update", "auto");
            probe(typeof(BmsColumnHitObjectContainer), "UpdateAfterChildrenLife", "column.layout");
            probe(typeof(BmsColumnHitObjectContainer), "UpdateDeferredLongNotes", "column.deferred");
            probe(typeof(BmsColumnHitObjectContainer), "RemoveDrawable", "representation.remove");
            probe(typeof(BmsColumnHitObjectContainer), "AddDrawable", "representation.add");
            probe(typeof(BmsColumnHitObjectContainer), "UpdateLandmines", "mines");
            probe(typeof(BmsColumnJudgementContext), "UpdateHellChargeBodies", "bodies");
            probe(typeof(BmsColumnJudgementContext), "TryPressNote", "note.press");
            probe(typeof(BmsColumnJudgementContext), "TryPressLongNote", "long.press");
            probe(typeof(BmsColumnJudgementContext), "TryReleaseLongNote", "long.release");
            // Observe the leaf used by other scoped probes; rebuilding its thin caller can inline it.
            probe(typeof(BmsLongNoteJudgementController), "TryRepress", "long.repress");
            probe(typeof(BmsColumnJudgementContext), "CheckPassiveNote", "note.passive");
            probe(typeof(BmsColumnJudgementContext), "CheckPassiveLongNote", "long.passive");
            probe(typeof(BmsColumnJudgementContext), "UpdateLongNotePostResult", "long.finish");
            probe(typeof(BmsColumnJudgementContext), "onHeadJudged", "head.publish");
            probe(typeof(BmsColumnJudgementContext), "Unregister", "index.remove");
            probe(typeof(BmsColumnJudgementContext), "RewindLongNotes", "history.rewind");
            probe(typeof(BmsColumnJudgementContext), "RebuildRewoundIndexes", "index.rebuild");
            probe(typeof(BmsLongNoteJudgementController), "UpdateHellChargeBody", "body.sample");
            probe(typeof(BmsColumn), "PlaySample", "keysound");
            probe(typeof(BmsPlayfield), "ApplyHellChargeTick", "body.tick");
            probe(typeof(BmsPlayfield), "ApplySyntheticLongNoteEndpoint", "endpoint");
            probe(typeof(DrawableHitObject), "onDefaultsApplied", "defaults");
            probe(typeof(DrawableHitObject), "OnApply", "representation.apply", [typeof(HitObjectLifetimeEntry)]);
            probe(typeof(DrawableHitObject), "OnFree", "representation.free", [typeof(HitObjectLifetimeEntry)]);
            probe(typeof(DrawableHitObject), "OnKilled", "representation.killed");
            probe(typeof(Playfield), "revertResult", "result.revert");
            // Full CN traversal can warm these callers before this recorder is installed.
            // Rebuild from leaves outward so prior scoped probes cannot hide synchronous checkpoints by inlining.
            foreach (var method in typeof(BmsColumnJudgementContext).GetMethods(instance).Where(m => m.Name.Contains("IBmsLongNoteJudgementSink")))
                refreshCaller(typeof(BmsColumnJudgementContext), method.Name);
            refreshCaller(typeof(BmsHellChargeBodyTracker), "Update");
            refreshCaller(typeof(BmsLongNoteJudgementController), "UpdateHellChargeBody");
            refreshCaller(typeof(BmsLongNoteJudgementController), "TryHit");
            refreshCaller(typeof(BmsLongNoteJudgementController), "CheckPassiveResult");
            refreshCaller(typeof(BmsLongNoteJudgementController), "UpdatePostResult");
            refreshCaller(typeof(BmsColumnJudgementContext), "TryPressLongNote");
            refreshCaller(typeof(BmsColumnJudgementContext), "CheckPassiveLongNote");
            refreshCaller(typeof(BmsColumnJudgementContext), "UpdateLongNotePostResult");
            refreshCaller(typeof(BmsColumnJudgementContext), "UpdateHellChargeBodies");
            // Earlier scenes may have inlined context calls into the concrete long-note adapters.
            // Recompile those callers after installing the probes, preserving their original bodies.
            foreach (var type in bindings.Keys.OfType<ILongNoteHolder>().Select(d => d.GetType()).Distinct())
            {
                refreshCaller(type, "CheckForResult");
                refreshCaller(type, "UpdateKindPostResultState");
            }
            // Tiered framework callers can devirtualise CheckForResult before this scope observes it.
            refreshCaller(typeof(DrawableHitObject), "UpdateResult");
            refreshCaller(typeof(DrawableHitObject), "UpdateAfterChildren");
            refreshCaller(typeof(DrawableHitObject), "OnKilled");
            refreshCaller(typeof(DrawableBmsHitObject), "UpdateSubTree");
            // Ordinary notes dispatch passive checks from traversal and removal, not CheckForResult.
            foreach (var type in bindings.Keys.Where(d => d.HitObject is BmsNote).Select(d => d.GetType()).Distinct())
            {
                refreshCaller(type, "UpdateSubTree");
                refreshCaller(type, "OnKilled");
            }
            // A warmed layout caller can also inline the virtual post-result bridge through UpdateColumnFrame.
            refreshCaller(typeof(DrawableBmsHitObject), "UpdateColumnFrame");
            foreach (var type in bindings.Keys.OfType<ILongNoteHolder>().Select(d => d.GetType()).Distinct())
                refreshCaller(type, "UpdateDeferredLongNote");
            refreshCaller(typeof(BmsColumnHitObjectContainer), "UpdateAfterChildrenLife");
            refreshCaller(typeof(BmsColumnHitObjectContainer), "UpdateDeferredLongNotes");
            refreshCaller(typeof(BmsColumnHitObjectContainer), "EndGameplayFrame");
            refreshCaller(typeof(BmsJudgementEngine), "ProcessFrame");
            refreshCaller(typeof(BmsPlayfield), "processJudgementFrame");
            // Replay frame tests can warm input forwarding before QueueInput is observed.
            refreshCaller(typeof(BmsPlayfield), "OnPressed");
            refreshCaller(typeof(BmsPlayfield), "OnReleased");
            refreshCaller(typeof(BmsPlayfield), "BeginGameplayFrame");
            refreshCaller(typeof(BmsPlayfield), "Update");
            refreshCaller(typeof(BmsPlayfield), "UpdateSubTree");
            refreshCaller(typeof(BmsPlayfield), "EndGameplayFrame");
            // The real replay host can inline EndGameplayFrame, hiding its source even while the final traversal runs.
            refreshCaller(typeof(BmsDrawableRuleset), "Update");
            refreshCaller(typeof(BmsDrawableRuleset), "UpdateAfterChildren");
            playfield.NewResult += newResult;
            playfield.RevertResult += revertResult;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void probe(Type type, string method, string name, Type[]? parameters = null)
    {
        var target = parameters == null ? type.GetMethod(method, instance) : type.GetMethod(method, instance, parameters);
        if (target == null)
            throw new MissingMethodException(type.FullName, method);
        names.Add(target, name);
        probes.Add(new ScopedMethodProbe(target,
            typeof(BmsJudgementCheckpointRecorder).GetMethod(nameof(before), BindingFlags.Static | BindingFlags.NonPublic),
            typeof(BmsJudgementCheckpointRecorder).GetMethod(nameof(after), BindingFlags.Static | BindingFlags.NonPublic)));
    }

    private void refreshCaller(Type type, string method)
        => probes.Add(new ScopedMethodProbe(type.GetMethod(method, instance)!,
            typeof(BmsJudgementCheckpointRecorder).GetMethod(nameof(keepCallBoundary), BindingFlags.Static | BindingFlags.NonPublic)));

    private static void keepCallBoundary()
    {
    }

    private static void before(object __instance, MethodBase __originalMethod, object[] __args, out bool __state)
    {
        var recorder = current;
        __state = recorder?.Recording == true && recorder.owns(__instance, __args);
        if (!__state)
            return;

        var name = recorder!.names[__originalMethod];
        if (name == "frame.begin") recorder.frame++;
        if (name == "simulation") recorder.step++;
        if (name == "batch") recorder.batch++;
        if (name == "representation.apply" && __instance is DrawableBmsHitObject drawable)
            recorder.bind(drawable);
        recorder.record(name, ">", __instance, __args, __originalMethod);
        recorder.sources.Push(name);
    }

    private static void after(object __instance, MethodBase __originalMethod, object[] __args, bool __state)
    {
        if (!__state)
            return;
        var recorder = current!;
        recorder.sources.Pop();
        recorder.record(recorder.names[__originalMethod], "<", __instance, __args, __originalMethod);
    }

    private bool owns(object value, object[] args) => value switch
    {
        BmsPlayfield field => ReferenceEquals(field, playfield),
        BmsColumn column => column.Index < 2 && ReferenceEquals(playfield.Stage.Columns[column.Index], column),
        BmsColumnHitObjectContainer container => playfield.Stage.Columns.Take(2).Any(c => ReferenceEquals(c.HitObjectContainer, container)),
        BmsColumnJudgementContext context => owns(context.Column, []),
        BmsModAutoScratch => args.Any(a => ReferenceEquals(a, playfield)),
        DrawableBmsHitObject drawable => drawable.HitObject != null && identities.ContainsKey(drawable.HitObject),
        HitObjectLifetimeEntry entry => identities.ContainsKey(entry.HitObject),
        JudgementResult result => identities.ContainsKey(result.HitObject),
        BmsLongNoteJudgementController controller => identities.ContainsKey(controllerNote(controller)),
        _ => ReferenceEquals(value, engine),
    };

    private static BmsLongNote controllerNote(BmsLongNoteJudgementController controller)
        => (BmsLongNote)typeof(BmsLongNoteJudgementController).GetField("ln", instance)!.GetValue(controller)!;

    private void bind(DrawableBmsHitObject drawable)
    {
        bindings[drawable] = drawable.HitObject;
        generations[drawable.HitObject] = generations.GetValueOrDefault(drawable.HitObject) + 1;
        drawable.OnRevertResult -= drawableRevert;
        drawable.OnRevertResult += drawableRevert;
    }

    internal void Mark(string name) => record(name, "=", playfield, []);

    private void newResult(DrawableHitObject drawable, JudgementResult result)
    {
        if (Recording) record("result.notify", "=", drawable, [result]);
    }

    private void revertResult(JudgementResult result)
    {
        if (Recording) record("result.externalRevert", "=", result, []);
    }

    private void drawableRevert(DrawableHitObject drawable, JudgementResult result)
    {
        if (Recording) record("result.drawableRevert", "=", drawable, [result]);
    }

    private void record(string name, string edge, object value, object[] args, MethodBase? method = null)
    {
        var drawable = value as DrawableBmsHitObject ?? args.OfType<DrawableBmsHitObject>().FirstOrDefault();
        var entry = value as HitObjectLifetimeEntry ?? args.OfType<HitObjectLifetimeEntry>().FirstOrDefault();
        var result = value as JudgementResult ?? args.OfType<JudgementResult>().FirstOrDefault();
        var endpoint = args.OfType<BmsLongNoteEndpointResult>().Select(e => (BmsLongNoteEndpointResult?)e).FirstOrDefault();
        var note = (HitObject?)drawable?.HitObject ?? entry?.HitObject ?? result?.HitObject ?? endpoint?.Source
                   ?? args.OfType<BmsHitObject>().FirstOrDefault()
                   ?? (value is BmsLongNoteJudgementController controller ? controllerNote(controller) : null);
        if (note != null && drawable == null)
            drawable = bindings.Keys.FirstOrDefault(d => ReferenceEquals(d.HitObject, note));
        result ??= entry == null ? drawable?.Result : (JudgementResult?)typeof(HitObjectLifetimeEntry).GetField("Result", instance)!.GetValue(entry);

        var column = value switch
        {
            BmsColumn c => c.Index,
            BmsColumnJudgementContext c => c.Index,
            BmsColumnHitObjectContainer c => Array.FindIndex(playfield.Stage.Columns.ToArray(), p => ReferenceEquals(p.HitObjectContainer, c)),
            _ => (note as BmsHitObject)?.Column ?? -1,
        };
        var argumentText = method == null ? "" : string.Join(",", method.GetParameters().Zip(args)
            .Where(p => p.Second is bool or double or int or ushort or Enum)
            .Select(p => p.First.Name + "=" + format(p.Second)));
        if (endpoint is { } e)
            argumentText += $",endpoint={e.Kind}:{e.Result}@{format(e.EventTime)}:{format(e.ApplicationTime)}:{format(e.GameplayRate)}";
        if (name == "result.notify" && result is BmsLongNoteJudgementResult longResult)
            argumentText += string.Join(",", longResult.EndpointResults.Select(e => $"endpoint={e.Kind}:{e.Result}@{format(e.EventTime)}:{format(e.ApplicationTime)}:{format(e.GameplayRate)}"));
        var time = playfield.Time;
        var location = drawable == null || column < 0 ? "-" : string.Join("/",
            Array.IndexOf(playfield.Stage.Columns[column].HitObjectContainer.AliveEntries.Values.ToArray(), drawable),
            Array.IndexOf(playfield.Stage.Columns[column].HitObjectContainer.AliveObjects.ToArray(), drawable));
        var states = name == "auto"
            ? string.Join(";", playfield.Stage.Columns[0].HitObjectContainer.AliveObjects.OfType<DrawableBmsHitObject>().Select(state))
            : drawable == null ? "-" : state(drawable);
        var pending = string.Join(",", playfield.Stage.Columns.Take(2).Cast<BmsColumn>()
            .Select(c => c.Judgements.FirstPendingHead is { } first ? identities[first.HitObject] : -1));
        var active = string.Join("/", playfield.Stage.Columns.Take(2).Cast<BmsColumn>()
            .Select(c => string.Join(",", c.Judgements.ActiveLongNotes.Select(d => identities[d.HitObject]))));
        var rawTime = result == null ? null : typeof(JudgementResult).GetProperty("RawTime", instance)!.GetValue(result);
        var resultState = result == null ? "-" : $"{result.Type}@{format(result.TimeAbsolute)}:{format(rawTime)}:{format(result.TimeOffset)}:{format(result.GameplayRate)}:{(result as BmsJudgementResult)?.SuppressPenalty}";
        var status = string.Join(",", score.JudgementEvents.Count, BmsExScore.CreateProgression(score.JudgementEvents)[^1],
            score.Combo.Value, score.TotalScore.Value, format(health.Health.Value));
        Rows.Add(string.Join("|", Rows.Count, frame, step, batch, (sources.TryPeek(out var source) ? source : "host") + "/" + name + edge,
            format(time.Current), format(time.Elapsed), column, note == null ? "-" : identities[note],
            note == null ? "-" : generations.GetValueOrDefault(note), location, argumentText, states, resultState, pending, active, status));
    }

    private string state(DrawableBmsHitObject drawable)
    {
        if (drawable.HitObject == null)
            return "unbound";
        var controller = drawable.LongNoteJudgement;
        return string.Join(",", identities[drawable.HitObject], drawable.Result?.Type,
            controller?.HeadJudged, controller?.TailJudged, (drawable as ILongNoteHolder)?.IsAutomaticallyHeld);
    }

    private static string format(object? value) => value is IFormattable formattable
        ? formattable.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "-";

    public void Dispose()
    {
        Recording = false;
        playfield.NewResult -= newResult;
        playfield.RevertResult -= revertResult;
        foreach (var drawable in bindings.Keys)
            drawable.OnRevertResult -= drawableRevert;
        for (var i = probes.Count - 1; i >= 0; i--)
            probes[i].Dispose();
        probes.Clear();
        if (ReferenceEquals(current, this)) current = null;
    }
}
