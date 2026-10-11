#nullable enable

using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

internal static class BmsThreadCpuCounter
{
    internal readonly record struct Snapshot(int ManagedThreadId, ulong Ticks);
    internal readonly record struct CycleSnapshot(int ManagedThreadId, ulong Cycles);

    internal static CapturedThread? CaptureCurrentThread()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var handle = OpenThread(0x0800, false, GetCurrentThreadId());
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        return new CapturedThread(handle, Environment.CurrentManagedThreadId);
    }

    internal sealed class CapturedThread(SafeWaitHandle handle, int managedThreadId) : IDisposable
    {
        internal Snapshot? Read()
        {
            if (handle.IsClosed || !GetCapturedThreadTimes(handle, out _, out _, out var kernel, out var user))
                return null;

            return new Snapshot(managedThreadId, kernel + user);
        }

        internal CycleSnapshot? ReadCycles()
        {
            if (handle.IsClosed || !QueryCapturedThreadCycleTime(handle, out var cycles))
                return null;

            return new CycleSnapshot(managedThreadId, cycles);
        }

        public void Dispose() => handle.Dispose();
    }

    internal static CycleSnapshot? ReadCycles()
    {
        if (!OperatingSystem.IsWindows() || !QueryThreadCycleTime(GetCurrentThread(), out var cycles))
            return null;

        return new CycleSnapshot(Environment.CurrentManagedThreadId, cycles);
    }

    internal static ulong? ElapsedCycles(CycleSnapshot? start, CycleSnapshot? end)
    {
        if (start is not { } from || end is not { } to
            || from.ManagedThreadId != to.ManagedThreadId || to.Cycles < from.Cycles)
            return null;

        return to.Cycles - from.Cycles;
    }

    internal static Snapshot? Read()
    {
        if (!OperatingSystem.IsWindows()
            || !GetThreadTimes(GetCurrentThread(), out _, out _, out var kernel, out var user))
            return null;

        return new Snapshot(Environment.CurrentManagedThreadId, kernel + user);
    }

    internal static double? ElapsedMilliseconds(Snapshot? start, Snapshot? end)
    {
        if (start is not { } from || end is not { } to
            || from.ManagedThreadId != to.ManagedThreadId || to.Ticks < from.Ticks)
            return null;

        return (to.Ticks - from.Ticks) / (double)TimeSpan.TicksPerMillisecond;
    }

    // The pseudo-handle refers to the caller and does not need closing. Read only at
    // interval boundaries so the diagnostic does not add a syscall to each update.
    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentThread();

    // Retaining a queried thread handle lets the update thread read the draw thread at
    // the same interval boundaries, without waiting for a draw scheduler callback.
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll")]
    private static extern SafeWaitHandle OpenThread(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint threadId);

    [DllImport("kernel32.dll", EntryPoint = "GetThreadTimes")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCapturedThreadTimes(SafeWaitHandle thread, out ulong creation, out ulong exit, out ulong kernel, out ulong user);

    [DllImport("kernel32.dll", EntryPoint = "QueryThreadCycleTime")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryCapturedThreadCycleTime(SafeWaitHandle thread, out ulong cycles);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetThreadTimes(nint thread, out ulong creation, out ulong exit, out ulong kernel, out ulong user);

    // Cycle counts have finer resolution than GetThreadTimes for sub-millisecond phases.
    // They measure scheduled execution, but CPU frequency changes prevent conversion to time.
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryThreadCycleTime(nint thread, out ulong cycles);
}
