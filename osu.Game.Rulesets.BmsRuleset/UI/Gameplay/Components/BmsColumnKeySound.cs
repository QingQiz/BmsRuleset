using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;
using osu.Game.Audio;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Playback;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

internal sealed partial class BmsColumnKeySound : CompositeDrawable
{
    public override bool IsPresent => false;

    private readonly BmsKeySoundCursor cursor;
    private readonly BmsColumnJudgementContext judgements;
    private readonly Func<BmsHitObject, bool> isFinished;

    internal BmsColumnKeySound(IReadOnlyList<BmsHitObject> hitObjects, BmsColumnJudgementContext judgements,
                              IReadOnlyList<BmsInvisibleNote>? invisibleNotes = null)
    {
        this.judgements = judgements;
        cursor = new BmsKeySoundCursor(hitObjects, invisibleNotes);
        isFinished = hasNoteFinished;
    }

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

        if (cursor.Next(Time.Current, isFinished) is not { } hitObject)
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

    private bool hasNoteFinished(BmsHitObject note) => judgements.IsKeySoundFinished(note, Time.Current);
}
