using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;

// Keeping slice and pitch in the domain avoids changing retrigger identity when BMSON support is added.
internal readonly record struct BmsTerminationDomain(
    ushort SampleKey,
    int Pitch = 0,
    long SliceStartFrame = 0,
    long SliceFrameCount = -1);

internal readonly record struct BmsVoicePlay(
    BmsPcmAsset Asset,
    BmsTerminationDomain Domain,
    long TargetFrame,
    float Gain = 1,
    long SourceOffsetFrame = 0,
    int Epoch = 0,
    long VoiceId = 0);

internal enum BmsVoiceCommandType : byte
{
    Play,
    Pause,
    Resume,
    SetMasterGain,
    ReplaceEpoch,
    StopVoice,
    SetVoiceGain,
}

internal readonly record struct BmsVoiceCommand(
    BmsVoiceCommandType Type,
    long TargetFrame,
    int Epoch,
    BmsVoicePlay Play = default,
    float Value = 0,
    long VoiceId = 0);
