#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

/// <summary>
/// Observes the no-automation core path captured before P3a-0.7. Probes exist only while a non-parallel baseline test runs.
/// </summary>
internal sealed class BmsCoreSchedulingRecorder : IDisposable
{
    private const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static BmsCoreSchedulingRecorder? current;
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
    internal int ReplayPolls { get; private set; }
    internal int StabilityUpdates { get; private set; }

    internal BmsCoreSchedulingRecorder(BmsPlayfield playfield, BmsScoreProcessor score, BmsHealthProcessor health)
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
            probe(typeof(BmsFramedReplayInputHandler), "CollectReplayInputs", "replay.poll");
            probe(typeof(FrameStabilityContainer), "UpdateSubTree", "stability");
            probe(typeof(BmsColumnHitObjectContainer), "UpdateAfterChildrenLife", "column.layout");
            probe(typeof(BmsColumnHitObjectContainer), "UpdateDeferredLongNotes", "column.deferred");
            probe(typeof(BmsColumnHitObjectContainer), "RemoveDrawable", "representation.remove");
            probe(typeof(BmsColumnHitObjectContainer), "AddDrawable", "representation.add");
            probe(typeof(BmsColumnJudgementContext), "UpdateLandmines", "mines");
            probe(typeof(BmsColumnJudgementContext), "UpdateRegisteredBodies", "bodies");
            probe(typeof(BmsColumnJudgementContext), "TryPressRegisteredNote", "core.press");
            probe(typeof(BmsCoreJudgementSchedule), "CheckPassive", "core.passive");
            probe(typeof(BmsCoreJudgementSchedule), "FinishLongNote", "long.finish");
            probe(typeof(BmsCoreJudgementSchedule), "RunLayout", "core.layout");
            probe(typeof(BmsCoreJudgementSchedule), "RunSubtree", "core.subtree");
            probe(typeof(BmsCoreJudgementSchedule), "RunDeferred", "core.deferred");
            probe(typeof(BmsCoreJudgementSchedule), "CompleteRemoval", "core.removal");
            foreach (var type in ruleTypes)
            {
                probe(type, "TryRelease", "long.release");
                probe(type, "TryRepress", "long.repress");
            }
            probe(typeof(BmsDrawableJudgementOutput), "PublishState", "state.publish",
                [typeof(BmsLongNote), typeof(BmsLongNoteState).MakeByRefType(), typeof(BmsFrameContext).MakeByRefType()]);
            probe(typeof(BmsDrawableJudgementOutput), "Unregister", "index.remove");
            probe(typeof(BmsColumnJudgementContext), "RewindLongNotes", "history.rewind");
            probe(typeof(BmsColumnJudgementContext), "RestoreExecutionState", "index.rebuild");
            probe(typeof(BmsHellChargeNoteJudgementRule), "updateBody", "body.sample");
            probe(typeof(BmsColumn), "PlaySample", "keysound");
            probe(typeof(BmsPlayfield), "ApplyHellChargeTick", "body.tick");
            probe(typeof(BmsPlayfield), "ApplySyntheticLongNoteEndpoint", "endpoint");
            probe(typeof(DrawableHitObject), "onDefaultsApplied", "defaults");
            probe(typeof(DrawableHitObject), "OnApply", "representation.apply", [typeof(HitObjectLifetimeEntry)]);
            probe(typeof(DrawableHitObject), "OnFree", "representation.free", [typeof(HitObjectLifetimeEntry)]);
            probe(typeof(DrawableHitObject), "OnKilled", "representation.killed");
            probe(typeof(DrawableBmsHitObject), "OnKilled", "representation.killed");
            probe(typeof(Playfield), "revertResult", "result.revert");
            // Rebuild callers after their leaves so warmed, inlined rules retain observable checkpoints.
            refreshCaller(typeof(BmsHellChargeBodyTracker), "Update");
            foreach (var type in ruleTypes)
            {
                refreshCaller(type, "TryPress");
                refreshCaller(type, "CheckPassive");
                refreshCaller(type, "Finish");
            }
            refreshCaller(typeof(BmsHellChargeNoteJudgementRule), "UpdateBody");
            foreach (var type in typeof(BmsColumnJudgementContext).GetNestedTypes(BindingFlags.NonPublic)
                         .Where(t => t.Name.EndsWith("NoteOperations")))
                foreach (var method in new[] { "Press", "Passive", "Repress", "Release", "Finish" })
                    refreshCaller(type, method);
            refreshCaller(typeof(BmsColumnJudgementContext), "UpdateRegisteredBodies");
            refreshCaller(typeof(BmsCoreJudgementSchedule), "updateLongNote");
            // Tiered framework callers can devirtualise CheckForResult before this scope observes it.
            refreshCaller(typeof(DrawableHitObject), "UpdateResult");
            refreshCaller(typeof(DrawableHitObject), "UpdateAfterChildren");
            refreshCaller(typeof(DrawableHitObject), "OnKilled");
            refreshCaller(typeof(BmsColumnHitObjectContainer), "UpdateAfterChildrenLife");
            refreshCaller(typeof(BmsColumnHitObjectContainer), "UpdateDeferredLongNotes");
            refreshCaller(typeof(BmsColumnHitObjectContainer), "EndGameplayFrame");
            refreshCaller(typeof(BmsJudgementEngine), "ProcessFrame");
            refreshCaller(typeof(BmsPlayfield), "processJudgementFrame");
            // Replay frame tests can warm input forwarding before QueueInput is observed.
            // Its closed generic parent may also inline the replay leaf before this scope starts.
            refreshCaller(typeof(FramedReplayInputHandler<BmsReplayFrame>), "CollectPendingInputs");
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

