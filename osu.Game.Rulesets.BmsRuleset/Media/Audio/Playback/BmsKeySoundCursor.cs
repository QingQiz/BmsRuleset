using System;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Playback;

/// <summary>
///     Passed invisible notes select persistent empty-press samples, with newer visible notes taking priority.
///     Pure per-column cursor that finds the next hit object whose key-sound should play on an
///     empty press. Linear-walks this column's sorted hit-object slice from a cursor that advances
///     forward, skipping landmines, notes past their head deadline, and notes the caller reports
///     as finished (judged or past its active lifetime). The cursor resets on a backward seek;
///     forward time progression (including normal gaps with no key press)
///     is handled by the linear scan, which advances past notes whose head deadline expired.
///     The caller supplies the core's result and activation query; selecting a sample does not
///     require a representation or advance judgement state.
/// </summary>
public sealed class BmsKeySoundCursor(IReadOnlyList<BmsHitObject> hitObjects, Func<BmsHitObject, double> getExpiryTime,
                                      IReadOnlyList<BmsInvisibleNote>? invisibleNotes = null)
{
    private int nextSoundIndex;
    private double lastSoundSearchTime = double.MinValue;

    /// <summary>
    /// Returns the next hit object whose key-sound should play at <paramref name="currentTime"/>,
    /// or <c>null</c>. <paramref name="isFinished"/> reports judged notes and notes which have
    /// passed while inactive.
    /// </summary>
    public BmsHitObject? Next(double currentTime, Func<BmsHitObject, bool> isFinished)
    {
        // A profile may give earlier notes wider windows, so start time alone cannot exclude them.
        if (currentTime < lastSoundSearchTime)
            nextSoundIndex = 0;

        lastSoundSearchTime = currentTime;

        if (invisibleNotes is { Count: > 0 })
        {
            var invisibleIndex = findFirstAtOrAfter(invisibleNotes, currentTime) - 1;

            if (invisibleIndex >= 0)
            {
                // Like beatoraja, a passed invisible note replaces the empty-press sample until
                // a newer visible note takes over. Its sound persists after its drawable expires.
                var sample = invisibleNotes[invisibleIndex];

                for (var i = findFirstAtOrAfter(hitObjects, currentTime) - 1; i >= 0; i--)
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

        var index = nextSoundIndex;

        while (index < hitObjects.Count && (hitObjects[index] is BmsLandmine || currentTime > getExpiryTime(hitObjects[index])))
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
