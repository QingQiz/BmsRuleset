using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;

internal readonly record struct BmsVoicePlay(
    BmsPcmAsset Asset,
    int SampleKey,
    long TargetFrame,
    float Gain = 1,
    long SourceOffsetFrame = 0,
    int Epoch = 0,
    long? SourceEndFrame = null);

internal enum BmsVoiceCommandType : byte
{
    Play,
    Pause,
    Resume,
    SetMasterGain,
    ReplaceEpoch,
}

internal readonly record struct BmsVoiceCommand(
    BmsVoiceCommandType Type,
    long TargetFrame,
    int Epoch,
    BmsVoicePlay Play = default,
    float Value = 0);
