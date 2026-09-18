using System;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Playback;

/// <summary>
///     Passed invisible notes select persistent empty-press samples, with newer visible notes taking priority.
///     Pure per-column cursor that finds the next hit object whose key-sound should play on an
///     empty press. Linear-walks this column's sorted hit-object slice from a cursor that advances
///     forward, skipping landmines, notes past their slow BAD window, and notes the caller reports
///     as finished (judged / expired-without-drawable). The cursor repositions via binary search
///     only on a backward seek; forward time progression (including normal gaps with no key press)
///     is handled by the linear scan, which advances past notes whose slow BAD window expired.
///     Extracted from the old centralized keysound player so the seek/skip logic is unit-testable without a drawable
///     host; the caller supplies isFinished (which needs the column's live HitObjectContainer —
///     see BmsColumnKeySound.hasNoteFinished).
/// </summary>
public sealed class BmsKeySoundCursor(IReadOnlyList<BmsHitObject> hitObjects, IReadOnlyList<BmsInvisibleNote>? invisibleNotes = null)
{
    private int nextSoundIndex;
    private double lastSoundSearchTime = double.MinValue;

    /// <summary>
    /// Returns the next hit object whose key-sound should play at <paramref name="currentTime"/>,
    /// or <c>null</c>. <paramref name="isFinished"/> must return true for notes whose drawable is
    /// already judged or expired-without-drawable (the caller has the live HitObjectContainer).
    /// </summary>
    public BmsHitObject? Next(double currentTime, Func<BmsHitObject, bool> isFinished)
    {
        if (invisibleNotes is { Count: > 0 })
        {
            var invisibleIndex = findFirstAtOrAfter(invisibleNotes, currentTime) - 1;
            if (invisibleIndex >= 0)
            {
                // Like beatoraja, a passed invisible note replaces the empty-press sample until
                // a newer visible note takes over. Its sound persists after its drawable expires.
                var sample = invisibleNotes[invisibleIndex];
                for (var i = findFirstSoundCandidateIndex(currentTime) - 1; i >= 0; i--)
                {
                    var note = hitObjects[i];
                    if (note.StartTime < sample.StartTime)
                        break;

                    if (note is not BmsLandmine && !(note is BmsLongNote && isFinished(note)))
                        return note;
                }

                return sample;
            }
        }

        // Reposition only on a backward seek — the linear scan below already advances past notes
        // whose slow BAD window has expired, so forward time progression (normal gaps included)
        // needs no time-threshold reset.
        if (currentTime < lastSoundSearchTime)
            nextSoundIndex = findFirstSoundCandidateIndex(currentTime - maxLookAhead());

        lastSoundSearchTime = currentTime;

        var index = nextSoundIndex;

        while (index < hitObjects.Count && isPastBadWindow(hitObjects[index], currentTime))
            index++;

        while (index < hitObjects.Count)
        {
            var hitObject = hitObjects[index];

            if (hitObject is BmsLandmine || isFinished(hitObject))
            {
                index++;
                continue;
            }

            nextSoundIndex = index;
            return hitObject;
        }

        nextSoundIndex = index;
        return null;
    }

    /// <summary>
    /// A note's slow BAD window has fully expired at <paramref name="currentTime"/>.
    /// Mirrors the old centralized player's isPastBadWindow.
    /// </summary>
    private static bool isPastBadWindow(BmsHitObject hitObject, double currentTime)
        => currentTime > hitObject.StartTime + (hitObject.HitWindows?.WindowFor(HitResult.Ok) ?? BmsHitWindows.FALLBACK_BAD_WINDOW);

    /// <summary>
    /// Generous lookahead for the binary-search cursor reset, covering the widest possible slow
    /// BAD window. A larger value is safe — the linear scan advances past finished notes anyway.
    /// </summary>
    private static double maxLookAhead() => 1000;

    /// <summary>Binary search: first index where hitObjects[i].StartTime &gt;= time.</summary>
    private int findFirstSoundCandidateIndex(double time) => findFirstAtOrAfter(hitObjects, time);

    private static int findFirstAtOrAfter(IReadOnlyList<BmsHitObject> notes, double time)
    {
        var low = 0;
        var high = notes.Count;

        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (notes[middle].StartTime < time)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }
}
