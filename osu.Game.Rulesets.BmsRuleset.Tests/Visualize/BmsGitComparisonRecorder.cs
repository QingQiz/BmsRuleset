#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

internal sealed class BmsGitComparisonRecorder : IDisposable
{
    private const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static BmsGitComparisonRecorder? current;
    private readonly BmsPlayfield playfield;
    private readonly BmsScoreProcessor score;
    private readonly BmsHealthProcessor health;
    private readonly List<ScopedMethodProbe> probes = [];
    private readonly List<object> rows = [];
    private readonly List<object> visuals = [];
    private readonly Dictionary<BmsHitObject, int> identities;
    private int results;
    private int samples;
    private int ticks;
    private int hitLights;
    private int reverts;

    internal BmsGitComparisonRecorder(BmsPlayfield playfield, ScoreProcessor score, HealthProcessor health)
    {
        if (current != null) throw new InvalidOperationException("Git comparison observers must run sequentially.");
        this.playfield = playfield;
        this.score = (BmsScoreProcessor)score;
        this.health = (BmsHealthProcessor)health;
        identities = playfield.Beatmap.HitObjects.Select((n, i) => (n, i)).ToDictionary(p => p.n, p => p.i);
        current = this;
        try
        {
            foreach (var name in new[] { "PlaySample", "PlayKeySound" })
                probe(typeof(BmsColumnKeySound).GetMethod(name, instance)!);
            probe(typeof(BmsColumn).GetMethod("TriggerHitExplosion", instance)!);
            foreach (var name in new[] { "ApplySyntheticLongNoteEndpoint", "RegisterNonConsumingBad", "RegisterEmptyPoor", "requestJudgementDisplay" })
                probe(typeof(BmsPlayfield).GetMethod(name, instance | BindingFlags.IgnoreCase)!);
            probe(typeof(BmsHealthProcessor).GetMethod("ApplyHellChargeTick", instance)!);
            playfield.NewResult += newResult;
            playfield.RevertResult += revert;
        }
        catch { Dispose(); throw; }
    }

    private void probe(MethodInfo method) => probes.Add(new ScopedMethodProbe(method,
        typeof(BmsGitComparisonRecorder).GetMethod(nameof(before), BindingFlags.Static | BindingFlags.NonPublic),
        typeof(BmsGitComparisonRecorder).GetMethod(nameof(after), BindingFlags.Static | BindingFlags.NonPublic)));

    private static void before(object __instance, MethodBase __originalMethod, object[] __args) => current?.observe(__instance, __originalMethod.Name, __args, false);
    private static void after(object __instance, MethodBase __originalMethod, object[] __args) => current?.observe(__instance, __originalMethod.Name, __args, true);

    private void observe(object source, string name, object[] arguments, bool after)
    {
        if (source is BmsColumn column && !ReferenceEquals(playfield.Stage.Columns[column.Index], column)) return;
        if (source is BmsPlayfield field && !ReferenceEquals(field, playfield)) return;
        if (source is BmsHealthProcessor processor && !ReferenceEquals(processor, health)) return;
        var columnIndex = (source as BmsColumn)?.Index;
        if (source is BmsColumnKeySound)
        {
            columnIndex = Array.FindIndex(playfield.Stage.Columns, c => ReferenceEquals(typeof(BmsColumn).GetField("keySound", instance)!.GetValue(c), source));
            if (columnIndex < 0) return;
        }
        if (name == "registerEmptyPoor") name = "RegisterEmptyPoor";
        if (!after && name == "PlaySample") samples++;
        if (!after && name == "ApplyHellChargeTick") ticks++;
        if (!after && name == "TriggerHitExplosion") hitLights++;
        // Catch-up applies body ticks at their logical deadline, before the current display frame.
        if (source is BmsHealthProcessor)
            Assert.That(arguments[2], Is.Not.Null.And.LessThanOrEqualTo(playfield.Time.Current));
        var args = arguments.Where(a => name != "ApplySyntheticLongNoteEndpoint" || a is BmsLongNoteEndpointResult)
            .Select(a => a is BmsLongNoteEndpointResult e ? endpoint(e)
                : name == "TriggerHitExplosion" ? a is bool hold ? hold ? "Hold" : "Note" : a.ToString() : a).ToArray();
        record(name + (after ? ".after" : ".before"), new { Column = columnIndex, Arguments = args });
    }

