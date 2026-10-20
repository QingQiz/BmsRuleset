using System;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;

// Each rule owns its mode's transitions over this shared endpoint state.
internal struct BmsHoldRuntime
{
    internal readonly BmsLongNote Note;
    internal BmsJudgementWindowTable HeadWindow = null!;
    internal BmsJudgementWindowTable TailWindow = null!;
    internal bool HeadJudged;
    internal bool Started;
    internal bool TailJudged;
    internal bool EarlyReleaseOutsideWindow;
    internal bool HeadReverseScratch;
    internal double HeadOffset;
    internal HitResult HeadResult;
    internal HitResult TailResult;
    internal double? TailTime;
    internal double? PendingRelease;

    internal BmsHoldRuntime(BmsLongNote note)
    {
        Note = note;
        RefreshWindows();
    }

    internal readonly bool IsScratch => BmsLayout.IsScratchColumn(Note.Column, Note.Beatmap.LayoutVariant);

    internal readonly double ReleaseMargin => BmsJudgementProfileProvider.IsPms(Note.Beatmap.LayoutVariant) ? 200 : 0;

    internal readonly bool Holding => Started && !TailJudged;

    internal readonly double NextEventTime(BmsLongNoteMode mode)
    {
        if (!HeadJudged) return BmsJudgementWindowTable.TimeAfter(Note.StartTime, HeadWindow.SlowWindowFor(HitResult.Ok));

        var next = double.PositiveInfinity;
        if (!TailJudged)
        {
            next = mode == BmsLongNoteMode.LongNote && Started
                ? BmsJudgementWindowTable.TimeAfter(Note.EndTime, 0)
                : BmsJudgementWindowTable.TimeAfter(Note.EndTime, HeadWindow.SlowWindowFor(HitResult.Ok));
            if (PendingRelease is { } released) next = Math.Min(next, released + ReleaseMargin);
        }

        if (Started && TailJudged)
            next = Math.Min(next, mode == BmsLongNoteMode.HellChargeNote ? Note.EndTime : BmsJudgementWindowTable.TimeAfter(Note.EndTime, 50));
        return next;
    }

    internal readonly BmsLongNoteState Snapshot(bool activeBody = false)
        => new(HeadJudged, Started, TailJudged, HeadResult, HeadJudged ? Note.StartTime + HeadOffset : null,
            TailResult, TailTime, PendingRelease, activeBody, EarlyReleaseOutsideWindow);

    internal void RefreshWindows()
    {
        HeadWindow = BmsJudgementProfileProvider.GetTable(Note.Beatmap.LayoutVariant, Note.Column, Note.EffectiveJudgementRate, false);
        TailWindow = BmsJudgementProfileProvider.GetTable(Note.Beatmap.LayoutVariant, Note.Column, Note.EffectiveJudgementRate, true);
    }

    internal readonly BmsLongNoteEndpointResult Endpoint(BmsLongNoteEndpointKind kind, double time, double rate, HitResult result, double? applicationTime = null)
        => new(Note, kind, time, rate, result, applicationTime ?? time);
}

internal sealed class BmsLongNoteRuntime(BmsLongNote note)
{
    internal BmsHoldRuntime Hold = new(note);
    internal BmsLongNoteEndpointResult? HeadEndpoint;

    internal BmsLongNoteState State => Hold.Snapshot();
}

internal sealed class BmsChargeNoteRuntime(BmsLongNote note)
{
    internal BmsHoldRuntime Hold = new(note);

    internal BmsLongNoteState State => Hold.Snapshot();
}

internal sealed class BmsHellChargeNoteRuntime(BmsLongNote note)
{
    internal BmsHoldRuntime Hold = new(note);
    internal readonly BmsHellChargeBodyTracker Body = new();
    internal double LastBodyTime = double.NegativeInfinity;

    internal BmsLongNoteState State => Hold.Snapshot(Hold.Started && LastBodyTime < Hold.Note.EndTime);
}

internal readonly record struct BmsReleaseContext(double InputTime, BmsFrameContext Frame, bool ReverseScratch = false);
