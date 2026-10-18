#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsColumnJudgement : BmsPlayerTestScene
{
    private static readonly JsonDocument baseline = readBaseline();
    private bool mixed;
    private bool culled;
    private double deadline;
    private BmsScoreProcessor score => (BmsScoreProcessor)Player.ScoreProcessor;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(60000)]);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2, Total = 24,
            LockedLongNoteMode = BmsLongNoteMode.ChargeNote,
            HitObjects =
            [
                new BmsNote { StartTime = 5000, Column = 1 },
                new BmsNote { StartTime = 5000, Column = 1 },
                new BmsLongNote { StartTime = 5000, Duration = 200, Column = 1 },
                new BmsNote { StartTime = 20000, Column = 2 },
            ],
        };
        if (mixed)
            beatmap.HitObjects.Add(new BmsLandmine { StartTime = 6000, Column = 1 });
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [Test]
    public void PassiveTraversalSurvivesPoolingAndRewind([Values] bool hide, [Values] bool includeMine)
    {
        load(hide, includeMine);
        seek(() => deadline);
        AddAssert("BAD boundary remains inclusive", () => score.JudgementEvents.Count == 0);
        seek(() => deadline + 1);
        capture("first passive traversal");
        AddAssert("both taps and charge endpoints receive POOR", () =>
            score.JudgementEvents.Count == 4 && score.JudgementEvents.All(e => e.Result == HitResult.Meh));
        seek(() => 5800);
        AddAssert("expired notes return to the pool", () =>
            !Playfield.AllColumnAliveObjects().Any(d => d.HitObject.StartTime == 5000));
        seek(() => 4900);
        AddAssert("rewind clears committed results", () => score.JudgementEvents.Count == 0);
        hideNotes();
        seek(() => deadline + 1);
        capture("passive traversal after reactivation");
        AddAssert("reactivated notes each judge once", () =>
            score.JudgementEvents.Count == 4 && score.JudgementEvents.All(e => e.Result == HitResult.Meh));
    }

    [TestCase(BmsJudgementAlgorithm.Combo)]
    [TestCase(BmsJudgementAlgorithm.Duration)]
    [TestCase(BmsJudgementAlgorithm.Lowest)]
    [TestCase(BmsJudgementAlgorithm.Score)]
    [TestCase(null)]
    public void DuplicateHeadsAcceptSuccessiveInputsAtOneTimestamp(BmsJudgementAlgorithm? algorithm)
    {
        load(false, false);
        seek(() => 5000);
        AddStep("press duplicate heads without advancing time", () =>
        {
            Playfield.JudgementAlgorithm = algorithm;
            var column = Playfield.Stage.Columns[1];
            PressColumn(column, 5000);
            ReleaseColumn(column, 5000);
            PressColumn(column, 5000);
        });
        capture("same timestamp inputs");
        AddAssert("distinct heads receive perfect results", () =>
            score.JudgementEvents.Count == 2 && score.JudgementEvents.All(e => e.Result == HitResult.Perfect));
        AddAssert("framework results retain separate object identities", () =>
            Player.Results.Select(r => r.HitObject).Distinct().Count() == 2);
    }

    private void load(bool hide, bool includeMine)
    {
        AddStep("load column scenario", () =>
        {
            mixed = includeMine;
            culled = hide;
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        seek(() => 4900);
        AddStep("read passive boundary", () =>
        {
            var note = (BmsHitObject)Player.GameplayState.Beatmap.HitObjects.First(h => h.StartTime == 5000);
            deadline = note.StartTime + BmsJudgementProfileProvider.GetTable(note.Beatmap.LayoutVariant,
                note.Column, note.EffectiveJudgementRate, false).SlowWindowFor(HitResult.Ok);
        });
        hideNotes();
    }

    private void hideNotes() => AddStep("apply requested visibility", () =>
    {
        if (!culled) return;
        foreach (var drawable in Playfield.AllColumnAliveObjects().OfType<DrawableBmsHitObject>())
        {
            if (drawable.HitObject.StartTime != 5000) continue;
            drawable.HitObject.ScrollPositionAtStartTime = -1_000_000;
            if (drawable.HitObject is BmsLongNote note)
                note.ScrollPositionAtEndTime = note.VisualScrollPositionAtEndTime = -1_000_000;
        }
    });

    private void seek(Func<double> time)
    {
        AddStep("seek checkpoint", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(time());
        });
        AddUntilStep("simulation reached checkpoint", () =>
            Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time()) < 0.001);
    }

    private void capture(string checkpoint)
    {
        var scenario = TestContext.CurrentContext.Test.MethodName!;
        AddStep(checkpoint, () =>
        {
            var objects = Player.GameplayState.Beatmap.HitObjects.ToArray();
            var trace = JsonSerializer.Serialize(new
            {
                checkpoint,
                events = score.JudgementEvents,
                results = Player.Results.Select(r => new { id = Array.IndexOf(objects, r.HitObject), r.Type, r.TimeAbsolute, r.GameplayRate }),
                progression = BmsExScore.CreateProgression(score.JudgementEvents),
                combo = score.Combo.Value,
                total = score.TotalScore.Value,
                health = ((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory,
            });
            TestContext.WriteLine("P2_TRACE " + trace);
            var expected = baseline.RootElement.GetProperty("traces").GetProperty(scenario).GetProperty(checkpoint);
            Assert.That(trace, Is.EqualTo(JsonSerializer.Serialize(expected)), "ordered trace with logical judgement deadlines");
        });
    }

    private static JsonDocument readBaseline()
    {
        using var stream = typeof(TestSceneBmsColumnJudgement).Assembly.GetManifestResourceStream(
            "osu.Game.Rulesets.BmsRuleset.Tests.reference_fixtures.column_judgement_p1_trace.json")
                           ?? throw new InvalidDataException("Missing P1 column judgement trace.");
        return JsonDocument.Parse(stream);
    }
}
