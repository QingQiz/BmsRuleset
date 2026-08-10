using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using ManagedBass;
using ManagedBass.Fx;
using osu.Framework.Audio.Track;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing;

/// <summary>
///     Bypasses BASS reverse streams for BMS tracks on desktop BASS platforms.
/// </summary>
/// <remarks>
///     BASS_FX reverse streams can introduce discontinuities when many short tracks overlap.
///     BMS playback does not reverse tracks, so the tempo stream can be used directly.
/// </remarks>
internal static class BmsTrackAudioPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.TrackAudio";
    private static readonly AsyncLocal<bool> bms_track_scope = new();
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

            var trackBassType = typeof(Track).Assembly.GetType("osu.Framework.Audio.Track.TrackBass");
            var prepareStream = trackBassType == null ? null : AccessTools.Method(trackBassType, "prepareStream", [typeof(Stream), typeof(bool)]);
            var transpiler = AccessTools.Method(typeof(BmsTrackAudioPatcher), nameof(prepareStreamTranspiler));
            var missingMembers = new (string name, MemberInfo? member)[]
            {
                ("TrackBass.prepareStream", prepareStream),
                ("BmsTrackAudioPatcher.prepareStreamTranspiler", transpiler),
            }.Where(member => member.member == null).Select(member => member.name).ToArray();

            if (missingMembers.Length > 0)
            {
                BmsLogger.Log("BMS track audio: Cannot install Harmony patch. Missing: " + string.Join(", ", missingMembers), LogLevel.Error);
                return;
            }

            try
            {
                new Harmony(harmony_id).Patch(prepareStream, transpiler: new HarmonyMethod(transpiler));
                IsInstalled = true;
            }
            catch (Exception exception)
            {
                BmsLogger.Error(exception, "BMS track audio: Failed to install desktop reverse-stream workaround. Using framework defaults.");
            }
        }
    }

    internal static IDisposable EnterBmsTrackScope()
    {
        var previous = bms_track_scope.Value;
        bms_track_scope.Value = true;
        return new Scope(previous);
    }

    private static IEnumerable<CodeInstruction> prepareStreamTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var createReverse = AccessTools.Method(typeof(BassFx), nameof(BassFx.ReverseCreate), [typeof(int), typeof(float), typeof(BassFlags)]);
        var replacementCreateReverse = AccessTools.Method(typeof(BmsTrackAudioPatcher), nameof(createReverseStream));
        var reverseCalls = 0;

        foreach (var instruction in instructions)
        {
            if (instruction.Calls(createReverse))
            {
                instruction.operand = replacementCreateReverse;
                reverseCalls++;
            }

            yield return instruction;
        }

        if (reverseCalls != 1)
            throw new InvalidOperationException($"Expected one BASS reverse stream creation call, found {reverseCalls}.");
    }

    private static int createReverseStream(int stream, float blockLength, BassFlags flags) =>
        bms_track_scope.Value ? stream : BassFx.ReverseCreate(stream, blockLength, flags);

    private sealed class Scope(bool previous) : IDisposable
    {
        public void Dispose() => bms_track_scope.Value = previous;
    }
}
