using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.BmsRuleset.Difficulty;
using osu.Game.Rulesets.BmsRuleset.IO.Import;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.IO;

public partial class BmsFileImporterTest
{
    [Test, NonParallelizable]
    public void TestCancellationDuringPreparationStopsStarRatingAndJoinsWorkers()
    {
        runImportTest(async (realm, storage) =>
        {
            addBmsRuleset(realm);
            var root = CreateImportFixture(storage, 2, 4, 16);
            using var notification = new ProgressNotification();
            var cancellation = (CancellationTokenSource)typeof(ProgressNotification)
                .GetField("cancellationTokenSource", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notification)!;
            using var probe = new ComputationProbe(cancellation.Cancel);
            var importer = new BmsFileImporter(realm, storage);
            await Task.Run(() => typeof(BmsFileImporter).GetMethod("runImportPipeline", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(importer, [notification, new[] { root }])).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(probe.Started, Is.GreaterThan(0));
                Assert.That(probe.Completed, Is.Zero, "Cancellation must reach the computation already being started.");
                Assert.That(probe.Active, Is.Zero, "Import completion must not leave producer work running.");
                Assert.That(notification.State, Is.EqualTo(ProgressNotificationState.Cancelled));
                Assert.That(realm.Run(r => r.All<BeatmapSetInfo>().Count()), Is.Zero);
            });
        });
    }

    [TestCase(1), TestCase(2), NonParallelizable]
    public void TestImportBoundsConcurrentStarRatingWork(int requests)
    {
        runImportTest(async (realm, storage) =>
        {
            addBmsRuleset(realm);
            var root = CreateImportFixture(storage, 8, 4, 24);
            using var probe = new ComputationProbe();
            var directories = Directory.GetDirectories(root).Order().ToArray();
            await Task.WhenAll(Enumerable.Range(0, requests).Select(request =>
                new BmsFileImporter(realm, storage).Import(directories.Where((_, index) => index % requests == request).ToArray()))).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(probe.Completed, Is.EqualTo(32));
                Assert.That(probe.Peak, Is.LessThanOrEqualTo(4));
                Assert.That(probe.Active, Is.Zero);
                Assert.That(realm.Run(r => r.All<BeatmapInfo>().Count()), Is.EqualTo(32));
            });
        });
    }

    internal static void RunIsolatedImportTest(Func<RealmAccess, TemporaryNativeStorage, Task> action) => runImportTest(async (realm, storage) =>
    {
        addBmsRuleset(realm);
        await action(realm, storage).ConfigureAwait(false);
    });

    internal static string CreateImportFixture(TemporaryNativeStorage storage, int directories, int charts, int measures)
    {
        var root = storage.GetFullPath("generated-charts");
        for (var directory = 0; directory < directories; directory++)
        {
            var path = Path.Combine(root, directory.ToString());
            Directory.CreateDirectory(path);
            for (var chart = 0; chart < charts; chart++)
            {
                var text = new StringBuilder($"#TITLE Set {directory} [Chart {chart}]\n#BPM 150\n#RANK 2\n#PLAYER 1\n");
                for (var measure = 1; measure <= measures; measure++)
                {
                    foreach (var channel in new[] { "11", "12", "13", "14", "15", "18", "19", "16" })
                        text.Append('#').Append(measure.ToString("D3")).Append(channel).Append(":01010101\n");
                }

                File.WriteAllText(Path.Combine(path, $"{chart:D3}.bms"), text.ToString());
            }
        }

        return root;
    }

    internal sealed class ComputationProbe : IDisposable
    {
        private readonly ScopedMethodProbe patch;
        private readonly Action onFirst;
        private static ComputationProbe current;
        private int active;
        private int started;
        private int completed;
        private int peak;

        public int Active => Volatile.Read(ref active);

        public int Started => Volatile.Read(ref started);

        public int Completed => Volatile.Read(ref completed);

        public int Peak => Volatile.Read(ref peak);

        public ComputationProbe(Action onFirst = null)
        {
            this.onFirst = onFirst;
            current = this;
            patch = new ScopedMethodProbe(typeof(BmsStarRatingProcessor).GetMethod("compute", BindingFlags.NonPublic | BindingFlags.Instance)!,
                prefix: typeof(ComputationProbe).GetMethod(nameof(enter), BindingFlags.NonPublic | BindingFlags.Static),
                finalizer: typeof(ComputationProbe).GetMethod(nameof(leave), BindingFlags.NonPublic | BindingFlags.Static));
        }

        private static void enter()
        {
            var probe = current;
            var active = Interlocked.Increment(ref probe.active);
            int previous;
            do
            {
                previous = probe.Peak;
                if (previous >= active) break;
            } while (Interlocked.CompareExchange(ref probe.peak, active, previous) != previous);

            if (Interlocked.Increment(ref probe.started) == 1)
                probe.onFirst?.Invoke();
        }

        // ReSharper disable once InconsistentNaming
        private static void leave(Exception __exception)
        {
            var probe = current;
            if (__exception == null)
                Interlocked.Increment(ref probe.completed);
            Interlocked.Decrement(ref probe.active);
        }

        public void Dispose()
        {
            patch.Dispose();
            current = null;
        }
    }
}
