using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal delegate bool BmsPressOperation(int slot, HitResult result, in BmsPressContext press);
internal delegate void BmsPassiveOperation(int slot, in BmsFrameContext frame);

internal readonly record struct BmsNoteRegistration(BmsHitObject Object, int Slot, BmsPressOperation Press, BmsPassiveOperation Passive);

/// <summary>
/// Each column caches two calls, shared by all its ordinary notes.
/// </summary>
internal sealed class BmsNoteOperationBinding
{
    private readonly BmsColumnJudgementContext column;
    private readonly BmsJudgementExecutionContext execution;

    internal BmsNoteOperationBinding(BmsColumnJudgementContext column, IBmsJudgementOutput output)
    {
        this.column = column;
        execution = new BmsJudgementExecutionContext(column, output);
        Press = tryPress;
        Passive = checkPassive;
    }

    internal BmsPressOperation Press { get; }
    internal BmsPassiveOperation Passive { get; }

    private bool tryPress(int slot, HitResult result, in BmsPressContext press)
        => column.Rules.Note.TryPress(column.NoteRegistrations[slot].Object, execution, result, press);

    private void checkPassive(int slot, in BmsFrameContext frame)
        => column.Rules.Note.CheckPassive(column.NoteRegistrations[slot].Object, column.NoteWindows[slot], execution, frame);
}
