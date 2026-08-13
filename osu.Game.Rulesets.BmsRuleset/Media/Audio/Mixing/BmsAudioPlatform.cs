using osu.Framework;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing;

/// <summary>
///     Describes the platforms where the ruleset can use its native BASS mixer path.
/// </summary>
internal static class BmsAudioPlatform
{
    internal static bool SupportsNativeBass =>
        RuntimeInfo.OS is RuntimeInfo.Platform.Linux or RuntimeInfo.Platform.Windows;
}
