using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Framework.Utils;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public partial class TestSceneBmsPlayfield : OsuTestScene
{
    private BmsPlayfield playfield = null!;
    private IReadOnlyList<BmsHitObject> hitObjects = null!;
    private ManualClock clock = null!;

    [SetUpSteps]
    public void SetUpSteps()
    {
        AddStep("create 5K playfield", () =>
        {
            hitObjects = createHitObjects();

            Children =
            [
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Clock = new FramedClock(clock = new ManualClock()),
                    Padding = new MarginPadding(60),
                    Child = playfield = new BmsPlayfield(hitObjects, 6, BmsLayoutVariant.Bms5K, true)
                    {
                        TimeRange = 1200,
                    },
                },
            ];

            foreach (var hitObject in hitObjects)
                playfield.Add(hitObject);
        });
    }

    private static IReadOnlyList<BmsHitObject> createHitObjects() =>
    [
        new()
            { StartTime = 1000, Column = 0 },
        new()
            { StartTime = 1200, Column = 1 },
        new()
            { StartTime = 1400, Column = 2 },
        new()
            { StartTime = 1600, Column = 3 },
        new()
            { StartTime = 1800, Column = 4 },
        new()
            { StartTime = 2000, Column = 5 },
        new()
            { StartTime = 2400, Column = 3, IsLongNote = true, Duration = 600 },
    ];

    [Test]
    public void TestDisplay()
    {
        AddAssert("stage loaded", () => playfield.Stage.IsLoaded);
    }

    [Test]
    public void TestNonScratchKeysAreCentred()
    {
        AddUntilStep("stage has width", () => playfield.Stage.DrawWidth > 0);

        AddAssert("non-scratch keys centred", () =>
        {
            var nonScratchColumns = playfield.Stage.Columns.Where(c => !c.IsScratch).ToArray();
            var left = nonScratchColumns.Min(c => c.ScreenSpaceDrawQuad.TopLeft.X);
            var right = nonScratchColumns.Max(c => c.ScreenSpaceDrawQuad.TopRight.X);
            var nonScratchCentre = (left + right) / 2;

            return Precision.AlmostEquals(nonScratchCentre, playfield.ScreenSpaceDrawQuad.Centre.X, 1f);
        });
    }

    [Test]
    public void TestNoteFillsLaneWithoutCrossingBounds()
    {
        DrawableBmsHitObject drawable = null!;

        AddStep("seek to column 3 note", () => clock.CurrentTime = 1200);

        AddUntilStep("column 3 note alive", () =>
        {
            drawable = playfield.HitObjectContainer.AliveObjects.OfType<DrawableBmsHitObject>().FirstOrDefault(d => d.HitObject.Column == 3)!;
            return drawable != null && drawable.IsPresent;
        });

        AddAssert("note left aligns to lane", () =>
        {
            var column = playfield.Stage.Columns[3].HitObjectArea;
            var noteLeft = column.ToLocalSpace(drawable.ToScreenSpace(drawable.DrawRectangle.BottomLeft)).X;
            return Precision.AlmostEquals(noteLeft, 0, 0.5f);
        });

        AddAssert("note fills lane width", () =>
        {
            var column = playfield.Stage.Columns[3].HitObjectArea;
            return Precision.AlmostEquals(drawable.DrawWidth, column.DrawWidth, 0.5f);
        });
    }
}
