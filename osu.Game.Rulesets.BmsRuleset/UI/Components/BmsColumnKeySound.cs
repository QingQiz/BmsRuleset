using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;
using osu.Game.Audio;
using osu.Game.Rulesets.BmsRuleset.Audio;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.UI.Components;

public sealed partial class BmsColumnKeySound : CompositeDrawable
{
    public override bool IsPresent => false;

    private readonly BmsChartSampleSound keySound = new();
    private readonly BmsChartSampleSound landmineSound = new();
    private readonly BmsKeySoundCursor cursor;
    private readonly HitObjectContainer hitObjectContainer;

    private readonly IBindable<bool> samplePlaybackDisabled = new Bindable<bool>();

    public BmsColumnKeySound(IReadOnlyList<BmsHitObject> hitObjects, HitObjectContainer hitObjectContainer)
    {
        this.hitObjectContainer = hitObjectContainer;
        cursor = new BmsKeySoundCursor(hitObjects);

        InternalChildren = [keySound, landmineSound];
    }

    /// <summary>Plays a note-hit / LN-tail sample in this column's key channel.</summary>
    public void PlaySample(string samplePath)
    {
        if (string.IsNullOrEmpty(samplePath))
            return;

        keySound.SampleInfo = new BmsSampleInfo(samplePath);
        keySound.Play();
    }

    /// <summary>
    /// Plays the next pending note's key-sound on an empty press. Returns false (no sound) when
    /// sample playback is disabled (replay/autoplay) or no pending note has a sample.
    /// </summary>
    public void PlayKeySound()
    {
        if (samplePlaybackDisabled.Value)
            return;

        if (cursor.Next(Time.Current, hasNoteFinished) is not { } hitObject || string.IsNullOrEmpty(hitObject.SamplePath))
            return;

        PlaySample(hitObject.SamplePath);
    }

    /// <summary>Plays the landmine explosion sample (#WAV00) in this column's landmine channel.</summary>
    public void PlayLandmineSound(string samplePath)
    {
        if (string.IsNullOrEmpty(samplePath))
            return;

        landmineSound.SampleInfo = new BmsSampleInfo(samplePath);
        landmineSound.Play();
    }

    [BackgroundDependencyLoader(true)]
    private void load(ISamplePlaybackDisabler? samplePlaybackDisabler)
    {
        if (samplePlaybackDisabler == null)
            return;

        samplePlaybackDisabled.BindTo(samplePlaybackDisabler.SamplePlaybackDisabled);
    }

    /// <summary>
    /// A note is "finished" when its drawable has been judged, or when it has scrolled past
    /// without a drawable (expired from the pool). The past-BAD-window case is owned by the
    /// cursor's isPastBadWindow (its first loop skips those before calling this), so it isn't
    /// re-checked here. Scoped to this column's own HitObjectContainer.
    /// </summary>
    private bool hasNoteFinished(BmsHitObject hitObject)
    {
        var currentTime = Time.Current;

        DrawableBmsHitObject? drawable = null;

        foreach (var (entry, d) in hitObjectContainer.AliveEntries)
        {
            if (entry.HitObject == hitObject && d is DrawableBmsHitObject bmsD)
            {
                drawable = bmsD;
                break;
            }
        }

        if (drawable?.Judged == true)
            return true;

        if (currentTime > hitObject.StartTime && drawable == null)
            return true;

        return false;
    }
}
