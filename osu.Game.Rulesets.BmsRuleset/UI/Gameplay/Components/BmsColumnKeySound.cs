using System;
using System.Collections.Generic;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;
using osu.Game.Audio;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

internal sealed partial class BmsColumnKeySound : CompositeDrawable
{
    public override bool IsPresent => false;

    private readonly IBindable<bool> samplePlaybackDisabled = new Bindable<bool>();

    [Resolved]
    private ISkinSource skin { get; set; } = null!;

    [Resolved]
    private BmsSamplePlayback samplePlayback { get; set; } = null!;

    /// <summary>Triggers a note-hit / LN-tail definition through shared sample playback.</summary>
    public void PlaySample(int? sampleKey, int volume = 100)
    {
        if (sampleKey is { } key)
            samplePlayback.QueueLivePlay(key, volume);
    }

    internal void PlayKeySound(BmsHitObject note, KeySound sound)
    {
        if (sound.Action != KeySoundAction.Play)
        {
            samplePlayback.ControlLiveVoice(sound.SourceId, sound.Action == KeySoundAction.Stop, sound.Action == KeySoundAction.Mute);
            return;
        }

        if (samplePlaybackDisabled.Value)
            return;

        var tail = sound.Part == NoteParts.TAIL || sound.Part == KeySoundPolicies.STABLE_TAIL;
        if (tail)
        {
            var hold = (BmsLongNote)note;
            foreach (var sample in hold.GetTailSamples())
                PlaySample(sample.SampleKey, sample.Volume);

            if (hold.ManiaTailSamples is { } tailSamples)
                playSkinSamples(tailSamples);
            else if (sound.Part == KeySoundPolicies.STABLE_TAIL)
                playSkinSamples(note.Samples);
        }
        else
        {
            // Only the head's chart sample can be muted or stopped by a long note transition.
            int? owner = note is BmsLongNote ? sound.SourceId : null;
            foreach (var sample in note.GetSamples())
                samplePlayback.QueueLivePlay(sample.SampleKey, sample.Volume, owner);

            playSkinSamples(note.Samples);
        }
    }

    private void playSkinSamples(IEnumerable<HitSampleInfo> samples)
    {
        foreach (var info in samples)
        {
            if (skin.GetSample(info) is not { } sample) continue;

            var channel = sample.GetChannel();
            channel.Volume.Value = Math.Max(info.Volume, DrawableHitObject.MINIMUM_SAMPLE_VOLUME) / 100.0;
            channel.Play();
        }
    }

    [BackgroundDependencyLoader(true)]
    private void load(ISamplePlaybackDisabler? samplePlaybackDisabler)
    {
        if (samplePlaybackDisabler == null)
            return;

        samplePlaybackDisabled.BindTo(samplePlaybackDisabler.SamplePlaybackDisabled);
    }
}
