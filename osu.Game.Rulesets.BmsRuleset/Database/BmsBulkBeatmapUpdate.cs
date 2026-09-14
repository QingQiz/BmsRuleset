using System;
using System.Runtime.CompilerServices;
using System.Threading;
using osu.Game.Database;

namespace osu.Game.Rulesets.BmsRuleset.Database;

/// <summary>
/// Keeps the detached library at its last published state while a BMS operation commits its changes.
/// </summary>
internal static class BmsBulkBeatmapUpdate
{
    private static readonly ConditionalWeakTable<RealmAccess, State> states = new();

    internal static State For(RealmAccess realm) => states.GetOrCreateValue(realm);

    internal static IDisposable Begin(RealmAccess realm)
    {
        BmsBeatmapNotificationPatcher.InstallOnce();
        return For(realm).Begin();
    }

    internal sealed class State
    {
        private readonly object sync = new();
        private int active;
        private long generation;

        internal (bool Active, long Generation) Read()
        {
            lock (sync)
                return (active > 0, generation);
        }

        internal IDisposable Begin()
        {
            lock (sync)
            {
                active++;
                generation++;
            }

            return new Scope(this);
        }

        internal void RunWhenIdle(Action<long> action)
        {
            lock (sync)
            {
                if (active == 0)
                    action(generation);
            }
        }

        private sealed class Scope(State state) : IDisposable
        {
            private State? owner = state;

            public void Dispose()
            {
                var current = Interlocked.Exchange(ref owner, null);
                if (current == null)
                    return;

                lock (current.sync)
                {
                    current.active--;
                    // A fast operation can finish before its first Realm callback reaches the update thread.
                    current.generation++;
                }
            }
        }
    }
}
