#nullable enable
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsSongProgress : BmsPlayerTestScene
{
    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(null);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = BmsTestBeatmaps.CreateBeatmap();
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [Test]
    public void TestAppearanceAndProgress()
    {
        BmsSongProgress progress() => Player.HUDOverlay.ChildrenOfType<BmsSongProgress>().Single();
        double firstHitTime() => Player.GameplayState.Beatmap.HitObjects.Min(hitObject => hitObject.StartTime);
        double lastHitTime() => Player.GameplayState.Beatmap.HitObjects.Max(hitObject => hitObject.GetEndTime());

        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddUntilStep("song progress loaded", () => Player.HUDOverlay.ChildrenOfType<BmsSongProgress>().SingleOrDefault()?.IsLoaded == true);
        AddStep("stop clock", () => Player.GameplayClockContainer.Stop());
        AddAssert("indicator remains left of playfield", () =>
            progress().ScreenSpaceDrawQuad.TopRight.X < Playfield.SkinnableComponentScreenSpaceDrawQuad.TopLeft.X);

        AddStep("seek to top", () => Player.GameplayClockContainer.Seek(firstHitTime()));
        AddStep("seek to middle", () => Player.GameplayClockContainer.Seek((firstHitTime() + lastHitTime()) / 2));
        AddStep("use cyan indicator", () => progress().IndicatorColour.Value = new Colour4(40, 220, 255, 255));
        AddStep("seek to bottom", () => Player.GameplayClockContainer.Seek(lastHitTime()));
        AddStep("restore default red", () => progress().IndicatorColour.SetDefault());
    }

    [Test]
    public void TestEmptyPoorLineUsesNextNoteOffset()
    {
        const double input_time = BmsTestBeatmaps.FIRST_NOTE_TIME - 400;

        Box emptyPoorWindow() => Player.HUDOverlay.ChildrenOfType<Box>().Single(child => child.Name == "empty poor window");

        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("hide EPOOR", () => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single().ShowEmptyPoor.Value = false);
        AddUntilStep("EPOOR window hidden", () => emptyPoorWindow().Alpha == 0);
        AddStep("register hidden EPOOR", () =>
            ((BmsScoreProcessor)Player.GameplayState.ScoreProcessor).RegisterEmptyPoor(input_time, BmsTestBeatmaps.FIRST_NOTE_TIME, 0));
        AddWaitStep("wait for scheduled update", 1);
        AddAssert("hidden EPOOR adds no line", () => !Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Any());
        AddStep("show EPOOR", () => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single().ShowEmptyPoor.Value = true);
        AddUntilStep("EPOOR window shown", () => emptyPoorWindow().Alpha == 1);
        AddStep("register EPOOR timing", () =>
            ((BmsScoreProcessor)Player.GameplayState.ScoreProcessor).RegisterEmptyPoor(input_time, BmsTestBeatmaps.FIRST_NOTE_TIME, 0));
        AddUntilStep("error line appears", () => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Count() == 1);
        AddAssert("EPOOR line is on fast side", () =>
            Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Single().ScreenSpaceDrawQuad.Centre.X
            < Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single().ScreenSpaceDrawQuad.Centre.X);
    }

    [Test]
    public void TestHitErrorMeterExpandsAndReusesPool()
    {
        BmsHitErrorMeter meter() => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single();
        BmsHitErrorMeter.JudgementLinePool pool() => meter().ChildrenOfType<BmsHitErrorMeter.JudgementLinePool>().Single();

        void registerBarrage()
        {
            var processor = (BmsScoreProcessor)Player.GameplayState.ScoreProcessor;

            for (var i = 0; i < 100; i++)
                processor.RegisterEmptyPoor(BmsTestBeatmaps.FIRST_NOTE_TIME - i, BmsTestBeatmaps.FIRST_NOTE_TIME, 0);
        }

        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("stop gameplay clock", () => Player.GameplayClockContainer.Stop());
        AddAssert("pool starts with fifty reusable lines", () => pool().CurrentPoolSize == 50);
        AddAssert("pooled lines stay out of the presented tree", () =>
            meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().All(line => !line.IsPresent));
        AddStep("register judgement barrage", registerBarrage);
        AddUntilStep("all hundred lines are displayed", () => meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Count() == 100);
        AddAssert("pool grows past its initial size", () => pool().CurrentPoolSize == 100 && pool().CountInUse == 100);
        AddStep("thicken existing lines", () => meter().JudgementLineThickness.Value = 5.5f);
        AddAssert("existing and expanded lines update thickness", () =>
            meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().All(line => line.Width == 5.5f));
        AddStep("clear judgement lines", () => meter().Clear());
        AddUntilStep("lines return to pool", () => pool().CountInUse == 0 && pool().CountAvailable == 100);
        AddStep("use short fade", () => meter().JudgementFadeDuration.Value = 0.5f);
        AddStep("change thickness while lines are pooled", () => meter().JudgementLineThickness.Value = 1.5f);
        AddStep("register another barrage", registerBarrage);
        AddUntilStep("all hundred lines are reused", () => pool().CountInUse == 100);
        AddAssert("reusing lines does not grow pool", () => pool().CurrentPoolSize == 100);
        AddAssert("reused lines use current thickness", () =>
            meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().All(line => line.Width == 1.5f));
        AddUntilStep("faded lines return to pool", () => pool().CountInUse == 0 && pool().CountAvailable == 100);
    }

    [Test]
    public void TestHitErrorMeterDrawSnapshotsTrackLayoutSettingsAndClear()
    {
        BmsHitErrorMeter meter() => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single();
        Container judgements() => meter().ChildrenOfType<Container>().Single(child => child.Name == "judgements");
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        var readSource = typeof(DrawNode).GetProperty("Source", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetMethod!.CreateDelegate<Func<DrawNode, IDrawable>>();
        var readQuad = typeof(SpriteDrawNode).GetProperty("ScreenSpaceDrawQuad", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetMethod!.CreateDelegate<Func<SpriteDrawNode, osu.Framework.Graphics.Primitives.Quad>>();
        var readColour = typeof(DrawNode).GetProperty("DrawColourInfo", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetMethod!.CreateDelegate<Func<DrawNode, DrawColourInfo>>();
        var canFlatten = typeof(CompositeDrawable).GetProperty("CanBeFlattened", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetMethod!.CreateDelegate<Func<CompositeDrawable, bool>>();
        var localVertexBatch = typeof(CompositeDrawable).GetProperty(nameof(CompositeDrawable.ForceLocalVertexBatch))!;
        var masking = typeof(CompositeDrawable).GetProperty("Masking", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        // A non-virtual call provides the framework's masking result without the component cache.
        var baseMaskingMethod = new DynamicMethod("UpdateBaseMasking", typeof(bool), [typeof(CompositeDrawable)], typeof(TestSceneBmsSongProgress), true);
        var baseMaskingIl = baseMaskingMethod.GetILGenerator();
        baseMaskingIl.Emit(OpCodes.Ldarg_0);
        baseMaskingIl.Emit(OpCodes.Call, typeof(CompositeDrawable).GetMethod(nameof(CompositeDrawable.UpdateSubTreeMasking))!);
        baseMaskingIl.Emit(OpCodes.Ret);
        var updateBaseMasking = baseMaskingMethod.CreateDelegate<Func<CompositeDrawable, bool>>();
        var clock = new ManualClock();
        var frame = 0UL;

        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("freeze meter clock and create lines", () =>
        {
            Player.GameplayClockContainer.Stop();
            meter().Clock = new FramedClock(clock);
            meter().Clear();
            var processor = (BmsScoreProcessor)Player.GameplayState.ScoreProcessor;
            for (var i = 0; i < 20; i++)
                processor.RegisterEmptyPoor(500 + i * 5, 1000, 0);
        });
        AddUntilStep("twenty lines prepared", () =>
            meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Count(line => line.Alpha == 0.4f) == 20);
        AddAssert("multiple lines retain their buffered draw node", () => !canFlatten(judgements()));
        AddStep("check every buffer and refresh boundary", () =>
        {
            assertSnapshots();
            clock.CurrentTime = 4;
            meter().UpdateSubTree();
            assertSnapshots();
            clock.CurrentTime = 8;
            meter().UpdateSubTree();
            assertSnapshots();
            meter().JudgementLineThickness.Value = 6;
            assertSnapshots();
            var changedLine = (BmsHitErrorMeter.JudgementLine)judgements().Children.First();
            changedLine.Alpha = 0.17f;
            changedLine.Colour = Colour4.Cyan;
            assertSnapshots();
            meter().ShowEmptyPoor.Value = false;
            assertSnapshots();
            meter().Position += new Vector2(13, 17);
            meter().Scale *= 1.25f;
            meter().Rotation = 11;
            meter().Colour = Colour4.Cyan;
            assertSnapshots();
            masking.SetValue(meter(), true);
            assertSnapshots();
            masking.SetValue(meter(), false);
            assertSnapshots();
            judgements().Masking = true;
            Assert.That(canFlatten(judgements()), Is.False);
            judgements().Width = 0.4f;
            assertSnapshots();
            clock.CurrentTime = 1;
            meter().UpdateSubTree();
            assertSnapshots();
            foreach (var line in judgements().Children.Skip(1))
                line.Expire();
            clock.CurrentTime = 9;
            meter().UpdateSubTree();
            Assert.That(judgements().Children.Count, Is.EqualTo(1));
            meter().Rotation = 0;
            meter().Scale = Vector2.One;
            judgements().Masking = false;
            localVertexBatch.SetValue(judgements(), true);
            Assert.That(canFlatten(judgements()), Is.False);
            assertSnapshots();
            localVertexBatch.SetValue(judgements(), false);
            Assert.That(canFlatten(judgements()), Is.True);
            judgements().RelativeSizeAxes = Axes.None;
            judgements().Size = new Vector2(1, 20);
            var bounds = judgements().ComputeMaskingBounds();
            judgements().Position = meter().ToLocalSpace(new Vector2(bounds.Left - 2, bounds.Centre.Y));
            ((BmsHitErrorMeter.JudgementLine)judgements().Children.Single()).X = 1;
            assertSnapshots();
            Assert.That(snapshot(generate(meter(), ++frame, 0, false)), Has.Length.EqualTo(1),
                "A flattened single line can extend into the viewport beyond its container bounds.");
            judgements().Position = meter().ToLocalSpace(new Vector2(bounds.Left + 2, bounds.Centre.Y));
            assertSnapshots();
            Assert.That(snapshot(generate(meter(), ++frame, 0, false)), Has.Length.EqualTo(1),
                "Moving the container back into the viewport restores the line.");
            meter().Clear();
            clock.CurrentTime = 10;
            meter().UpdateSubTree();
            assertSnapshots();
            Assert.That(judgements().Children, Is.Empty);
            judgements().CreateProxy();
            Assert.That(canFlatten(judgements()), Is.False);
        });

        void assertSnapshots()
        {
            var container = judgements();
            meter().UpdateSubTreeMasking();
            var cachedMasking = snapshot(generate(meter(), ++frame, 0, false));
            updateBaseMasking(container);
            Assert.That(snapshot(generate(meter(), ++frame, 0, true)), Is.EqualTo(cachedMasking),
                "Cached masking differs from the framework's child traversal.");
            for (var slot = 0; slot < 3; slot++)
            {
                var node = generate(container, ++frame, slot, false);
                var normal = snapshot(node);
                // Fresh nodes keep all three buffered snapshots comparable to current Box state.
                var fresh = snapshot(generate(container, ++frame, slot, true));
                Assert.That(normal, Is.EqualTo(fresh), $"Buffer {slot} retained stale line state.");
                Assert.That(snapshot(generate(container, ++frame, slot, false)), Is.EqualTo(fresh));
            }
        }

        object[] snapshot(DrawNode? node)
        {
            var result = new System.Collections.Generic.List<object>();
            if (node != null)
                visit(node);
            return result.ToArray();

            void visit(DrawNode child)
            {
                if (readSource(child) is BmsHitErrorMeter.JudgementLine)
                    result.Add((readQuad((SpriteDrawNode)child), readColour(child)));
                if (child is ICompositeDrawNode { Children: { } children })
                    foreach (var nested in children)
                        visit(nested);
            }
        }
    }

    [Test]
    public void TestHitErrorMeterReuseAfterRewind()
    {
        BmsHitErrorMeter meter() => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single();
        BmsHitErrorMeter.JudgementLinePool pool() => meter().ChildrenOfType<BmsHitErrorMeter.JudgementLinePool>().Single();
        var clock = new ManualClock { CurrentTime = 1000 };
        BmsHitErrorMeter.JudgementLine original = null!;
        var onJudgement = typeof(BmsHitErrorMeter).GetMethod("OnNewJudgement", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<BmsHitErrorMeter, JudgementResult>>();

        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("use controlled meter clock", () =>
        {
            Player.GameplayClockContainer.Stop();
            meter().Clock = new FramedClock(clock);
            meter().JudgementFadeDuration.Value = 1;
        });
        AddStep("add timing line", addLine);
        AddUntilStep("line begins fading", () => meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().SingleOrDefault()?.Alpha == 0.4f);
        AddStep("remember line and advance halfway", () =>
        {
            original = meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Single();
            clock.CurrentTime = 1500;
        });
        AddUntilStep("halfway alpha is preserved", () => Math.Abs(original.Alpha - 0.2f) < 0.00001);
        AddStep("rewind controlled meter clock", () => clock.CurrentTime = 100);
        AddStep("seek backwards and clear", () => Player.GameplayClockContainer.Seek(0));
        AddUntilStep("seek returns line to pool", () => pool().CountInUse == 0);
        AddStep("reuse after rewind", addLine);
        AddUntilStep("same line has full initial alpha", () => pool().CountInUse == 1
            && meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Single() == original && original.Alpha == 0.4f);
        AddStep("advance past reused fade", () => clock.CurrentTime = 1101);
        AddUntilStep("reused line expires on new timeline", () => pool().CountInUse == 0 && pool().CountAvailable == 50);

        void addLine()
        {
            var note = Playfield.Beatmap.HitObjects[0];
            onJudgement(meter(), new JudgementResult(note, note.CreateJudgement()) { Type = HitResult.Perfect });
        }
    }

    [Test]
    public void TestHitErrorMeterFadeMatchesFramework([Values(0.1f, 10f, 20f)] float seconds)
    {
        BmsHitErrorMeter meter() => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single();
        var clock = new ManualClock { CurrentTime = 1000 };
        Box reference = null!;
        var onJudgement = typeof(BmsHitErrorMeter).GetMethod("OnNewJudgement", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<BmsHitErrorMeter, JudgementResult>>();

        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("load reference fade", () => Add(reference = new Box { Clock = new FramedClock(clock) }));
        AddUntilStep("reference loaded", () => reference.IsLoaded);
        AddStep("compare fades across pause and rewind", () =>
        {
            Player.GameplayClockContainer.Stop();
            meter().Clock = new FramedClock(clock);
            meter().Clear();
            meter().UpdateSubTree();
            meter().JudgementFadeDuration.Value = seconds;
            reference.UpdateSubTree();
            reference.FadeTo(0.4f).Then().FadeOut(seconds * 1000).Expire();

            var note = Playfield.Beatmap.HitObjects[0];
            onJudgement(meter(), new JudgementResult(note, note.CreateJudgement()) { Type = HitResult.Perfect });
            meter().UpdateSubTree();
            var line = meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Single();
            Assert.That(line.LifetimeEnd, Is.EqualTo(reference.LifetimeEnd));

            // A settings change applies to new lines; the ongoing fade retains its duration.
            meter().JudgementFadeDuration.Value = seconds == 20 ? 0.1f : 20;
            double[] offsets = [0, 8, seconds * 250, seconds * 500, seconds * 500, seconds * 1000 - 1, seconds * 500, 0, seconds * 1000];
            foreach (var offset in offsets)
            {
                clock.CurrentTime = 1000 + offset;
                meter().UpdateSubTree();
                reference.UpdateSubTree();
                Assert.That(line.Alpha, Is.EqualTo(reference.Alpha), $"Alpha at {offset} ms");
                Assert.That(line.IsPresent, Is.EqualTo(reference.IsPresent), $"Presence at {offset} ms");
            }

            clock.CurrentTime = reference.LifetimeEnd + 8;
            meter().UpdateSubTree();
            Assert.That(meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>(), Is.Empty);
        });
    }

    [Test]
    public void TestHitErrorMeterDisposesActiveAndRecycledLines()
    {
        BmsHitErrorMeter meter() => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single();
        BmsHitErrorMeter.JudgementLinePool pool() => meter().ChildrenOfType<BmsHitErrorMeter.JudgementLinePool>().Single();
        BmsHitErrorMeter.JudgementLine[] lines = [];
        var isDisposed = typeof(Drawable).GetProperty("IsDisposed", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetMethod!.CreateDelegate<Func<Drawable, bool>>();

        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("stop gameplay clock and fill pool", () =>
        {
            Player.GameplayClockContainer.Stop();
            var processor = (BmsScoreProcessor)Player.GameplayState.ScoreProcessor;
            for (var i = 0; i < 100; i++)
                processor.RegisterEmptyPoor(BmsTestBeatmaps.FIRST_NOTE_TIME - i, BmsTestBeatmaps.FIRST_NOTE_TIME, 0);
        });
        AddUntilStep("hundred lines active", () => pool().CountInUse == 100);
        AddStep("remember lines and recycle half", () =>
        {
            lines = meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().ToArray();
            foreach (var line in lines.Take(50))
            {
                line.ClearTransforms();
                line.Expire();
            }
        });
        AddUntilStep("half the lines recycled", () => pool().CountInUse == 50 && pool().CountAvailable == 50);
        AddAssert("recycling keeps lines alive for reuse", () => lines.All(line => !isDisposed(line)));
        AddStep("remove meter", () => meter().Expire());
        AddUntilStep("all owned lines disposed", () => lines.All(line => isDisposed(line)));
    }

    [Test]
    public void TestHitErrorMeterLayout()
    {
        BmsHitErrorMeter meter() => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single();
        Box badWindow() => meter().ChildrenOfType<Box>().Single(child => child.Name == $"{HitResult.Ok} window");
        Container colourBar() => meter().ChildrenOfType<Container>().Single(child => child.Name == "judgement windows");
        Container judgements() => meter().ChildrenOfType<Container>().Single(child => child.Name == "judgements");

        float originalWindowWidth = 0;
        float originalJudgementHeight = 0;

        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddUntilStep("hit error meter loaded", () => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().SingleOrDefault()?.IsLoaded == true);
        AddAssert("legacy dimensions follow the BMS domain", () =>
        {
            var domain = BmsHitErrorMeter.CreateDomain(Playfield.Beatmap.LayoutVariant, Playfield.Beatmap.HitObjects[0].EffectiveJudgementRate);
            return Math.Abs(meter().Width - (domain.SlowOffset - domain.FastOffset) / 2 * LegacySkin.STABLE_MAGIC_SCALE_FACTOR) < 0.001
                   && Math.Abs(meter().Height - 12 * LegacySkin.STABLE_MAGIC_SCALE_FACTOR) < 0.001
                   && meter().Scale == Vector2.One;
        });
        AddAssert("legacy background is translucent black", () =>
        {
            var background = meter().ChildrenOfType<Box>().Single(child => child.Name == "background");
            return background.Colour == Colour4.Black && background.Alpha == 0.6f;
        });
        AddStep("make background transparent", () => meter().BackgroundOpacity.Value = 0);
        AddAssert("only background becomes transparent", () =>
            meter().ChildrenOfType<Box>().Single(child => child.Name == "background").Alpha == 0
            && badWindow().IsPresent && meter().ChildrenOfType<Triangle>().Single().IsPresent);
        AddStep("make background opaque", () => meter().BackgroundOpacity.Value = 1);
        AddAssert("background is fully opaque", () => meter().ChildrenOfType<Box>().Single(child => child.Name == "background").Alpha == 1);
        AddStep("restore background opacity", () => meter().BackgroundOpacity.SetDefault());
        AddAssert("meter has one window bar", () => meter().ChildrenOfType<Container>().Count(child => child.Name == "judgement windows") == 1);
        AddStep("thicken colour bar", () => meter().ColourBarHeight.Value = 5.5f);
        AddAssert("colour bar uses configured height", () => Math.Abs(colourBar().DrawHeight - 5.5f * LegacySkin.STABLE_MAGIC_SCALE_FACTOR) < 0.001);
        AddStep("hide colour bar", () => meter().ColourBarHeight.Value = 0);
        AddAssert("zero height hides colour bar and preserves timing overlay", () =>
            colourBar().DrawHeight == 0 && !colourBar().IsPresent
            && meter().ChildrenOfType<Box>().Single(child => child.Name == "centre marker").IsPresent
            && judgements().IsPresent && meter().ChildrenOfType<Triangle>().Single().IsPresent);
        AddStep("restore colour bar height", () => meter().ColourBarHeight.SetDefault());
        AddAssert("colour bar becomes visible at default height", () =>
            colourBar().IsPresent && Math.Abs(colourBar().DrawHeight - 3 * LegacySkin.STABLE_MAGIC_SCALE_FACTOR) < 0.001);
        AddAssert("window bars use BMS judgement colours", () =>
        {
            HitResult[] results = [HitResult.Perfect, HitResult.Great, HitResult.Good, HitResult.Ok];
            return results.All(result => meter().ChildrenOfType<Box>().Single(child => child.Name == $"{result} window").Colour == BmsHitResultColours.ForHitResult(result))
                   && meter().ChildrenOfType<Box>().Single(child => child.Name == "empty poor window").Colour == BmsHitResultColours.ForHitResult(HitResult.Miss);
        });
        AddAssert("centre marker is white", () =>
        {
            var marker = meter().ChildrenOfType<Box>().Single(child => child.Name == "centre marker");
            return marker.Colour == Colour4.White
                   && Math.Abs(marker.ScreenSpaceDrawQuad.Centre.X - meter().ScreenSpaceDrawQuad.Centre.X) < 0.001
                   && marker.Width == 1.5f * LegacySkin.STABLE_MAGIC_SCALE_FACTOR;
        });
        AddAssert("legacy triangle starts at zero", () =>
        {
            var arrow = meter().ChildrenOfType<Triangle>().Single();
            return arrow.X == 0.5f && arrow.Scale.Y == -1;
        });
        AddAssert("BAD window retains asymmetric BMS edges", () =>
        {
            var domain = BmsHitErrorMeter.CreateDomain(Playfield.Beatmap.LayoutVariant, Playfield.Beatmap.HitObjects[0].EffectiveJudgementRate);
            var windows = BmsJudgementProfileProvider.GetTable(Playfield.Beatmap.LayoutVariant, 1, Playfield.Beatmap.HitObjects[0].EffectiveJudgementRate, tail: false);
            return Math.Abs(badWindow().X - domain.RelativePosition(-windows.FastWindowFor(HitResult.Ok))) < 0.00001
                   && Math.Abs(badWindow().X + badWindow().Width - domain.RelativePosition(windows.SlowWindowFor(HitResult.Ok))) < 0.00001;
        });
        AddAssert("EPOOR window is an added fast segment", () =>
            meter().ChildrenOfType<Box>().Single(child => child.Name == "empty poor window").ScreenSpaceDrawQuad.Centre.X
            < meter().ScreenSpaceDrawQuad.Centre.X);
        AddAssert("meter is horizontal", () => meter().ScreenSpaceDrawQuad.AABBFloat.Width > meter().ScreenSpaceDrawQuad.AABBFloat.Height);
        AddStep("capture original dimensions", () =>
        {
            originalWindowWidth = badWindow().ScreenSpaceDrawQuad.AABBFloat.Width;
            originalJudgementHeight = judgements().ScreenSpaceDrawQuad.AABBFloat.Height;
        });
        AddStep("stretch horizontally", () => meter().Width *= 1.5f);
        AddAssert("horizontal stretch lengthens timing axis", () =>
            badWindow().ScreenSpaceDrawQuad.AABBFloat.Width > originalWindowWidth);
        AddStep("stretch vertically", () => meter().Height = 52);
        AddAssert("vertical stretch widens judgement lines", () => judgements().ScreenSpaceDrawQuad.AABBFloat.Height > originalJudgementHeight);
    }

    [Test]
    public void TestColourBarMatchesJudgements([Values(false, true)] bool noBad, [Values(0, 1, 2)] int constraint)
    {
        BmsHitErrorMeter meter() => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single();

        AddStep("load window mods", () =>
        {
            Mod[] mods = constraint switch
            {
                1 => [new BmsModNoGood()],
                2 => [new BmsModNoGreat()],
                _ => [],
            };
            LoadPlayer(noBad ? [..mods, new BmsModNoBad()] : mods);
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddUntilStep("meter loaded", () => meter().IsLoaded);
        AddStep("check visible colours against playable windows", () =>
        {
            var windows = BmsJudgementProfileProvider.GetTable(Playfield.Beatmap.LayoutVariant, 1,
                Playfield.Beatmap.HitObjects[0].EffectiveJudgementRate, tail: false);
            var bars = meter().ChildrenOfType<Container>().Single(child => child.Name == "judgement windows").Children.OfType<Box>().ToArray();

            // Quarter-millisecond samples avoid shared inclusive boundaries while covering both sides
            // and the unused space beyond E-POOR's finite slow boundary.
            for (var offset = -499.75; offset < 500; offset++)
            {
                var result = windows.ResultForOffset(offset);
                if (result == HitResult.None && windows.IsEmptyPoorOffset(offset))
                    result = HitResult.Miss;

                var position = (float)((offset + 500) / 1000);
                var visible = bars.LastOrDefault(bar => bar.Width > 0 && position > bar.X && position < bar.X + bar.Width);
                if (result == HitResult.None)
                    Assert.That(visible, Is.Null, $"no judgement region at {offset} ms");
                else
                    Assert.That(visible != null && visible.Colour == BmsHitResultColours.ForHitResult(result), Is.True, $"{result} at {offset} ms");
            }

            Assert.That(meter().Width, Is.EqualTo(500 * LegacySkin.STABLE_MAGIC_SCALE_FACTOR).Within(0.001));
        });
        AddStep("hide E-POOR regions", () => meter().ShowEmptyPoor.Value = false);
        AddAssert("E-POOR background is hidden", () =>
            !meter().ChildrenOfType<Box>().Single(child => child.Name == "empty poor window").IsPresent);
    }

    [Test]
    public void TestJudgementFadeDuration()
    {
        BmsHitErrorMeter meter() => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single();

        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("default fade lasts ten seconds", () => meter().JudgementFadeDuration.Value == 10);
        AddStep("set short fade", () => meter().JudgementFadeDuration.Value = 0.5f);
        AddStep("register EPOOR timing", () =>
            ((BmsScoreProcessor)Player.GameplayState.ScoreProcessor).RegisterEmptyPoor(
                BmsTestBeatmaps.FIRST_NOTE_TIME - 400, BmsTestBeatmaps.FIRST_NOTE_TIME, 0));
        AddUntilStep("error line appears", () => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Count() == 1);
        AddAssert("legacy line fades without shrinking", () =>
        {
            var line = meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Single();
            return line.Width == 3 && line.Height == 1 && line.RelativeSizeAxes == Axes.Y
                   && line.Alpha is > 0 and <= 0.4f && line.ChildrenOfType<Drawable>().Count() == 1;
        });
        AddUntilStep("error line expires", () => !Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Any());
    }

    [Test]
    public void TestPoorLinesFollowVisibleWindowEnds([Values(false, true)] bool noBad, [Values(0, 1, 2)] int constraint)
    {
        BmsHitErrorMeter meter() => Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single();
        Box window(HitResult result) => meter().ChildrenOfType<Box>().Single(child =>
            child.Name == (result == HitResult.Miss ? "empty poor window" : $"{result} window"));

        AddStep("load player", () =>
        {
            Mod[] mods = constraint switch
            {
                1 => [new BmsModNoGood()],
                2 => [new BmsModNoGreat()],
                _ => [],
            };
            LoadPlayer(noBad ? [..mods, new BmsModNoBad()] : mods);
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddUntilStep("hit error meter loaded", () => meter().IsLoaded);
        AddStep("stop gameplay clock", () => Player.GameplayClockContainer.Stop());
        AddStep("register Fast and Slow POOR", registerPoors);
        AddUntilStep("both POOR lines appear", () => meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Count() == 2);
        AddStep("POOR lines match visible outer edges", () => assertEnds(showEmptyPoor: true));
        AddStep("hide E-POOR", () => meter().ShowEmptyPoor.Value = false);
        AddStep("existing POOR lines follow remaining hit windows", () => assertEnds(showEmptyPoor: false));
        AddStep("clear lines", () => meter().Clear());
        AddUntilStep("lines returned to pool", () => !meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Any());
        AddStep("register POOR with E-POOR hidden", registerPoors);
        AddUntilStep("both POOR lines appear again", () => meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().Count() == 2);
        AddStep("new POOR lines use remaining hit windows", () => assertEnds(showEmptyPoor: false));
        AddStep("show E-POOR", () => meter().ShowEmptyPoor.Value = true);
        AddStep("existing POOR lines return to outer edges", () => assertEnds(showEmptyPoor: true));

        void registerPoors()
        {
            var notes = Player.GameplayState.Beatmap.HitObjects.OfType<BmsNote>().Take(2).ToArray();
            for (var i = 0; i < notes.Length; i++)
            {
                var result = new JudgementResult(notes[i], notes[i].CreateJudgement()) { Type = HitResult.Meh };
                typeof(JudgementResult).GetProperty(nameof(JudgementResult.TimeOffset))!.SetValue(result, i == 0 ? -281d : 281d);
                Player.GameplayState.ScoreProcessor.ApplyResult(result);
            }
        }

        void assertEnds(bool showEmptyPoor)
        {
            var hitResult = !noBad ? HitResult.Ok : constraint switch
            {
                1 => HitResult.Great,
                2 => HitResult.Perfect,
                _ => HitResult.Good,
            };
            var left = window(showEmptyPoor ? HitResult.Miss : hitResult).ScreenSpaceDrawQuad.AABBFloat.Left;
            var right = window(showEmptyPoor && noBad ? HitResult.Miss : hitResult).ScreenSpaceDrawQuad.AABBFloat.Right;
            var lines = meter().ChildrenOfType<BmsHitErrorMeter.JudgementLine>().OrderBy(line => line.X).ToArray();

            Assert.That(lines, Has.Length.EqualTo(2));
            Assert.That(lines.All(line => line.Colour == BmsHitResultColours.ForHitResult(HitResult.Meh)), Is.True);
            Assert.That(lines[0].ScreenSpaceDrawQuad.Centre.X, Is.EqualTo(left).Within(0.5f));
            Assert.That(lines[1].ScreenSpaceDrawQuad.Centre.X, Is.EqualTo(right).Within(0.5f));
        }
    }
}
