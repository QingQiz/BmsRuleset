#nullable enable
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Development;
using osu.Game.Rulesets.BmsRuleset.IO.ResourceStore;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal;

public partial class BmsPreviewTrackTest
{
    private PreviewPreparationProbe? preparationProbe;

    [TearDown]
    public void ReleasePreparationProbe()
    {
        preparationProbe?.Dispose();
        preparationProbe = null;
    }

    [Test]
    public void TestSwitchAwayDuringSampleResolutionDoesNotBlockAudio()
    {
        BmsPreviewTrack previous = null!;
        BmsPreviewTrack next = null!;
        Task stop = null!;
        string directory = null!;
        AddStep("start preview with slow sample resolution", () =>
        {
            directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-prepare-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            writePcmWave(Path.Combine(directory, "blocked.wav"), TimeSpan.FromSeconds(1));
            writePcmWave(Path.Combine(directory, "next.wav"), TimeSpan.FromSeconds(1));
            preparationProbe = new PreviewPreparationProbe();
            previous = new BmsEventPreviewTrack(_ => BmsEventPreviewTimeline.CreateSingleFile("blocked.wav"), directory, audio);
            audio.AddItem(previous);
            _ = previous.StartAsync();
        });
        AddUntilStep("sample resolution started", () => preparationProbe!.Entered.IsSet);
        AddAssert("resolution runs outside audio and update threads", () => !preparationProbe!.OnAudioThread && !preparationProbe.OnUpdateThread);
        AddStep("switch to another chart before resolution finishes", () =>
        {
            stop = previous.StopAsync();
            previous.Dispose();
            next = new BmsEventPreviewTrack(_ => BmsEventPreviewTimeline.CreateSingleFile("next.wav"), directory, audio);
            audio.AddItem(next);
            _ = next.StartAsync();
        });
        AddUntilStep("old track stops and disposes", () => stop.IsCompletedSuccessfully && previous.IsDisposed);
        AddUntilStep("new preview plays while old preparation is blocked", () => next.IsRunning && getActivePlaybackCount(next) > 0);
        AddAssert("old preparation has not been released", () => !preparationProbe!.Release.IsSet);
        AddStep("finish obsolete resource lookup", () => preparationProbe!.Release.Set());
        AddUntilStep("old constructor cleans up its session", () => preparationProbe!.Disposed.Task.IsCompleted);
        AddAssert("old track remains stopped", () => !previous.IsRunning);
        AddStep("dispose new preview", () => next.Dispose());
    }

    private sealed class PreviewPreparationProbe : IDisposable
    {
        private static PreviewPreparationProbe? current;
        private readonly ScopedMethodProbe resolutionPatch;
        private readonly ScopedMethodProbe cleanupPatch;
        internal readonly ManualResetEventSlim Entered = new();
        internal readonly ManualResetEventSlim Release = new();
        internal readonly TaskCompletionSource Disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool OnAudioThread;
        internal bool OnUpdateThread;

        internal PreviewPreparationProbe()
        {
            current = this;
            resolutionPatch = new ScopedMethodProbe(typeof(BmsAudioResourceStore).GetMethod("ResolveSamplePath", BindingFlags.Instance | BindingFlags.NonPublic)!,
                prefix: typeof(PreviewPreparationProbe).GetMethod(nameof(beforeResolution), BindingFlags.Static | BindingFlags.NonPublic));
            cleanupPatch = new ScopedMethodProbe(typeof(BmsEventPreviewPlayback).GetMethod(nameof(BmsEventPreviewPlayback.Dispose))!,
                postfix: typeof(PreviewPreparationProbe).GetMethod(nameof(afterDisposal), BindingFlags.Static | BindingFlags.NonPublic));
        }

        private static void beforeResolution(string definition)
        {
            var probe = current!;
            if (definition != "blocked.wav")
                return;
            probe.OnAudioThread = ThreadSafety.IsAudioThread;
            probe.OnUpdateThread = ThreadSafety.IsUpdateThread;
            probe.Entered.Set();
            probe.Release.Wait(TimeSpan.FromSeconds(15));
        }

        private static void afterDisposal() => current?.Disposed.TrySetResult();

        public void Dispose()
        {
            Release.Set();
            cleanupPatch.Dispose();
            resolutionPatch.Dispose();
            current = null;
        }
    }
}
