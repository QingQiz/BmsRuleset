#nullable enable

using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Pooling;
using osu.Framework.Input.Events;
using osu.Framework.Input.States;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsDenseReplay : BmsPlayerTestScene
{
    private const int note_count = 512;
    private static int columnUpdates;
    private static int noteMaskingUpdates;
    private static int explosionMaskingUpdates;
    private static int passiveDeadlineReads;
    private bool allNotesVisible;
    private ScopedMethodProbe? probe;
    private ScopedMethodProbe? maskingProbe;

    [SetUp]
    public void ResetChartOptions() => allNotesVisible = false;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(b => new BmsAutoGenerator(b).Generate().Frames.ToList());

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var chart = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 3 };
        for (var i = 0; i < note_count; i++)
            chart.HitObjects.Add(new BmsNote { StartTime = 3000 + i * 0.0001, Column = i % 8 });
        if (!allNotesVisible)
            chart.HitObjects.Add(new BmsNote { StartTime = 60000, Column = 1 });
        BmsTestBeatmaps.SetupBeatmapInfo(chart, ruleset);
        return chart;
    }

    [Test]
    public void FullyActivatedTapChartCatchesUpWithoutRepeatedSkinTraversal()
    {
        AddStep("load chart with all taps visible", () => { allNotesVisible = true; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        seek(2999);
        AddStep("advance within one frame beyond the visual deferral budget", () =>
        {
            var column = (BmsColumnHitObjectContainer)Playfield.Stage.Columns[1].HitObjectContainer;
            using var counter = new ScopedMethodProbe(typeof(BmsColumnHitObjectContainer).GetMethod("UpdateAfterChildrenLife", BindingFlags.Instance | BindingFlags.NonPublic),
                typeof(TestSceneBmsDenseReplay).GetMethod(nameof(countUpdate), BindingFlags.Static | BindingFlags.NonPublic));
            var manual = new osu.Framework.Timing.ManualClock { CurrentTime = 2999, IsRunning = true };
            var framed = new osu.Framework.Timing.FramedClock(manual);
            var previousClock = column.Clock;
            column.Clock = framed;
            columnUpdates = 0;
            column.BeginGameplayFrame();
            for (var time = 3000; time <= 3040; time += 10)
            {
                manual.CurrentTime = time;
                framed.ProcessFrame();
                column.UpdateSubTree();
            }
            column.EndGameplayFrame();
            column.Clock = previousClock;
            Assert.That(columnUpdates, Is.EqualTo(1), "All candidates already exist; update their skins at the final timestamp.");
        });
    }

    [Test]
    public void ComboWakesAfterAutoHideAndDisplaysOnlyTheFinalBurstCount()
    {
        BmsComboCounter counter = null!;
        AddStep("load player", () => LoadPlayer());
        AddUntilStep("HUD loaded", () => Player.IsLoaded && (counter = Player.HUDOverlay.ChildrenOfType<BmsComboCounter>().SingleOrDefault()!) != null && counter.IsLoaded);
        seek(0);
        AddStep("hide combo below threshold", () => counter.Current.Value = 1);
        AddUntilStep("combo fully hidden", () => counter.Alpha == 0);
        AddStep("apply burst of combo increments", () =>
        {
            for (var i = 1; i <= 1000; i++)
                counter.Current.Value = i;
        });
        AddUntilStep("final count is visible", () => counter.Alpha > 0 && counter.DisplayedCount == 1000);
    }

    [Test]
    public void BatchedTapActivationIncludesTheEarliestInputWindow()
    {
        AddStep("load player", () => LoadPlayer());
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        seek(0);
        AddStep("use maximum scroll speed", () =>
        {
            Playfield.ScrollController.SetConfiguredScrollSpeed(50);
            Playfield.RefreshAllLifetimes();
        });
        AddStep("activation leaves room for one deferred visual update", () =>
        {
            foreach (var entry in Playfield.Stage.Columns.SelectMany(c => c.HitObjectContainer.Entries))
            {
                var note = (BmsNote)entry.HitObject;
                var table = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, note.Column, note.EffectiveJudgementRate, false);
                Assert.That(entry.LifetimeStart, Is.LessThanOrEqualTo(note.StartTime - table.FastWindowFor(HitResult.Miss) - 16));
            }
        });
    }

    [Test]
    public void ComboBatchPreservesBreakFlashAndIncrementAnimation()
    {
        BmsComboCounter counter = null!;
        AddStep("load player", () => LoadPlayer());
        AddUntilStep("HUD loaded", () => Player.IsLoaded && (counter = Player.HUDOverlay.ChildrenOfType<BmsComboCounter>().SingleOrDefault()!) != null && counter.IsLoaded);
        seek(0);
        AddStep("set existing combo", () => { counter.Current.Value = 100; counter.UpdateSubTree(); });
        AddStep("break and recover before the next HUD frame", () =>
        {
            counter.Current.Value = 0;
            counter.Current.Value = 1;
            counter.UpdateSubTree();
            var text = feedbackDrawable(counter, "displayedCountText");
            Assert.That(counter.DisplayedCount, Is.EqualTo(1));
            Assert.That(text.Colour.TopLeft.Linear.G, Is.LessThan(0.1), "combo-break red flash must survive batching");
            Assert.That(text.Scale.Y, Is.GreaterThan(1.3), "the final +1 still uses the increment pulse");
        });
    }

    [Test]
    public void ComboJumpDoesNotBecomeAnIncrementPulse()
    {
        BmsComboCounter counter = null!;
        AddStep("load player", () => LoadPlayer());
        AddUntilStep("HUD loaded", () => Player.IsLoaded && (counter = Player.HUDOverlay.ChildrenOfType<BmsComboCounter>().SingleOrDefault()!) != null && counter.IsLoaded);
        seek(0);
        AddStep("jump directly to a restored combo", () =>
        {
            counter.Current.Value = 100;
            counter.UpdateSubTree();
            Assert.That(counter.DisplayedCount, Is.EqualTo(100));
            Assert.That(feedbackDrawable(counter, "displayedCountText").Scale.Y, Is.EqualTo(1).Within(0.001));
        });
    }

    [Test]
    public void KeyFeedbackKeepsReleaseDelayFadeAndRetrigger()
    {
        LegacyBmsKeyArea key = null!;
        LegacyBmsColumnLight light = null!;
        var press = new KeyBindingPressEvent<BmsAction>(new InputState(), BmsAction.Key1);
        var release = new KeyBindingReleaseEvent<BmsAction>(new InputState(), BmsAction.Key1);
        AddStep("load player", () => LoadPlayer());
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        seek(1000);
        AddStep("press and release in one frame", () =>
        {
            key = feedbackForColumn<LegacyBmsKeyArea>();
            light = feedbackForColumn<LegacyBmsColumnLight>();
            key.OnPressed(press);
            key.OnReleased(release);
            light.OnPressed(press);
            light.OnReleased(release);
        });
        seek(1040);
        AddAssert("released key stays down for 80 ms", () => feedbackDrawable(key, "downSprite").Alpha == 1);
        AddAssert("column light fades over 250 ms", () => Math.Abs(feedbackDrawable(light, "light").Alpha - 0.84) < 0.001);
        seek(1100);
        AddAssert("key release delay ends", () => feedbackDrawable(key, "downSprite").Alpha == 0);
        AddStep("retrigger during fade", () => { key.OnPressed(press); light.OnPressed(press); });
        seek(1200);
        AddAssert("new press restores feedback", () => feedbackDrawable(key, "downSprite").Alpha == 1 && feedbackDrawable(light, "light").Alpha == 1);
        AddStep("release again", () => { key.OnReleased(release); light.OnReleased(release); });
        seek(1500);
        AddAssert("released feedback disappears", () => feedbackDrawable(key, "downSprite").Alpha == 0 && feedbackDrawable(light, "light").Alpha == 0);
    }

    private T feedbackForColumn<T>() where T : Drawable => Player.ChildrenOfType<T>().First(k => typeof(T)
        .GetField("lookup", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(k) is BmsSkinComponentLookup { ColumnIndex: 1 });

    private static Drawable feedbackDrawable<T>(T feedback, string field) =>
        (Drawable)typeof(T).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(feedback)!;

    [Test]
    public void DenseKeyFeedbackDoesNotAllocatePerInputAnimations()
    {
        AddStep("load player", () => LoadPlayer());
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        seek(2999);
        AddStep("measure key feedback burst", () =>
        {
            var key = feedbackForColumn<LegacyBmsKeyArea>();
            var light = feedbackForColumn<LegacyBmsColumnLight>();
            var press = new KeyBindingPressEvent<BmsAction>(new InputState(), BmsAction.Key1);
            var release = new KeyBindingReleaseEvent<BmsAction>(new InputState(), BmsAction.Key1);
            key.OnPressed(press);
            key.OnReleased(release);
            light.OnPressed(press);
            light.OnReleased(release);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
            {
                key.OnPressed(press);
                key.OnReleased(release);
                light.OnPressed(press);
                light.OnReleased(release);
            }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(32_000));
        });
    }

    [Test]
    public void HitErrorBurstKeepsLatestMarkersAndEveryTimingObservation()
    {
        BmsHitErrorMeter meter = null!;
        var onJudgement = typeof(BmsHitErrorMeter).GetMethod("OnNewJudgement", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<BmsHitErrorMeter, JudgementResult>>();
        AddStep("load player", () => LoadPlayer());
        AddUntilStep("meter loaded", () => Player.IsLoaded && (meter = Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().SingleOrDefault()!) != null && meter.IsLoaded);
        seek(0);
        AddStep("apply timing burst", () =>
        {
            meter.Clear();
            var note = Playfield.Beatmap.HitObjects[0];
            for (var i = 0; i < 100; i++)
            {
                var result = new JudgementResult(note, note.CreateJudgement()) { Type = HitResult.Perfect };
                typeof(JudgementResult).GetProperty(nameof(JudgementResult.TimeOffset))!.SetValue(result, (double)i - 50);
                onJudgement(meter, result);
            }
        });
        AddUntilStep("last fifty markers rendered", () => meter.ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Count(d => d.IsAlive) == 50);
        AddStep("markers and average retain timing semantics", () =>
        {
            var domain = BmsHitErrorMeter.CreateDomain(BmsLayoutVariant.Bme7K, Playfield.Beatmap.HitObjects[0].EffectiveJudgementRate);
            var expected = Enumerable.Range(50, 50).Select(i => domain.RelativePosition(i - 50)).Order().ToArray();
            Assert.That(meter.ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Where(d => d.IsAlive).Select(d => d.Y).Order(), Is.EqualTo(expected).Within(0.00001));
            var average = Enumerable.Range(0, 100).Aggregate(0d, (value, i) => value * 0.9 + (i - 50) * 0.1);
            Assert.That(typeof(BmsHitErrorMeter).GetField("floatingAverage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(meter), Is.EqualTo(average).Within(0.000001));
            var note = Playfield.Beatmap.HitObjects[0];
            onJudgement(meter, new JudgementResult(note, note.CreateJudgement()) { Type = HitResult.Perfect });
            meter.Clear();
        });
        AddUntilStep("clear also discards queued marker", () => meter.ChildrenOfType<BmsHitErrorMeter.JudgementLine>().All(d => !d.IsAlive));
    }

    [Test]
    public void NotesDoNotActivateUnusedFrameworkSamplePlayers()
    {
        AddStep("load player", () => LoadPlayer());
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        seek(2999);
        AddAssert("notes are alive", () => Playfield.AllColumnAliveObjects().Count() >= note_count);
        AddAssert("shared PCM playback needs no per-note audio hierarchy", () =>
            Playfield.AllColumnAliveObjects().SelectMany(note => note.ChildrenOfType<PausableSkinnableSound>()).All(samples => !samples.IsAlive));
    }

    [Test]
    public void AdjacentGameplayFramesDoNotRepeatTheSameInitialColumnTraversal()
    {
        AddStep("load player", () => LoadPlayer());
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        seek(2999);
        AddStep("measure successive frames at the same simulation time", () =>
        {
            var column = (BmsColumnHitObjectContainer)Playfield.Stage.Columns[1].HitObjectContainer;
            using var counter = new ScopedMethodProbe(typeof(BmsColumnHitObjectContainer).GetMethod("UpdateAfterChildrenLife", BindingFlags.Instance | BindingFlags.NonPublic),
                typeof(TestSceneBmsDenseReplay).GetMethod(nameof(countUpdate), BindingFlags.Static | BindingFlags.NonPublic));
            columnUpdates = 0;
            for (var i = 0; i < 10; i++)
            {
                column.BeginGameplayFrame();
                column.UpdateSubTree();
                column.UpdateSubTree();
                column.EndGameplayFrame();
            }
            Assert.That(columnUpdates, Is.LessThanOrEqualTo(11));
        });
    }

    [Test]
    public void UnchangedTapDeadlinesAreNotScannedEveryFrame()
    {
        AddStep("load player", () => LoadPlayer());
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        seek(2999);
        AddStep("measure repeated frames with unchanged pending notes", () =>
        {
            var column = (BmsColumnHitObjectContainer)Playfield.Stage.Columns[1].HitObjectContainer;
            var note = column.AliveEntries.Values.First();
            using var counter = new ScopedMethodProbe(note.GetType().GetProperty("NextPassiveJudgementTime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetMethod,
                typeof(TestSceneBmsDenseReplay).GetMethod(nameof(countPassiveDeadline), BindingFlags.Static | BindingFlags.NonPublic));
            passiveDeadlineReads = 0;
            for (var i = 0; i < 20; i++)
                column.UpdateSubTree();
            Assert.That(passiveDeadlineReads, Is.LessThan(64), "deadlines change on activation/revert or expiry, not on every visual frame");
        });
    }

    [Test]
    public void DenseReplayPreloadsEveryOverlappingHitPulse()
    {
        AddStep("load player", () => LoadPlayer());
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        seek(2999);
        AddStep("burst does not construct skin trees on the update thread", () =>
        {
            var column = (BmsColumn)Playfield.Stage.Columns[1];
            var pools = column.ChildrenOfType<DrawablePool<BmsHitExplosion>>().ToArray();
            var count = pools.Sum(p => p.CurrentPoolSize);
            for (var i = 0; i < note_count / 8; i++)
                column.TriggerHitExplosion(false);
            Assert.That(pools.Sum(p => p.CurrentPoolSize), Is.EqualTo(count));
        });
        seek(3050);
        AddAssert("all independent pulses remain visible", () => ((BmsColumn)Playfield.Stage.Columns[1]).HitExplosionArea.AliveChildren.Count(d => d.Alpha > 0) >= note_count / 8);
    }

    [Test]
    public void SkinReloadRefreshesIdlePreloadedPulsesBeforeUse()
    {
        BmsCachedSkinnableDrawable skin = null!;
        Drawable previous = null!;
        AddStep("load player", () => LoadPlayer());
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        seek(0);
        AddStep("capture idle pulse skin and reload source", () =>
        {
            var pool = ((BmsColumn)Playfield.Stage.Columns[1]).ChildrenOfType<DrawablePool<BmsHitExplosion>>().First();
            var pulse = pool.Get();
            skin = pulse.ChildrenOfType<BmsCachedSkinnableDrawable>().Single();
            previous = skin.Drawable;
            pulse.Return();
            typeof(SkinProvidingContainer).GetMethod("TriggerSourceChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(((BmsTestSkins.SkinnedTestPlayer)Player).SkinSource, null);
        });
        AddUntilStep("idle skin is refreshed before the next hit", () => !ReferenceEquals(previous, skin.Drawable));
        AddAssert("replacement skin tree is already loaded", () => skin.Drawable.LoadState >= LoadState.Ready);
    }

    [Test]
    public void SubMillisecondReplayKeepsEveryJudgementWithoutRepeatingVisualTraversal()
    {
        AddStep("load dense replay", () => LoadPlayer());
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        for (var run = 0; run < 2; run++)
        {
            seek(2999);
            AddStep("count column visual traversals", () =>
            {
                Player.Results.Clear();
                probe = new ScopedMethodProbe(typeof(BmsColumnHitObjectContainer).GetMethod("UpdateAfterChildrenLife", BindingFlags.Instance | BindingFlags.NonPublic),
                    typeof(TestSceneBmsDenseReplay).GetMethod(nameof(countUpdate), BindingFlags.Static | BindingFlags.NonPublic));
                columnUpdates = 0;
                maskingProbe = new ScopedMethodProbe(typeof(CompositeDrawable).GetMethod(nameof(CompositeDrawable.UpdateSubTreeMasking)),
                    typeof(TestSceneBmsDenseReplay).GetMethod(nameof(countMasking), BindingFlags.Static | BindingFlags.NonPublic));
                noteMaskingUpdates = 0;
                explosionMaskingUpdates = 0;
            });
            seek(3001);
            AddStep("stop counting", () => { probe?.Dispose(); probe = null; maskingProbe?.Dispose(); maskingProbe = null; });
            AddStep("all burst notes judged perfectly at their exact replay times", () =>
            {
                Assert.That(Player.Results.Count, Is.EqualTo(note_count));
                Assert.That(Player.Results.All(r => r.Type == HitResult.Perfect), Is.True);
                Assert.That(Player.Results.Max(r => Math.Abs(r.TimeOffset)), Is.LessThan(0.000001));
            });
            AddAssert("visual traversal is bounded per game frame", () => columnUpdates < note_count);
            AddAssert("masking does not traverse notes for every replay timestamp", () => noteMaskingUpdates < note_count * 16);
            AddAssert("masking does not traverse overlapping pulses for every replay timestamp", () => explosionMaskingUpdates < note_count * 16);
        }
    }

    private void seek(double time)
    {
        AddStep($"seek {time}", () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(time); });
        AddUntilStep("simulation reached target", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < 0.000001);
    }

    private static void countUpdate() => columnUpdates++;

    private static void countPassiveDeadline() => passiveDeadlineReads++;

    // ReSharper disable once InconsistentNaming
    private static void countMasking(CompositeDrawable __instance)
    {
        if (__instance is DrawableBmsHitObject)
            noteMaskingUpdates++;
        if (__instance is BmsHitExplosion)
            explosionMaskingUpdates++;
    }

    protected override void Dispose(bool isDisposing)
    {
        probe?.Dispose();
        maskingProbe?.Dispose();
        base.Dispose(isDisposing);
    }
}
