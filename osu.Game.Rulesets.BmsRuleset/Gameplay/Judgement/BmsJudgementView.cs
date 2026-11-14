using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;

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
                ? NoteParts.TAIL : note is BmsLongNote ? NoteParts.HEAD : NoteParts.TAP)).Resolved
            : time > note.StartTime;
}
