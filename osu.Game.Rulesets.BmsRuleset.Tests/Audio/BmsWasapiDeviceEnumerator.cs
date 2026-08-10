#nullable enable

using System;
using ManagedBass;
using ManagedBass.Wasapi;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

internal static class BmsWasapiDeviceEnumerator
{
    public static void PrintDevices()
    {
        for (var index = 0; BassWasapi.GetDeviceInfo(index, out var info); index++)
        {
            Console.WriteLine(
                $"BMS_WASAPI_DEVICE index={index} enabled={info.IsEnabled} input={info.IsInput} " +
                $"loopback={info.IsLoopback} default={info.IsDefault} frequency={info.MixFrequency} " +
                $"channels={info.MixChannels} name={info.Name} id={info.ID}");
        }
    }

    public static void PrintBassDevices()
    {
        for (var index = 0; Bass.GetDeviceInfo(index, out var info); index++)
            Console.WriteLine($"BMS_BASS_DEVICE index={index} enabled={info.IsEnabled} default={info.IsDefault} name={info.Name} driver={info.Driver}");
    }
}