    private static readonly Type[] ruleTypes = [typeof(BmsLongNoteJudgementRule), typeof(BmsChargeNoteJudgementRule), typeof(BmsHellChargeNoteJudgementRule)];

    private void probe(Type type, string method, string name, Type[]? parameters = null)
    {
        var target = parameters == null ? type.GetMethod(method, instance | BindingFlags.Static) : type.GetMethod(method, instance, parameters);
        if (target == null)
            throw new MissingMethodException(type.FullName, method);
        names.Add(target, name);
        probes.Add(new ScopedMethodProbe(target,
            typeof(BmsCoreSchedulingRecorder).GetMethod(nameof(before), BindingFlags.Static | BindingFlags.NonPublic),
            typeof(BmsCoreSchedulingRecorder).GetMethod(nameof(after), BindingFlags.Static | BindingFlags.NonPublic)));
    }

    private void refreshCaller(Type type, string method)
    {
        var targets = type.GetMethods(instance | BindingFlags.Static).Where(m => m.Name == method).ToArray();
        if (targets.Length == 0) throw new MissingMethodException(type.FullName, method);
        foreach (var target in targets)
            probes.Add(new ScopedMethodProbe(target,
                typeof(BmsCoreSchedulingRecorder).GetMethod(nameof(keepCallBoundary), BindingFlags.Static | BindingFlags.NonPublic)));
    }

    private static void keepCallBoundary()
    {
    }

    private static void before(object __instance, MethodBase __originalMethod, object[] __args, out bool __state)
    {
        var recorder = current;
        __state = recorder?.Recording == true && recorder.owns(__instance, __args);
        if (!__state)
            return;

        // Long-note removal includes its last passive check; ordinary-note removal observes the
        // framework sample-stop boundary before that check, as in the frozen framework trace.
        if (__originalMethod.Name == "OnKilled" && __instance is DrawableBmsHitObject killed
            && (__originalMethod.DeclaringType == typeof(DrawableBmsHitObject)) != (killed.HitObject is BmsLongNote))
        {
            __state = false;
            return;
        }

        var name = recorder!.names[__originalMethod];
        if (name == "frame.begin") recorder.frame++;
        if (name == "simulation") recorder.step++;
        if (name == "batch") recorder.batch++;
        if (name == "replay.poll") recorder.ReplayPolls++;
        if (name == "stability") recorder.StabilityUpdates++;
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
        BmsColumnJudgementContext context => playfield.Stage.Columns.Take(2).Cast<BmsColumn>().Any(c => ReferenceEquals(c.Judgements, context)),
        BmsDrawableJudgementOutput output => playfield.Stage.Columns.Take(2).Cast<BmsColumn>().Any(c => ReferenceEquals(c.JudgementOutput, output)),
        BmsCoreJudgementSchedule schedule => playfield.Stage.Columns.Take(2).Cast<BmsColumn>().Any(c => ReferenceEquals(c.Judgements.Schedule, schedule)),
        BmsFramedReplayInputHandler => true,
        FrameStabilityContainer => true,
        DrawableBmsHitObject drawable => drawable.HitObject != null && identities.ContainsKey(drawable.HitObject),
        HitObjectLifetimeEntry entry => identities.ContainsKey(entry.HitObject),
        JudgementResult result => identities.ContainsKey(result.HitObject),
        _ => runtimeNote(args) is { } note ? identities.ContainsKey(note) : ReferenceEquals(value, engine),
    };

