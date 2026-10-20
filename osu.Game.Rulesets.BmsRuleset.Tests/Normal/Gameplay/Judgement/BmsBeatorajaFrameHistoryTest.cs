using System;
using NUnit.Framework;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsBeatorajaFrameHistoryTest
{
    [TestCase(1200, .24)]
    [TestCase(1200.5, .26)]
    [TestCase(1201, .26)]
    [TestCase(2399.75, .36)]
    public void SteadyAndUnobservedTimesReplayTheSameGaugeTicks(double rewindTime, double expectedHealth)
    {
        using var run = new Run();
        run.Head();
        for (var time = 1001; time <= 2801; time++)
            run.Frame(time, 1, true);
        run.Expect(.42);

        run.Rewind(rewindTime);
        run.Expect(expectedHealth);
        for (var time = (int)rewindTime + 1; time <= 2801; time++)
            run.Frame(time, 1, true);
        run.Expect(.42);
        run.Frame(3000, 199, true);
        run.Release(3000);
        run.Expect(.46);
    }

    // GaugeProperty.NORMAL: TOTAL12 / three endpoints => +4%,
    // half GREAT +2%, half BAD -1.5%. These expectations do not call the calculator.
    [TestCase(1650)]
    [TestCase(1650.5)]
    public void DelayedUpdateSettlesAllTicksAndRewindRestoresElapsedTime(double rewindTime)
    {
        using var run = new Run();
        run.Head();
        run.Frame(1650, 650, true); run.Expect(0.30);
        run.Frame(1651, 1, true); run.Expect(0.30);
        run.Rewind(rewindTime); run.Expect(0.30);
        run.Frame(1651, 1, true); run.Expect(0.30);
        run.Frame(1652, 1, true); run.Expect(0.30);
        run.Frame(1800, 148, true); run.Expect(0.30);
        run.Frame(1801, 1, true); run.Expect(0.32);
    }

    [Test]
    public void SignedHeldAndReleasedIntervalsCancelBeforeGaugeTick()
    {
        using var run = new Run();
        run.Head();
        run.Frame(1150, 150, true); run.Expect(0.24);
        run.Frame(1250, 100, false); run.Expect(0.24);
        run.Frame(1400, 150, true); run.Expect(0.24);
        run.Frame(1401, 1, true); run.Expect(0.26);
        run.Frame(1602, 201, false); run.Expect(0.26);
        run.Frame(1603, 1, false); run.Expect(0.245);
        run.Rewind(1250); run.Expect(0.24);
        run.Frame(1400, 150, true); run.Frame(1401, 1, true);
        run.Frame(1602, 201, false); run.Frame(1603, 1, false); run.Expect(0.245);
    }

    [Test]
    public void HeldAndReleasedIntervalsSettleIndependentlyOfUpdateCadence()
    {
        using var run = new Run();
        run.Head();
        run.Frame(1650, 650, true); run.Expect(0.30);
        run.Release(1650); run.Expect(0.24);
        run.Frame(1750, 100, false); run.Expect(0.24);
        run.Frame(2301, 551, false); run.Expect(0.195);
        run.Frame(2951, 650, true); run.Expect(0.255);
        run.Rewind(1750); run.Expect(0.24);
        run.Frame(2301, 551, false); run.Frame(2951, 650, true); run.Expect(0.255);
    }

    [TestCase(2999)]
    [TestCase(3000)]
    [TestCase(3001)]
    public void TailClipsElapsedBodyTimeAndReplaysWithoutExtraTicks(double rewindTime)
    {
        using var run = new Run();
        run.Head();
        run.Frame(2999, 1999, true); run.Expect(0.42);
        run.Frame(3000, 1, true); run.Release(3000); run.Expect(0.46);
        run.Frame(3001, 1, true); run.Expect(0.46);
        run.Rewind(rewindTime);
        if (rewindTime < 3000)
        {
            run.Expect(0.42);
            run.Frame(3000, 1, true); run.Release(3000);
        }
        run.Frame(3002, 2, true); run.Expect(0.46);
    }

    private sealed class Run : IDisposable
    {
        private readonly BmsLongNote note;
        private readonly BmsLongNoteTestContext context;
        private readonly BmsHealthProcessor health;
        private readonly ManualClock clock = new();
        private readonly FramedClock framed;

        public Run()
        {
            framed = new FramedClock(clock);
            note = new BmsLongNote { StartTime = 1000, Duration = 2000, Column = 1 };
            var beatmap = new BmsBeatmap
            {
                LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2,
                Total = 12, LockedLongNoteMode = BmsLongNoteMode.HellChargeNote,
                HitObjects = [note, new BmsNote { StartTime = 10000, Column = 1 }],
            };
            note.Beatmap = beatmap;
            health = new BmsHealthProcessor { Clock = framed };
            health.ApplyBeatmap(beatmap);
            context = new BmsLongNoteTestContext(note);
            context.BeforeRebuild = health.ResetReplayState;
            context.BeforeOperation = time;
            context.Output.BeforeResultCheckpoint += (_, _) => health.ApplyResult(new BmsLongNoteJudgementResult(note, note.CreateJudgement(), context.Output.GetEndpoints(note)) { Type = context.Output.GetMainResult(note).Type });
            context.Output.SyntheticEndpoint += (endpoint, applied) =>
            {
                if (applied) health.ApplySyntheticLongNoteEndpoint(new BmsLongNoteJudgementResult(note.CreateSyntheticEndpoint(endpoint.ExpectedTime), note.CreateJudgement(), [endpoint]) { Type = endpoint.Result });
            };
            context.Output.BodyTick += (_, holding, scale, frame) => health.ApplyHellChargeTick(holding, scale, frame.SimulationTime);
        }

        private void time(double t) { clock.CurrentTime = t; framed.ProcessFrame(); }
        public void Head() { time(1000); context.Press(1000, HitResult.Perfect); Expect(0.24); }
        public void Frame(double t, double elapsed, bool holding) { time(t); context.Frame(t, elapsed, holding); }
        public void Release(double t)
        {
            time(t);
            context.Release(t);
        }
        public void Rewind(double t) { context.Rewind(t); time(t); }
        public void Expect(double value) => Assert.That(health.Health.Value, Is.EqualTo(value).Within(1e-10), "HCN health at " + clock.CurrentTime);
        public void Dispose() => health.Dispose();
    }
}
