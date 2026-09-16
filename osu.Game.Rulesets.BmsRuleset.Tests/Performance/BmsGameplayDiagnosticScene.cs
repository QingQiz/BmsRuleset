#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Tests.Audio;
using osu.Game.Rulesets.BmsRuleset.Tests.Visualize;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Objects;
using osu.Game.Storyboards;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

// TestScene supplies an inherited constructor test; only the CLI should launch a performance capture.
[TestFixtureSource(nameof(no_test_cases))]
internal partial class BmsGameplayDiagnosticScene(BmsGameplayDiagnosticOptions options) : BmsPlayerTestScene
{
    private static readonly object[] no_test_cases = [];

    private bool sought;
    private bool playbackStarted;
    private long seekTimestamp;
    private long playbackTimestamp;
    private double seekTime;

    public bool Ready => playbackStarted && Stopwatch.GetElapsedTime(playbackTimestamp).TotalSeconds >= 2;

    public double ChartTime => Player?.GameplayClockContainer?.CurrentTime ?? -1;

    public double SimulationTime => Player?.DrawableRuleset?.FrameStableClock.CurrentTime ?? -1;

    public double EndTime { get; private set; }

    public double LoadMilliseconds { get; private set; }

    public double SeekMilliseconds { get; private set; }

    public BmsBeatmap Chart { get; private set; } = null!;

    private readonly Stopwatch loading = new();

    protected override bool UseFreshStoragePerRun => true;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(beatmap => new BmsAutoGenerator(beatmap).Generate().Frames.ToList(), options.Skin);

    protected override WorkingBeatmap CreateWorkingBeatmap(IBeatmap beatmap, Storyboard storyboard = null!)
        => new BmsWorkingBeatmap(base.CreateWorkingBeatmap(beatmap, storyboard), Audio);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var decoded = BmsBeatmapDecoder.DecodeBytes(File.ReadAllBytes(options.Chart),
            randomValueSelector: _ => 1, referenceBpmMode: options.ReferenceBpm);
        Chart = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset())
        {
            BranchRandomValueSelector = _ => 1,
            ReferenceBpmMode = options.ReferenceBpm,
        }.Convert();
        if (options.LongNoteMode != BmsLongNoteMode.Undefined)
            Chart.LockedLongNoteMode = options.LongNoteMode;
        BmsTestBeatmaps.SetupBeatmapInfo(Chart, ruleset);
        Chart.Metadata.Source = Path.GetDirectoryName(options.Chart)!;
        EndTime = Math.Min(Chart.BeatmapInfo.Length - 1000, (options.Start + options.Duration) * 1000);
        if (EndTime <= options.Start * 1000 + 2000)
            throw new ArgumentException("Requested interval must leave at least two seconds of playback before chart completion.");

        return Chart;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        var config = (BmsRulesetConfigManager)RulesetConfigs.GetConfigFor(new BmsRuleset())!;
        config.SetValue(BmsRulesetSetting.ScrollSpeed, options.ScrollSpeed);
        config.SetValue(BmsRulesetSetting.ReferenceBpmMode, options.ReferenceBpm);
        loading.Start();
        LoadPlayer();
    }

    protected override void Update()
    {
        base.Update();
        if (Player?.IsLoaded != true || Player.Alpha != 1)
            return;

        if (sought)
        {
            // Replay simulation may need several update frames to reconstruct a seek. Starting the
            // audio clock earlier would count that catch-up as gameplay inside the requested interval.
            if (!playbackStarted && Math.Abs(SimulationTime - seekTime) < 0.001)
            {
                SeekMilliseconds = Stopwatch.GetElapsedTime(seekTimestamp).TotalMilliseconds;
                Player.GameplayClockContainer.Start();
                playbackTimestamp = Stopwatch.GetTimestamp();
                playbackStarted = true;
            }

            return;
        }

        if (!Player.LoadedBeatmapSuccessfully)
            throw new InvalidOperationException("Player failed to load the chart.");

        LoadMilliseconds = loading.Elapsed.TotalMilliseconds;
        seekTime = Math.Max(0, options.Start * 1000 - 2000);
        Player.GameplayClockContainer.Stop();
        Player.GameplayClockContainer.Seek(seekTime);
        seekTimestamp = Stopwatch.GetTimestamp();
        sought = true;
    }

    public int AliveObjects()
    {
        var count = 0;
        foreach (var column in Playfield.Stage.Columns)
            count += column.HitObjectContainer.AliveEntries.Count;
        return count;
    }

    public string? CompletionError()
    {
        if (SimulationTime < EndTime - 100)
            return "Simulation could not keep up with the gameplay clock.";
        if (Player.ScoreProcessor.Statistics.Any(pair => pair.Key != HitResult.Perfect && pair.Value > 0))
            return "Autoplay produced a non-Perfect judgement.";

        var fullPlayback = options.Start == 0 && EndTime >= Chart.BeatmapInfo.Length - 1000;
        var expected = Chart.HitObjects.Where(h => h is not BmsLandmine
            && (fullPlayback || h.StartTime >= options.Start * 1000 && h.GetEndTime() <= EndTime - 400)).ToHashSet();
        var judged = Player.Results.Select(r => r.HitObject).Distinct().Count(h => h is BmsHitObject bms && expected.Contains(bms));
        if (judged != expected.Count)
            return $"Playback judged {judged} of {expected.Count} expected playable objects in the interval.";

        return null;
    }

    public object DescribeChart() => new
    {
        Chart.Metadata.Title,
        HitObjects = Chart.HitObjects.Count,
        Mines = Chart.HitObjects.Count(h => h is BmsLandmine),
        LongNotes = Chart.HitObjects.Count(h => h is BmsLongNote),
        Chart.LockedLongNoteMode,
        BackgroundEvents = Chart.BackgroundSampleEvents.Count,
        Samples = Chart.SampleDefinitions.Count,
        Stops = Chart.TimingMap?.StopEvents.Count,
        ScrollEvents = Chart.TimingMap?.ScrollEvents.Count,
        Chart.BeatmapInfo.Length,
        Judgements = Player?.ScoreProcessor?.Statistics.ToDictionary(p => p.Key.ToString(), p => p.Value),
    };

    public object DescribeAlive()
    {
        var objects = Playfield.Stage.Columns.SelectMany(c => c.HitObjectContainer.AliveEntries).ToArray();
        return new
        {
            ChartTime,
            SimulationTime,
            Playfield.ScrollSpeed,
            ReferenceBpm = Chart.TimingMap?.ScrollReferenceBpm,
            Alive = objects.Length,
            Present = objects.Count(p => p.Value.IsPresent),
            NotPresent = objects.Count(p => !p.Value.IsPresent),
            AudioVoices = BmsAudioTestAccess.GetActiveVoiceCount(((BmsDrawableRuleset)Player.DrawableRuleset).SamplePlayback),
            FutureSeconds = objects.GroupBy(p => (int)((p.Key.HitObject.StartTime - ChartTime) / 1000))
                .OrderBy(g => g.Key).Select(g => new { SecondsAhead = g.Key, Count = g.Count() }).ToArray(),
        };
    }
}
