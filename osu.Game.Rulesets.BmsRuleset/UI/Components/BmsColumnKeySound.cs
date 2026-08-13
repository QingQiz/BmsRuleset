using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;
using osu.Game.Audio;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Playback;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;
using osu.Game.Rulesets.BmsRuleset.UI.Objects;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.UI.Components;

public sealed partial class BmsColumnKeySound(IReadOnlyList<BmsHitObject> hitObjects, HitObjectContainer hitObjectContainer)
    : CompositeDrawable
{
    public override bool IsPresent => false;

    private readonly BmsKeySoundCursor cursor = new(hitObjects);

    private readonly IBindable<bool> samplePlaybackDisabled = new Bindable<bool>();

    [Resolved]
    private BmsSamplePlayback samplePlayback { get; set; } = null!;

    /// <summary>Triggers a note-hit / LN-tail definition through shared sample playback.</summary>
    public void PlaySample(ushort? sampleKey, int volume = 100)
    {
        if (sampleKey is { } key)
            samplePlayback.QueueLivePlay(key, volume);
    }

    /// <summary>
    /// Plays the next pending note's key-sound on an empty press. Returns false (no sound) when
    /// sample playback is disabled (replay/autoplay) or no pending note has a sample.
    /// </summary>
    public void PlayKeySound()
    {
        if (samplePlaybackDisabled.Value)
            return;

        if (cursor.Next(Time.Current, hasNoteFinished) is not { } hitObject)
            return;

        PlaySample(hitObject.SampleKey, hitObject.SampleVolume);
    }

    /// <summary>Triggers the landmine explosion definition (#WAV00).</summary>
    public void PlayLandmineSound(int volume = 100) => PlaySample(0, volume);

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