    private static BmsLongNote? runtimeNote(object[] args) => args.Select(arg => arg switch
    {
        BmsLongNoteRuntime state => state.Hold.Note,
        BmsChargeNoteRuntime state => state.Hold.Note,
        BmsHellChargeNoteRuntime state => state.Hold.Note,
        _ => null,
    }).FirstOrDefault(note => note != null);

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
                   ?? runtimeNote(args);
        if (name.StartsWith("core.") && name is "core.press" or "core.passive")
            name = (note is BmsLongNote ? "long." : "note.") + name[5..];
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
        // Preserve the frozen trace schema while numerical contexts replace clock-dependent arguments.
        if (name == "batch") argumentText = "suppressJudgements=" + format(args.OfType<bool>().First());
        if (name == "bodies") argumentText = "holding=" + format(args.OfType<bool>().Single());
        if (name == "body.sample") argumentText = "currentTime=" + format(args.OfType<BmsFrameContext>().Single().SimulationTime) + "," + argumentText;
        if (endpoint is { } e)
            argumentText += $",endpoint={e.Kind}:{e.Result}@{format(e.EventTime)}:{format(e.ApplicationTime)}:{format(e.GameplayRate)}";
        if (name == "result.notify" && result is BmsLongNoteJudgementResult longResult)
            argumentText += string.Join(",", longResult.EndpointResults.Select(e => $"endpoint={e.Kind}:{e.Result}@{format(e.EventTime)}:{format(e.ApplicationTime)}:{format(e.GameplayRate)}"));
        var time = playfield.Time;
        var location = drawable?.HitObject == null || column < 0 ? "-" : string.Join("/",
            Array.IndexOf(playfield.Stage.Columns[column].HitObjectContainer.AliveEntries.Values.ToArray(), drawable),
            Array.IndexOf(playfield.Stage.Columns[column].HitObjectContainer.AliveObjects.ToArray(), drawable),
            playfield.Stage.Columns[column].HitObjectContainer.IndexOf(drawable));
        var states = drawable == null ? "-" : state(drawable);
        var pending = string.Join(",", playfield.Stage.Columns.Take(2).Cast<BmsColumn>()
            .Select(c => c.Judgements.FirstPendingNote is { } first ? identities[first] : -1));
        var active = string.Join("/", playfield.Stage.Columns.Take(2).Cast<BmsColumn>()
            .Select(c => string.Join(",", c.Judgements.ActiveLongNotes.Select(n => identities[n]))));
        var rawTime = result == null ? null : typeof(JudgementResult).GetProperty("RawTime", instance)!.GetValue(result);
        var resultState = result == null ? "-" : $"{result.Type}@{format(result.TimeAbsolute)}:{format(rawTime)}:{format(result.TimeOffset)}:{format(result.GameplayRate)}:{(result as BmsJudgementResult)?.SuppressPenalty}";
        var status = string.Join(",", score.JudgementEvents.Count, BmsExScore.CreateProgression(score.JudgementEvents)[^1],
            score.Combo.Value, score.TotalScore.Value, format(health.Health.Value));
        Rows.Add(string.Join("|", Rows.Count, frame, step, batch, (sources.TryPeek(out var source) ? source : "host") + "/" + name + edge,
            format(time.Current), format(time.Elapsed), column, note == null ? "-" : identities[note],
            note == null ? "-" : generations.GetValueOrDefault(note), location, argumentText, states, resultState, pending, active, status,
            string.Join(",", playfield.Stage.Columns.Take(2).Cast<BmsColumn>().Select(c => c.Judgements.PressedDirections)),
            drawable?.BindingGeneration.ToString() ?? "-", bodyState(drawable)));
    }

    private string bodyState(DrawableBmsHitObject? drawable)
    {
        if (drawable?.HitObject is not BmsLongNote note || !hasLongNoteObservation(drawable)) return "-";
        var context = ((BmsColumn)playfield.Stage.Columns[note.Column]).Judgements;
        var states = (IEnumerable<BmsHellChargeNoteRuntime>)typeof(BmsColumnJudgementContext).GetField("hellChargeHoldStates", instance)!.GetValue(context)!;
        var tracker = states.FirstOrDefault(s => ReferenceEquals(s.Hold.Note, note))?.Body;
        return context.GetLongNoteState(note) + ";acc=" + (tracker == null ? "0" : format(typeof(BmsHellChargeBodyTracker).GetField("accumulator", instance)!.GetValue(tracker)));
    }

    private string state(DrawableBmsHitObject drawable)
    {
        if (drawable.HitObject == null)
            return "unbound";
        var state = drawable.HitObject is BmsLongNote note && hasLongNoteObservation(drawable)
            ? ((BmsColumn)playfield.Stage.Columns[note.Column]).Judgements.GetLongNoteState(note) : (BmsLongNoteState?)null;
        return string.Join(",", identities[drawable.HitObject], drawable.Result?.Type,
            state?.HeadJudged, state?.TailJudged, drawable.HitObject is BmsLongNote ? false : (bool?)null);
    }

    private bool hasLongNoteObservation(DrawableBmsHitObject drawable)
    {
        // The fixture observes representation state. Logical history now survives OnFree independently.
        var output = ((BmsColumn)playfield.Stage.Columns[drawable.HitObject.Column]).JudgementOutput;
        return ((System.Collections.IDictionary)typeof(BmsDrawableJudgementOutput).GetField("presentationObservers", instance)!.GetValue(output)!).Contains(drawable);
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
