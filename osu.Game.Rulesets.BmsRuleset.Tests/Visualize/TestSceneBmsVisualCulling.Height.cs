#nullable enable

using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsVisualCulling
{
    private static DrawableBmsHitObject? measuredHeightNote;
    private static int cullingHeightReads;

    private delegate void ApplyCullingLayout(DrawableBmsHitObject note, in BmsColumnVisualFrame frame, in BmsVisualLayout layout);

    [TestCase(false, BmsLongNoteMode.LongNote)]
    [TestCase(true, BmsLongNoteMode.LongNote)]
    [TestCase(true, BmsLongNoteMode.ChargeNote)]
    [TestCase(true, BmsLongNoteMode.HellChargeNote)]
    [NonParallelizable]
    [Category("JudgementInstrumentation")]
    public void TestCullingReadsSkinHeightOnlyOutsideMinimumRange(bool isLongNote, BmsLongNoteMode longNoteMode)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var apply = typeof(DrawableBmsHitObject).GetMethod("ApplyVisualLayout", flags)!.CreateDelegate<ApplyCullingLayout>();
        load(isLongNote, longNoteMode);
        AddStep("verify height work and culling boundaries", () =>
        {
            var heightGetter = drawable.GetType().GetProperty("VisualHeight", flags)!.GetMethod!;
            var skin = drawable.ChildrenOfType<BmsNoteVisual>().SingleOrDefault()?.Drawable
                       ?? drawable.ChildrenOfType<osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.BmsResolvedNoteSprite>().Single();
            var originalAxes = skin.RelativeSizeAxes;
            var originalSize = skin.Size;
            var originalScale = Playfield.Stage.NoteHeightScale;
            var frame = new BmsColumnVisualFrame(0, 1, 0, 700);
            drawable.UpdateVisualLayout(0, frame);
            using var probe = new ScopedMethodProbe(heightGetter,
                typeof(TestSceneBmsVisualCulling).GetMethod(nameof(countCullingHeightRead), BindingFlags.Static | BindingFlags.NonPublic)!);
            try
            {
                cullingHeightReads = 0;
                measuredHeightNote = drawable;
                for (var repeat = 0; repeat < 32; repeat++)
                {
                    apply(drawable, frame, new BmsVisualLayout(-repeat, -repeat, -repeat));
                    Assert.That(drawable.VisualsSuppressed, Is.False);
                    // A long body crossing the interval is retained regardless of either endpoint's distance.
                    apply(drawable, frame, new BmsVisualLayout(0, -10000, 10000));
                    Assert.That(drawable.VisualsSuppressed, Is.False);
                }
                measuredHeightNote = null;
                TestContext.WriteLine($"CULLING_HEIGHT_READS longNote={isLongNote} mode={longNoteMode} layouts=64 reads={cullingHeightReads}");
                Assert.That(cullingHeightReads, Is.Zero);

                skin.RelativeSizeAxes &= ~Axes.Y;
                float[] columnHeights = [0, 0.25f, 700];
                float[] skinHeights = [0, 10, 1400, 7000];
                float[] scales = [0.5f, 2];
                foreach (var columnHeight in columnHeights)
                foreach (var skinHeight in skinHeights)
                foreach (var scale in scales)
                {
                    skin.Height = skinHeight;
                    Playfield.Stage.SetNoteHeightScale(scale);
                    frame = frame with { Height = columnHeight };
                    var visualHeight = (float)heightGetter.Invoke(drawable, null)!;
                    var padding = Math.Max(columnHeight, visualHeight);
                    float[] boundaries = [-columnHeight - columnHeight, columnHeight, -columnHeight - padding, padding];
                    foreach (var boundary in boundaries)
                    {
                        float[] positions = [MathF.BitDecrement(boundary), boundary, MathF.BitIncrement(boundary)];
                        foreach (var y in positions)
                        {
                            var expected = y < -columnHeight - padding || y > padding;
                            var previousY = drawable.Y;
                            apply(drawable, frame, new BmsVisualLayout(y, y, y));
                            Assert.That(drawable.VisualsSuppressed, Is.EqualTo(expected),
                                $"column={columnHeight}, skin={skinHeight}, scale={scale}, y={y}");
                            Assert.That(drawable.Y, Is.EqualTo(expected ? previousY : y));
                        }
                    }
                }
            }
            finally
            {
                measuredHeightNote = null;
                skin.RelativeSizeAxes = originalAxes;
                skin.Size = originalSize;
                Playfield.Stage.SetNoteHeightScale(originalScale);
            }
        });
    }

    private static void countCullingHeightRead(DrawableBmsHitObject __instance)
    {
        if (ReferenceEquals(__instance, measuredHeightNote))
            cullingHeightReads++;
    }
}