    private object endpoint(BmsLongNoteEndpointResult e) => new
    {
        Object = identities[e.Source], e.Kind, e.Result, e.EventTime, e.ApplicationTime, e.ExpectedTime, e.TimeOffset, e.GameplayRate,
    };

    private void newResult(DrawableHitObject drawable, JudgementResult result)
    {
        results++;
        record("result", resultData(result));
    }

    private void revert(JudgementResult result)
    {
        reverts++;
        record("revert", resultData(result));
    }

    private object resultData(JudgementResult result) => new
    {
        Object = result.HitObject is BmsHitObject n && identities.TryGetValue(n, out var id) ? id : -1,
        result.Type, result.TimeAbsolute, result.TimeOffset, result.GameplayRate,
        RawTime = typeof(JudgementResult).GetProperty("RawTime", instance)!.GetValue(result),
        SuppressPenalty = (result as BmsJudgementResult)?.SuppressPenalty,
        Endpoints = result is BmsLongNoteJudgementResult ln ? ln.EndpointResults.Select(endpoint).ToArray() : [],
    };

    private void record(string name, object? data = null) => rows.Add(new
    {
        Event = name, Time = playfield.Time.Current, Data = data,
        Events = score.JudgementEvents.ToArray(), Combo = score.Combo.Value, Score = score.TotalScore.Value,
        ExScore = BmsExScore.CreateProgression(score.JudgementEvents)[^1], Health = health.Health.Value,
        GaugeHistory = health.GaugeHistory.ToArray(),
    });

    internal void Mark(string name)
    {
        record(name);
        visuals.Add(new
        {
            Event = name, Time = playfield.Time.Current,
            Objects = playfield.AllColumnAliveObjects().OfType<DrawableBmsHitObject>().Where(d => d.HitObject != null)
                .OrderBy(d => identities[d.HitObject]).Select(d => new
                {
                    Object = identities[d.HitObject], d.Alpha, d.Y, d.Height, Suppressed = d.VisualsSuppressed,
                    Parts = new[] { "head", "longNoteBody", "longNoteTail" }.Select(n => (Name: n, Drawable: part(d, n)))
                        .Where(p => p.Drawable != null).Select(p => new { p.Name, p.Drawable!.Y, p.Drawable.Height, p.Drawable.Alpha }).ToArray(),
                }).ToArray(),
        });
    }

    private static Drawable? part(Drawable source, string name)
    {
        for (var type = source.GetType(); type != null; type = type.BaseType)
            if (type.GetField(name, instance | BindingFlags.DeclaredOnly)?.GetValue(source) is Drawable drawable) return drawable;
        // The baseline composes the same head in its inherited NoteContainer.
        if (name == "head") return part(source, "noteVisual") ?? part(source, "marker") ?? part(source, "NoteContainer");
        return null;
    }

    internal void Save(string scenario)
    {
        Assert.That(results, Is.GreaterThan(0), "The scenario must submit actual framework results.");
        Assert.That(samples, Is.GreaterThan(0), "The audio observer must see actual sample requests.");
        Assert.That(reverts, Is.GreaterThan(0), "The scenario must restore the framework result stack.");
        Assert.That(hitLights, Is.GreaterThan(0), "The feedback observer must see actual hit-light requests after warmup.");
        if (playfield.Beatmap.LockedLongNoteMode == BmsLongNoteMode.HellChargeNote)
            Assert.That(ticks, Is.GreaterThan(0), "The body observer must see actual HCN ticks after warmup.");
        var json = JsonSerializer.Serialize(new { Scenario = scenario, Results = results, Samples = samples, Ticks = ticks, Reverts = reverts, Rows = rows, Visuals = visuals });
        var directory = Environment.GetEnvironmentVariable("BMS_COMPARISON_RESULTS");
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, scenario + ".json"), json);
        }
        TestContext.WriteLine($"BMS_GIT_COMPARISON {scenario}: results={results}, samples={samples}, ticks={ticks}, reverts={reverts}, rows={rows.Count}");
    }

    public void Dispose()
    {
        playfield.NewResult -= newResult;
        playfield.RevertResult -= revert;
        for (var i = probes.Count - 1; i >= 0; i--) probes[i].Dispose();
        if (ReferenceEquals(current, this)) current = null;
    }
}
