using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using ManagedBass;
using ManagedBass.Mix;
using osu.Framework.Audio.Mixing;
using osu.Framework.Logging;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Native;

internal static class BmsPcmMixerPatcher
{
    internal const string MIXER_IDENTIFIER = "bms-pcm";

    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.PcmFloatMixer";
    private static readonly object install_lock = new();

    internal static bool IsInstalled { get; private set; }

    internal static void InstallOnce()
    {
        if (!BmsAudioPlatform.SupportsNativeBass)
            return;

        lock (install_lock)
        {
            if (IsInstalled)
                return;

            var mixerType = typeof(AudioMixer).Assembly.GetType("osu.Framework.Audio.Mixing.Bass.BassAudioMixer");
            var createMixer = mixerType == null ? null : AccessTools.Method(mixerType, "createMixer");
            var transpiler = AccessTools.Method(typeof(BmsPcmMixerPatcher), nameof(createMixerTranspiler));

            if (createMixer == null || transpiler == null)
            {
                BmsLogger.Log("BMS PCM mixer: framework mixer creation members are unavailable.", LogLevel.Error);
                return;
            }

            try
            {
                var harmony = new Harmony(harmony_id);
                harmony.Patch(createMixer, transpiler: new HarmonyMethod(transpiler));
                IsInstalled = true;
            }
            catch (Exception exception)
            {
                BmsLogger.Error(exception, "BMS PCM mixer: failed to install the float mixer patch.");
            }
        }
    }

    private static IEnumerable<CodeInstruction> createMixerTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var createMixerStream = AccessTools.Method(typeof(BassMix), nameof(BassMix.CreateMixerStream), [typeof(int), typeof(int), typeof(BassFlags)]);
        var adjustFlagsMethod = AccessTools.Method(typeof(BmsPcmMixerPatcher), nameof(adjustFlags));
        var patchedCalls = 0;

        foreach (var instruction in instructions)
        {
            if (instruction.Calls(createMixerStream))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, adjustFlagsMethod);

                patchedCalls++;
            }

            yield return instruction;
        }

        if (patchedCalls != 2)
            throw new InvalidOperationException($"Expected two framework mixer creation calls, found {patchedCalls}.");
    }

    private static BassFlags adjustFlags(BassFlags flags, AudioMixer mixer) =>
        mixer.Identifier == MIXER_IDENTIFIER ? flags | BassFlags.Float : flags;
}
