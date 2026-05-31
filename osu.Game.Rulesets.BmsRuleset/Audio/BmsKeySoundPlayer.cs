using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.Audio;

/// <summary>
///     Drives key-sound and landmine-sound lookup and playback for a BMS playfield.
///     Maintains per-column search state so that successive key presses scan forward
///     through the sorted hit-object list without restarting from the beginning.
/// </summary>
internal class BmsKeySoundPlayer(
    IReadOnlyList<BmsHitObject> hitObjects,
    HitObjectContainer hitObjectContainer,
    Func<double> getCurrentTime,
    IBindable<bool> samplePlaybackDisabled,
    BmsChartSampleSound keySound,
    BmsChartSampleSound landmineSound)
{

    #region Helpers

    /// <summary>
    ///     A note is "finished" when its drawable has already been judged, when it has
    ///     scrolled past without a drawable (expired from the pool), or when it is
    ///     beyond the widest late hit window (BAD window).
    /// </summary>
    private bool hasNoteFinished(BmsHitObject hitObject)
    {
        var currentTime = getCurrentTime();

        var drawable = hitObjectContainer.AliveObjects
            .OfType<DrawableBmsHitObject>()
            .FirstOrDefault(d => ReferenceEquals(d.HitObject, hitObject));

        if (drawable?.Judged == true)
            return true;

        if (currentTime > hitObject.StartTime && drawable == null)
            return true;

        return currentTime > hitObject.StartTime + hitObject.HitWindows.WindowFor(HitResult.Ok);
    }

    #endregion

    #region Fields

    /// <summary>
    ///     Per-column cursor into the sorted hit-object list.  Advances forward on each
    ///     key press so we never re-scan already-skipped notes.
    /// </summary>
    private readonly Dictionary<int, int> nextSoundIndexByColumn = new();

    /// <summary>
    ///     Tracks the last seek time per column.  When the current time jumps backwards
    ///     or forwards more than 5 s we reset the per-column cursor via binary search.
    /// </summary>
    private readonly Dictionary<int, double> lastSoundSearchTimeByColumn = new();

    #endregion

    #region Public methods

    /// <summary>
    ///     Finds the next unjudged note in <paramref name="column" /> that is still
    ///     within a playable window, then plays its declared key sound.  Returns
    ///     <c>true</c> when a sound was triggered.
    /// </summary>
    public bool PlayKeySound(int column)
    {
        if (samplePlaybackDisabled.Value)
            return false;

        if (findNextSoundHitObject(column) is not { } hitObject || string.IsNullOrEmpty(hitObject.SamplePath))
            return false;

        keySound.SampleInfo = new BmsSampleInfo(hitObject.SamplePath);
        keySound.Play();
        return true;
    }

    /// <summary>
    ///     Plays the landmine explosion sample (<c>#WAV00</c> by convention) when a
    ///     landmine detonates.
    /// </summary>
    public void PlayLandmineSound(string samplePath)
    {
        landmineSound.SampleInfo = new BmsSampleInfo(samplePath);
        landmineSound.Play();
    }

    #endregion

    #region Search

    /// <summary>
    ///     Linear-walks the sorted hit-object list from the per-column cursor,
    ///     skipping notes that are already finished or belong to other columns.  The
    ///     cursor is reset via binary search when time jumps (seek / pause-resume).
    /// </summary>
    private BmsHitObject? findNextSoundHitObject(int column)
    {
        var currentTime = getCurrentTime();

        if (!lastSoundSearchTimeByColumn.TryGetValue(column, out var lastSearchTime)
            || currentTime < lastSearchTime
            || currentTime - lastSearchTime > 5000)
        {
            nextSoundIndexByColumn[column] = findFirstSoundCandidateIndex(currentTime - BmsHitWindows.BAD_WINDOW);
        }

        lastSoundSearchTimeByColumn[column] = currentTime;

        var index = nextSoundIndexByColumn.GetValueOrDefault(column);

        while (index < hitObjects.Count && hitObjects[index].StartTime < currentTime - BmsHitWindows.BAD_WINDOW)
            index++;

        while (index < hitObjects.Count)
        {
            var hitObject = hitObjects[index];

            if (hitObject.Column != column || hasNoteFinished(hitObject))
            {
                index++;
                continue;
            }

            nextSoundIndexByColumn[column] = index;
            return hitObject;
        }

        nextSoundIndexByColumn[column] = index;
        return null;
    }

    /// <summary>
    ///     Binary search: returns the first index where
    ///     <c>hitObjects[i].StartTime >= time</c>.
    /// </summary>
    private int findFirstSoundCandidateIndex(double time)
    {
        var low = 0;
        var high = hitObjects.Count;

        while (low < high)
        {
            var middle = low + (high - low) / 2;

            if (hitObjects[middle].StartTime < time)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    #endregion

}
