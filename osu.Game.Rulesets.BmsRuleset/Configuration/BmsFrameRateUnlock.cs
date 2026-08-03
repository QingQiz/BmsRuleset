using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Platform;

namespace osu.Game.Rulesets.BmsRuleset.Configuration;

internal static class BmsFrameRateUnlock
{
    private static readonly MethodInfo? update_frame_sync_mode = AccessTools.Method(typeof(GameHost), "updateFrameSyncMode");
    private static readonly Dictionary<GameHost, HostState> host_states = [];

    public static IDisposable Acquire(
        GameHost host,
        Bindable<ExecutionMode>? executionMode = null,
        Bindable<FrameSync>? frameSyncMode = null,
        Action? updateFrameSyncMode = null)
    {
        lock (host_states)
        {
            if (!host_states.TryGetValue(host, out var state))
            {
                state = new HostState(
                    host,
                    executionMode ?? new Bindable<ExecutionMode>(ExecutionMode.MultiThreaded),
                    frameSyncMode ?? new Bindable<FrameSync>(FrameSync.Unlimited),
                    updateFrameSyncMode);
                host_states.Add(host, state);

                state.SetUnlocked(true);
            }

            state.ReferenceCount++;
            return new Lease(host);
        }
    }

    private static void release(GameHost host)
    {
        lock (host_states)
        {
            if (!host_states.TryGetValue(host, out var state) || --state.ReferenceCount > 0)
                return;

            host_states.Remove(host);
            state.SetUnlocked(false);
        }
    }

    private sealed class HostState
    {
        private const double framework_default_input_hz = 1000;

        private readonly GameHost host;
        private readonly Bindable<ExecutionMode> executionMode;
        private readonly Bindable<FrameSync> frameSyncMode;
        private readonly Action updateFrameSyncMode;
        private readonly bool wasUnlocked;
        private bool active;

        public int ReferenceCount { get; set; }

        public HostState(GameHost host, Bindable<ExecutionMode> executionMode, Bindable<FrameSync> frameSyncMode, Action? updateFrameSyncMode)
        {
            this.host = host;
            this.executionMode = executionMode;
            this.frameSyncMode = frameSyncMode;
            this.updateFrameSyncMode = updateFrameSyncMode ?? (() => update_frame_sync_mode?.Invoke(host, null));
            wasUnlocked = host.AllowBenchmarkUnlimitedFrames;

            executionMode.BindValueChanged(onExecutionModeChanged);
            frameSyncMode.BindValueChanged(onFrameSyncModeChanged);
        }

        public void SetUnlocked(bool unlocked)
        {
            active = unlocked;
            host.AllowBenchmarkUnlimitedFrames = unlocked || wasUnlocked;

            if (!unlocked)
            {
                executionMode.ValueChanged -= onExecutionModeChanged;
                frameSyncMode.ValueChanged -= onFrameSyncModeChanged;
            }

            try
            {
                // Changing the opt-out flag does not make framework recalculate its active thread limits.
                updateFrameSyncMode();
            }
            catch (Exception exception)
            {
                BmsLogger.Error(exception, "Failed to update the frame limiter after changing the BMS frame rate unlock setting.");
            }

            if (unlocked)
                applyInitialInputRate();
            else
                restoreInputRate();
        }

        private void applyInitialInputRate()
        {
            if (host.InputThread == null)
                return;

            if (executionMode.Value == ExecutionMode.MultiThreaded)
                host.InputThread.ActiveHz = 0;
        }

        private void onExecutionModeChanged(ValueChangedEvent<ExecutionMode> mode)
        {
            if (!active || host.InputThread == null)
                return;

            if (mode.NewValue == ExecutionMode.MultiThreaded)
                host.InputThread.ActiveHz = 0;
            else
                host.InputThread.ActiveHz = host.MaximumUpdateHz;

            // ThreadRunner reapplies its own main-thread rate while changing modes, so run again afterwards.
            host.InputThread.Scheduler.Add(applyInputRateAfterFrameworkChange);
        }

        private void onFrameSyncModeChanged(ValueChangedEvent<FrameSync> _)
        {
            if (!active || host.InputThread == null)
                return;

            // GameHost reapplies the framework input cap while recalculating frame limits.
            host.InputThread.Scheduler.Add(applyInputRateAfterFrameworkChange);
        }

        private void applyInputRateAfterFrameworkChange()
        {
            if (!active || host.InputThread == null)
                return;

            if (executionMode.Value == ExecutionMode.MultiThreaded)
                host.InputThread.ActiveHz = 0;
            else
                host.InputThread.ActiveHz = host.MaximumUpdateHz;
        }

        private void restoreInputRate()
        {
            // ReSharper disable once UseNullPropagation
            if (host.InputThread == null)
                return;

            host.InputThread.ActiveHz = executionMode.Value == ExecutionMode.MultiThreaded
                ? framework_default_input_hz
                : host.MaximumUpdateHz;
        }
    }

    private sealed class Lease(GameHost host) : IDisposable
    {
        private GameHost? host = host;

        public void Dispose()
        {
            var currentHost = host;
            host = null;

            if (currentHost != null)
                release(currentHost);
        }
    }
}
