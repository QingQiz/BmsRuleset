using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public partial class TestSceneBmsHitObject : OsuTestScene
{
    private ManualClock clock = null!;
    private BmsPlayfield playfield = null!;
    private DrawableBmsHitObject note = null!;
    private DrawableBmsHitObject longNote = null!;

    [SetUpSteps]
    public void SetUpSteps()
    {
        AddStep("create playfield", () =>
        {
            var objects = new[]
            {
                new BmsHitObject { StartTime = 1000, Column = 1 },
                new BmsHitObject { StartTime = 1500, Column = 2, IsLongNote = true, Duration = 1000 },
            };

            Children =
            [
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Clock = new FramedClock(clock = new ManualClock()),
                    Padding = new MarginPadding(80),
                    Child = playfield = new BmsPlayfield(objects, 8, BmsLayoutVariant.Bme7K, true)
                    {
                        TimeRange = 1200,
                    },
                },
            ];

            foreach (var hitObject in objects)
                playfield.Add(hitObject);
        });
    }

    [Test]
    public void TestNoteAndLongNoteVisibility()
    {
        AddStep("seek to notes", () => clock.CurrentTime = 900);
        AddUntilStep("note alive", () =>
        {
            note = playfield.HitObjectContainer.AliveObjects.OfType<DrawableBmsHitObject>().FirstOrDefault(d => !d.HitObject.IsLongNote)!;
            return note != null && note.IsPresent;
        });
        AddUntilStep("long note alive", () =>
        {
            longNote = playfield.HitObjectContainer.AliveObjects.OfType<DrawableBmsHitObject>().FirstOrDefault(d => d.HitObject.IsLongNote)!;
            return longNote != null && longNote.IsPresent;
        });

        AddAssert("note fills key lane", () => note.DrawWidth, () => Is.EqualTo(playfield.Stage.Columns[1].HitObjectArea.DrawWidth).Within(0.5));
        AddAssert("long note fills key lane", () => longNote.DrawWidth, () => Is.EqualTo(playfield.Stage.Columns[2].HitObjectArea.DrawWidth).Within(0.5));
        AddAssert("long note has positive height", () => longNote.DrawHeight, () => Is.GreaterThan(0));
    }
}
