using System.Collections.Generic;
using System.Linq;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

/// <summary>Freezes imported chart and mod values into the standalone engine's contracts.</summary>
internal sealed class BmsEngineChart
{
    internal BmsHitObject[] Objects { get; }

    internal IReadOnlyDictionary<BmsHitObject, int> Ids { get; }

    internal Chart Chart { get; }

    internal GameplayProfile Profile { get; }

    internal SessionConfiguration Configuration { get; }

    internal ScoreConfiguration Score => Configuration.Score;

    internal double MaximumScore { get; }

    internal BmsEngineChart(BmsBeatmap beatmap, GameplayProfile? profile = null)
    {
        var columnCount = BmsLayout.GetTotalColumns(beatmap.LayoutVariant);
        var scratch = beatmap.LayoutVariant switch
        {
            BmsLayoutVariant.Bms5K or BmsLayoutVariant.Bms5K2P or BmsLayoutVariant.Bms5KDouble => 5,
            BmsLayoutVariant.Bme7K or BmsLayoutVariant.Bme7K2P or BmsLayoutVariant.Bme7KDouble => 7,
            _ => -1,
        };
        var notesByColumn = beatmap.HitObjects.Where(n => n is not BmsInvisibleNote).ToLookup(n => n.Column);
        // The host's chart traversal order is observable when simultaneous results clip a gauge.
        Objects = Enumerable.Range(0, columnCount).OrderBy(column => scratch < 0 || column > scratch ? column : column == 0 ? scratch : column - 1)
            .SelectMany(column => notesByColumn[column].OrderBy(n => n.StartTime)).ToArray();
        Ids = Objects.Select((note, id) => (note, id)).ToDictionary(pair => pair.note, pair => pair.id);
        Profile = profile ?? BmsGameplayProfile.INSTANCE;
        (Chart, Configuration) = Profile.Compile(Objects, new KeyChart(columnCount, Objects.Select(createKeyNote),
            endTime: beatmap.PracticeEndTime is { } practiceEndTime ? Time.FromMilliseconds(practiceEndTime) : null), beatmap.BeatmapInfo);
        MaximumScore = Score.MaximumScore(Chart.ScorePlan);
    }

    internal NoteTiming TimingFor(BmsHitObject note) => ((INoteTiming)Chart.Notes[Ids[note]].Rule).Timing;

    private static KeyNoteKind getKind(BmsHitObject note) => note switch
    {
        BmsLandmine => KeyNoteKind.Mine,
        BmsLongNote hold => hold.Kind,
        _ => KeyNoteKind.Tap,
    };

    private static KeyNote createKeyNote(BmsHitObject note, int id)
    {
        var longNote = note as BmsLongNote;
        var layout = note.Beatmap.LayoutVariant;
        return new KeyNote(id, note.Column, Time.FromMilliseconds(note.StartTime),
            Time.FromMilliseconds(longNote?.EndTime ?? note.StartTime), getKind(note),
            Directional: BmsLayout.IsScratchColumn(note.Column, layout)
                         && layout is not (BmsLayoutVariant.Keyboard24K or BmsLayoutVariant.Keyboard24KDouble),
            MineDamage: note is BmsLandmine mine ? mine.LandmineDamagePercent : 0);
    }
}
