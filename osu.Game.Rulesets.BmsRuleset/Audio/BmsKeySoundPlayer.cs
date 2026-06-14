using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;
using osu.Game.Audio;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Audio;

/// <inheritdoc />
/// <summary>
///     Drives key-sound and landmine-sound lookup and playback for a BMS playfield.
///     Maintains per-column search state so that successive key presses scan forward
///     through the sorted hit-object list without restarting from the beginning.
/// </summary>
public sealed partial class BmsKeySoundPlayer : CompositeDrawable
{
    public override bool IsPresent => false;

    public BmsKeySoundPlayer(IReadOnlyList<BmsHitObject> hitObjects, BmsHitObjectContainer hitObjectContainer, Func<double> getCurrentTime, int totalColumns)
    {
        this.hitObjects = hitObjects;
        this.hitObjectContainer = hitObjectContainer;
        this.getCurrentTime = getCurrentTime;
        keySounds = new BmsChartSampleSound[totalColumns];

        for (var i = 0; i < totalColumns; i++)
        {
            keySounds[i] = new BmsChartSampleSound();
            AddInternal(keySounds[i]);
        }

        AddInternal(landmineSound);
    }

    [BackgroundDependencyLoader(true)]
    private void load(ISamplePlaybackDisabler? samplePlaybackDisabler)
    {
        if (samplePlaybackDisabler == null)
            return;

        samplePlaybackDisabled.BindTo(samplePlaybackDisabler.SamplePlaybackDisabled);
    }

    #region Fields

    private readonly IReadOnlyList<BmsHitObject> hitObjects;
    private readonly BmsHitObjectContainer hitObjectContainer;
    private readonly Func<double> getCurrentTime;
    private readonly BmsChartSampleSound[] keySounds;
    private readonly BmsChartSampleSound landmineSound = new();
    private readonly IBindable<bool> samplePlaybackDisabled = new Bindable<bool>();

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

    #region Helpers

    /// <summary>
    ///     A note is "finished" when its drawable has already been judged, when it has
    ///     scrolled past without a drawable (expired from the pool), or when it is
    ///     beyond the widest late hit window (BAD window).
    /// </summary>
    private bool hasNoteFinished(BmsHitObject hitObject)
    {
        var currentTime = getCurrentTime();

        hitObjectContainer.TryGetAliveDrawable(hitObject, out var drawable);

        if (drawable?.Judged == true)
            return true;

        if (currentTime > hitObject.StartTime && drawable == null)
            return true;

        return currentTime > hitObject.StartTime + hitObject.HitWindows.WindowFor(HitResult.Ok);
    }

    /// <summary>
    ///     Whether <paramref name="hitObject"/>'s late BAD window has fully expired
    ///     at <paramref name="currentTime"/>.
    /// </summary>
    private static bool isPastBadWindow(BmsHitObject hitObject, double currentTime)
        => currentTime > hitObject.StartTime + (hitObject.HitWindows?.WindowFor(HitResult.Ok) ?? BmsHitWindows.FALLBACK_BAD_WINDOW);

    /// <summary>
    ///     Generous lookahead for the binary-search cursor reset, covering the
    ///     widest possible late BAD window across all supported window implementations.
    ///     A larger value is safe — the linear scan advances past finished notes anyway.
    /// </summary>
    private static double maxLookAhead() => 1000;

    #endregion

    #region Public methods

    public void PlaySample(int column, string samplePath)
    {
        if (string.IsNullOrEmpty(samplePath))
            return;

        var col = Math.Clamp(column, 0, keySounds.Length - 1);
        keySounds[col].SampleInfo = new BmsSampleInfo(samplePath);
        keySounds[col].Play();
    }

    public bool PlayKeySound(int column)
    {
        if (samplePlaybackDisabled.Value)
            return false;

        if (findNextSoundHitObject(column) is not { } hitObject || string.IsNullOrEmpty(hitObject.SamplePath))
            return false;

        PlaySample(column, hitObject.SamplePath);
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
            nextSoundIndexByColumn[column] = findFirstSoundCandidateIndex(currentTime - maxLookAhead());
        }

        lastSoundSearchTimeByColumn[column] = currentTime;

        var index = nextSoundIndexByColumn.GetValueOrDefault(column);

        while (index < hitObjects.Count && isPastBadWindow(hitObjects[index], currentTime))
            index++;

        while (index < hitObjects.Count)
        {
            var hitObject = hitObjects[index];

            if (hitObject.Column != column || hitObject.IsMine || hasNoteFinished(hitObject))
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
