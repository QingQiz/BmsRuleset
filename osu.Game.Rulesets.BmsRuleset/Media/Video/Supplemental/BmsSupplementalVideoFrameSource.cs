using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.BmsRuleset.Media.Video.Supplemental;

internal sealed class BmsSupplementalVideoFrameSource(byte[] data, int maxQueuedFrames = 3) : IDisposable
{
    private const double decode_ahead_seconds = 0.10;
    private const double stale_before_target_seconds = 0.20;

    private readonly int maxQueuedFrames = Math.Max(1, maxQueuedFrames);
    private readonly ConcurrentQueue<BmsSupplementalVideoFrame> queuedFrames = new();
    private readonly AutoResetEvent wakeSignal = new(false);
    private readonly CancellationTokenSource cancellation = new();

    private Task? worker;
    private long targetTimeMilliseconds;
    private int decodedFrames;
    private int droppedFrames;
    private int uploadedFrames;
    private int disposed;
    private volatile bool isFaulted;
    private string? faultMessage;

    public BmsSupplementalVideoFrameSourceStats Stats => new(
        decodedFrames,
        droppedFrames,
        isFaulted,
        faultMessage);

    internal bool WorkerCompleted => worker?.IsCompleted ?? true;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        if (worker != null)
            return;

        worker = Task.Run(runWorker);
    }

    public void SetTargetTime(double seconds)
    {
        Interlocked.Exchange(ref targetTimeMilliseconds, (long)(Math.Max(0, seconds) * 1000));
        wakeSignal.Set();
    }

    public bool TryTakeLatestFrame(out BmsSupplementalVideoFrame? frame)
    {
        frame = null;
        var targetTime = readTargetTime();

        while (queuedFrames.TryPeek(out var next) && next.Time <= targetTime)
        {
            if (!queuedFrames.TryDequeue(out var candidate))
                continue;

            if (frame != null)
            {
                frame.Dispose();
                Interlocked.Increment(ref droppedFrames);
            }

            frame = candidate;
        }

        if (frame == null)
            return false;

        Interlocked.Increment(ref uploadedFrames);
        return true;
    }

    private void runWorker()
    {
        if (!BmsSupplementalVideoDecoder.TryCreate(data, out var decoder, out var error))
        {
            fault(error);
            return;
        }

        if (decoder == null)
            return;

        using (decoder)
        {
            double lastFrameTime = 0;

            while (!cancellation.IsCancellationRequested)
            {
                var desired = readTargetTime() + decode_ahead_seconds;

                if (lastFrameTime >= desired && queuedFrames.Count >= maxQueuedFrames)
                {
                    wakeSignal.WaitOne(8);
                    continue;
                }

                if (!decoder.TryDecodeNextFrame(out var decoded, out error))
                {
                    if (!string.IsNullOrWhiteSpace(error))
                        fault(error);

                    wakeSignal.WaitOne(8);
                    continue;
                }

                if (decoded == null)
                    continue;

                lastFrameTime = decoded.Time;
                Interlocked.Increment(ref decodedFrames);

                var currentTarget = readTargetTime();
                var hasDisplayableFrame = queuedFrames.Count > 0 || Volatile.Read(ref uploadedFrames) > 0;

                if (hasDisplayableFrame && decoded.Time < currentTarget - stale_before_target_seconds)
                {
                    decoded.Dispose();
                    Interlocked.Increment(ref droppedFrames);
                    continue;
                }

                queuedFrames.Enqueue(decoded);
            }
        }
    }

    private double readTargetTime() => Interlocked.Read(ref targetTimeMilliseconds) / 1000.0;

    private void fault(string? message)
    {
        faultMessage = message ?? "Supplemental video decoder failed.";
        isFaulted = true;
        Logger.Log($"[BGA] Supplemental video path faulted: {faultMessage}", "bms-bga");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        cancellation.Cancel();
        wakeSignal.Set();

        try
        {
            // The decoder reads from memory, so cancellation is observed after the in-flight frame.
            // Do not release its wait handle or source bytes while that frame is still being decoded.
            worker?.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed while stopping a supplemental BGA video worker.");
        }

        while (queuedFrames.TryDequeue(out var frame))
            frame.Dispose();

        wakeSignal.Dispose();
        cancellation.Dispose();
    }
}

internal readonly record struct BmsSupplementalVideoFrameSourceStats(
    int DecodedFrames,
    int DroppedFrames,
    bool IsFaulted,
    string? FaultMessage);
