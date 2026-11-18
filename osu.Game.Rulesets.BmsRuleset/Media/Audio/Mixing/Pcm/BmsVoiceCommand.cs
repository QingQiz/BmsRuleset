using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;

internal readonly record struct BmsVoicePlay(
    BmsPcmAsset Asset,
    int SampleKey,
    long TargetFrame,
    float Gain = 1,
    long SourceOffsetFrame = 0,
    int Epoch = 0,
    long? SourceEndFrame = null,
    int? Owner = null,
    bool Muted = false);

internal enum BmsVoiceCommandType : byte
{
    Play,
    Pause,
    Resume,
    SetMasterGain,
    ReplaceEpoch,
    StopVoice,
    SetVoiceMuted,
}

internal readonly record struct BmsVoiceCommand(
    BmsVoiceCommandType Type,
    long TargetFrame,
    int Epoch,
    BmsVoicePlay Play = default,
    float Value = 0,
    int? Owner = null);
