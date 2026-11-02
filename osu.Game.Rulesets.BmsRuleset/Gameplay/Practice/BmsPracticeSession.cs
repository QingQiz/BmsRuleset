using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Framework.Audio;
using osu.Framework.Utils;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Conversion;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

public sealed class BmsPracticeSession
{
    public WorkingBeatmap Source { get; }

    public BmsPracticeSettings Settings { get; }

    public IReadOnlyList<Mod> Mods { get; }

    public int Attempts { get; private set; }

    private BmsModBranchReplay? branchReplay;
    private readonly object conversionLock = new();
    private readonly List<BmsPracticeAttemptResult> attemptResults = [];

    internal IReadOnlyList<BmsPracticeAttemptResult> AttemptResults => attemptResults;

    public BmsPracticeSession(WorkingBeatmap source, BmsPracticeSettings settings, IReadOnlyList<Mod> mods)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsValid)
            throw new ArgumentOutOfRangeException(nameof(settings));

        Source = source;
        Settings = settings;
        var prepared = PrepareMods(mods);
        branchReplay = prepared.OfType<BmsModBranchReplay>().SingleOrDefault();
        Mods = Array.AsReadOnly(prepared.Where(mod => mod.Type != ModType.System).ToArray());
    }

    public void BeginAttempt() => Attempts++;

    internal void AddAttemptResult(BmsPracticeAttemptResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (attemptResults.Any(existing => existing.Attempt == result.Attempt))
            return;

        attemptResults.Add(result);
    }

    public WorkingBeatmap CreateWorkingBeatmap(AudioManager audio) => new BmsPracticeWorkingBeatmap(this, audio);

    internal BmsBeatmap GetFullBeatmap(CancellationToken cancellationToken = default)
    {
        lock (conversionLock)
        {
            var beatmap = GetFullBeatmap(Source, branchReplay == null ? Mods : [.. Mods, branchReplay], cancellationToken);
            branchReplay ??= CreateBranchReplay(beatmap);
            return beatmap;
        }
    }

    internal static BmsModBranchReplay? CreateBranchReplay(BmsBeatmap beatmap)
        => beatmap.BranchDecisions.Count == 0 ? null : new BmsModBranchReplay
        {
            Decisions = { Value = BmsChartParser.SerialiseBranchDecisions(beatmap.BranchDecisions) },
        };

    internal static bool SupportsBeatmap(IBeatmapInfo source) => source.Ruleset.ShortName == Constant.SHORT_NAME
        || BmsForeignBeatmapConverterRegistry.FindConverter(source) != null;

    public static Mod[] PrepareMods(IEnumerable<Mod> mods)
    {
        var result = mods.Select(mod => mod.DeepClone()).ToArray();
        foreach (var seeded in result.OfType<IHasSeed>())
            seeded.Seed.Value ??= RNG.Next();

        foreach (var gauge in result.OfType<BmsModGauge>())
        {
            gauge.InitialHealth.Value = null;
            gauge.GaugeProfileFamily.Value = null;
        }

        foreach (var gauge in result.OfType<BmsModAutoGauge>())
        {
            gauge.ClassHealth.Value = null;
            gauge.ExClassHealth.Value = null;
            gauge.ExHardClassHealth.Value = null;
            gauge.GaugeProfileFamily.Value = null;
        }

        return result;
    }

    public static BmsBeatmap GetFullBeatmap(WorkingBeatmap source, IReadOnlyList<Mod> mods, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var beatmap = source.GetPlayableBeatmap(new BmsRuleset().RulesetInfo, mods, cancellationToken) as BmsBeatmap
                      ?? throw new InvalidOperationException("The selected beatmap cannot be converted to BMS.");
        cancellationToken.ThrowIfCancellationRequested();
        return beatmap;
    }
}
