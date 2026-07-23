using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.Replays;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.AutoGeneration;

[TestFixture]
[HeadlessTest]
public partial class BmsAutoPlayGeneration : OsuTestScene
{

    private static BmsBeatmap createBeatmap(params BmsHitObject[] objects)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
        };

        foreach (var hitObject in objects)
            beatmap.HitObjects.Add(hitObject);

        return beatmap;
    }

    private static bool contains(ReplayFrame frame, params BmsAction[] actions) => actions.All(action => ((BmsReplayFrame)frame).Actions.Contains(action));

    [Test]
    public void TestLongNoteChord()
    {
        var beatmap = createBeatmap(
            new BmsLongNote { StartTime = 1000, Column = 1, Duration = 2000 },
            new BmsLongNote { StartTime = 1000, Column = 2, Duration = 2000 });

        var generated = new BmsAutoGenerator(beatmap).Generate();

        Assert.That(generated.Frames, Has.Count.EqualTo(2));
        Assert.That(generated.Frames[0].Time, Is.EqualTo(1000));
        Assert.That(generated.Frames[1].Time, Is.EqualTo(3000));
        Assert.That(contains(generated.Frames[0], BmsAction.Key1, BmsAction.Key2), Is.True);
        Assert.That(contains(generated.Frames[1], BmsAction.Key1, BmsAction.Key2), Is.False);
    }

    [Test]
    public void TestLongNoteStair()
    {
        var beatmap = createBeatmap(
            new BmsLongNote { StartTime = 1000, Column = 1, Duration = 2000 },
            new BmsLongNote { StartTime = 2000, Column = 2, Duration = 2000 });

        var generated = new BmsAutoGenerator(beatmap).Generate();

        Assert.That(generated.Frames, Has.Count.EqualTo(4));
        Assert.That(generated.Frames[0].Time, Is.EqualTo(1000));
        Assert.That(generated.Frames[1].Time, Is.EqualTo(2000));
        Assert.That(generated.Frames[2].Time, Is.EqualTo(3000));
        Assert.That(generated.Frames[3].Time, Is.EqualTo(4000));
        Assert.That(contains(generated.Frames[0], BmsAction.Key1), Is.True);
        Assert.That(contains(generated.Frames[1], BmsAction.Key1, BmsAction.Key2), Is.True);
        Assert.That(contains(generated.Frames[2], BmsAction.Key1), Is.False);
        Assert.That(contains(generated.Frames[2], BmsAction.Key2), Is.True);
        Assert.That(contains(generated.Frames[3], BmsAction.Key2), Is.False);
    }

    [Test]
    public void TestLongNoteWithReleasePress()
    {
        var beatmap = createBeatmap(
            new BmsLongNote { StartTime = 1000, Column = 1, Duration = 2000 },
            new BmsHitObject { StartTime = 3000, Column = 2 });

        var generated = new BmsAutoGenerator(beatmap).Generate();

        Assert.That(generated.Frames, Has.Count.EqualTo(3));
        Assert.That(generated.Frames[0].Time, Is.EqualTo(1000));
        Assert.That(generated.Frames[1].Time, Is.EqualTo(3000));
        Assert.That(generated.Frames[2].Time, Is.EqualTo(3000 + BmsAutoGenerator.RELEASE_DELAY));
        Assert.That(contains(generated.Frames[0], BmsAction.Key1), Is.True);
        Assert.That(contains(generated.Frames[1], BmsAction.Key1), Is.False);
        Assert.That(contains(generated.Frames[1], BmsAction.Key2), Is.True);
        Assert.That(contains(generated.Frames[2], BmsAction.Key2), Is.False);
    }

    [Test]
    public void TestSingleLongNote()
    {
        var beatmap = createBeatmap(new BmsLongNote { StartTime = 1000, Column = 1, Duration = 2000 });

        var generated = new BmsAutoGenerator(beatmap).Generate();

        Assert.That(generated.Frames, Has.Count.EqualTo(2));
        Assert.That(generated.Frames[0].Time, Is.EqualTo(1000));
        Assert.That(generated.Frames[1].Time, Is.EqualTo(3000));
        Assert.That(contains(generated.Frames[0], BmsAction.Key1), Is.True);
        Assert.That(contains(generated.Frames[1], BmsAction.Key1), Is.False);
    }

    [Test]
    public void TestSingleNote()
    {
        var beatmap = createBeatmap(new BmsHitObject { StartTime = 1000, Column = 1 });

        var generated = new BmsAutoGenerator(beatmap).Generate();

        Assert.That(generated.Frames, Has.Count.EqualTo(2));
        Assert.That(generated.Frames[0].Time, Is.EqualTo(1000));
        Assert.That(generated.Frames[1].Time, Is.EqualTo(1000 + BmsAutoGenerator.RELEASE_DELAY));
        Assert.That(contains(generated.Frames[0], BmsAction.Key1), Is.True);
        Assert.That(contains(generated.Frames[1], BmsAction.Key1), Is.False);
    }

    [Test]
    public void TestSingleNoteChord()
    {
        var beatmap = createBeatmap(
            new BmsHitObject { StartTime = 1000, Column = 1 },
            new BmsHitObject { StartTime = 1000, Column = 2 });

        var generated = new BmsAutoGenerator(beatmap).Generate();

        Assert.That(generated.Frames, Has.Count.EqualTo(2));
        Assert.That(generated.Frames[0].Time, Is.EqualTo(1000));
        Assert.That(generated.Frames[1].Time, Is.EqualTo(1000 + BmsAutoGenerator.RELEASE_DELAY));
        Assert.That(contains(generated.Frames[0], BmsAction.Key1, BmsAction.Key2), Is.True);
        Assert.That(contains(generated.Frames[1], BmsAction.Key1, BmsAction.Key2), Is.False);
    }

    [Test]
    public void TestSingleNoteStair()
    {
        var beatmap = createBeatmap(
            new BmsHitObject { StartTime = 1000, Column = 1 },
            new BmsHitObject { StartTime = 2000, Column = 2 });

        var generated = new BmsAutoGenerator(beatmap).Generate();

        Assert.That(generated.Frames, Has.Count.EqualTo(4));
        Assert.That(generated.Frames[0].Time, Is.EqualTo(1000));
        Assert.That(generated.Frames[1].Time, Is.EqualTo(1000 + BmsAutoGenerator.RELEASE_DELAY));
        Assert.That(generated.Frames[2].Time, Is.EqualTo(2000));
        Assert.That(generated.Frames[3].Time, Is.EqualTo(2000 + BmsAutoGenerator.RELEASE_DELAY));
        Assert.That(contains(generated.Frames[0], BmsAction.Key1), Is.True);
        Assert.That(contains(generated.Frames[1], BmsAction.Key1), Is.False);
        Assert.That(contains(generated.Frames[2], BmsAction.Key2), Is.True);
        Assert.That(contains(generated.Frames[3], BmsAction.Key2), Is.False);
    }
}
