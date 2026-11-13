using System.Linq;
using RhythmEngine;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

/// <summary>A read-only column view. All decisions and interaction state belong to the engine.</summary>
internal sealed class BmsJudgementView(BmsJudgementEngine engine, int index)
{
    internal int Index => index;
    internal int PressedDirections => engine.GetPressedDirections(Index);
    internal NoteSnapshot GetNote(BmsHitObject note) => engine.GetNote(note);
    internal BmsLandmineState GetLandmineState(BmsLandmine note) => engine.GetLandmineState(note);

    internal bool IsKeySoundFinished(BmsHitObject note, double time)
        => note is BmsNote or BmsLongNote
            ? GetNote(note).Parts.Single(p => p.Part == (note is BmsLongNote { EffectiveMode: BmsLongNoteMode.LongNote }
                ? "tail" : note is BmsLongNote ? "head" : "tap")).Resolved
            : time > note.StartTime;
}
