using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Snapwell.App.Motion;
using Snapwell.Core.Geometry;
using Snapwell.Platform.Monitors;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Motion;

internal sealed class FakeMotion : IMotionEngine
{
    internal List<MotionRequest> Requests { get; } = [];
    internal HashSet<nint> Moving { get; } = [];
    internal List<nint> Canceled { get; } = [];
    internal int CancelAllCalls;
    internal int DisposeCalls;
    public void Request(MotionRequest request)
    {
        Requests.Add(request);
        lock (Moving)
        {
            Moving.Add(request.Handle);
        }
    }

    public bool IsMoving(nint handle)
    {
        lock (Moving)
        {
            return Moving.Contains(handle);
        }
    }

    /// <summary>着地が終わったとみなす。ドラッグのタイマーが別スレッドから読むので、ロックして外す。</summary>
    internal void FinishMoving(nint handle)
    {
        lock (Moving)
        {
            Moving.Remove(handle);
        }
    }

    public void Cancel(nint handle)
    {
        Canceled.Add(handle);
        Moving.Remove(handle);
    }

    public void CancelAll()
    {
        CancelAllCalls++;
        Moving.Clear();
    }

    public void Dispose() => DisposeCalls++;
}

internal sealed class FakeInspector : IWindowInspector
{
    internal WindowSnapshot Window { get; set; } = new(1, "Editor", "", 123, 0x00040000, 0,
        new(100, 100, 400, 300), new(100, 100, 400, 300), default, false, true, false, false);
    internal nint Foreground = 1;
    internal bool Exists = true;
    public bool TryInspect(nint handle, [NotNullWhen(true)] out WindowSnapshot? snapshot)
    {
        snapshot = Exists && handle == Window.Handle ? Window : null;
        return snapshot is not null;
    }

    public nint GetForegroundWindow() => Foreground;
    public nint GetRootWindowAt(int x, int y) => Window.Handle;
}

internal sealed class FakeMonitors : IMonitorSource
{
    public IReadOnlyList<MonitorInfo> GetMonitors() =>
        [new("main", new(0, 0, 1920, 1040), new(0, 0, 1920, 1040), 96, 96, true)];
}

internal sealed class FakeMover : IWindowMover
{
    internal ConcurrentQueue<(nint Handle, int X, int Y)> Moves { get; } = new();
    internal ConcurrentQueue<(nint Handle, PixelRect Bounds)> Bounds { get; } = new();
    internal ConcurrentQueue<(nint Handle, PixelRect Bounds)> AsyncBounds { get; } = new();
    internal ConcurrentQueue<(WindowPlacement[] Placements, bool PositionOnly)> Batches { get; } = new();
    internal ConcurrentQueue<nint> Reads { get; } = new();
    internal ConcurrentQueue<string> Calls { get; } = new();
    private readonly ConcurrentDictionary<nint, PixelRect> _visualBounds = new();
    internal bool BatchSuccess = true;
    internal bool ThrowBatch;
    internal IReadOnlyList<nint> FailedBatchHandles = [];
    internal Action? OnBatch;
    internal Func<nint, PixelRect?>? GetBoundsResult;
    internal nint FailingHandle;
    internal nint ThrowingHandle;
    internal ManualResetEventSlim? Block;
    internal ManualResetEventSlim Entered { get; } = new(false);
    internal Func<PixelRect, PixelRect?>? SetBoundsResult;

    /// <summary>反映は自動で行わず、送信した矩形だけを記録する。</summary>
    public bool RequestVisualBounds(nint handle, PixelRect visualBounds)
    {
        Calls.Enqueue($"async:{handle}");
        AsyncBounds.Enqueue((handle, visualBounds));
        if (handle == ThrowingHandle)
        {
            throw new InvalidOperationException("closed");
        }

        return handle != FailingHandle;
    }
    public bool MoveVisual(nint handle, int x, int y)
    {
        Calls.Enqueue($"move:{handle}");
        Moves.Enqueue((handle, x, y));
        Entered.Set();
        Block?.Wait();
        if (handle == ThrowingHandle)
        {
            throw new InvalidOperationException("closed");
        }

        return handle != FailingHandle;
    }

    public PixelRect? SetVisualBounds(nint handle, PixelRect visualBounds)
    {
        Calls.Enqueue($"bounds:{handle}");
        Bounds.Enqueue((handle, visualBounds));
        return SetBoundsResult is null ? visualBounds : SetBoundsResult(visualBounds);
    }

    public PixelRect? GetVisualBounds(nint handle)
    {
        Reads.Enqueue(handle);
        return GetBoundsResult is null ? _visualBounds[handle] : GetBoundsResult(handle);
    }

    public bool ApplyBatch(IReadOnlyList<WindowPlacement> placements, bool positionOnly,
        out IReadOnlyList<nint> failedHandles, Func<nint, bool>? isCurrent = null)
    {
        Calls.Enqueue("batch");
        Batches.Enqueue(([.. placements], positionOnly));
        OnBatch?.Invoke();
        if (ThrowBatch)
        {
            throw new InvalidOperationException("batch failed");
        }

        failedHandles = FailedBatchHandles;
        foreach (var placement in placements)
        {
            _visualBounds[placement.Handle] = placement.VisualBounds;
        }

        return BatchSuccess;
    }
}

internal sealed class FakePreference(bool reduceMotion = false) : IMotionPreference
{
    public bool ReduceMotion => reduceMotion;
}

internal sealed class ControlledFrames : IClock, IFramePacer, IDisposable
{
    private readonly SemaphoreSlim _frames = new(0);
    private long _ticks;
    private int _waitCalls;
    internal int WaitCalls => Volatile.Read(ref _waitCalls);
    public double Seconds => Interlocked.Read(ref _ticks) / 60.0;
    internal void Advance(int count = 1) => _frames.Release(count);
    public void Wait(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _waitCalls);
        _frames.Wait(cancellationToken);
        Interlocked.Increment(ref _ticks);
    }

    public void Dispose() => _frames.Dispose();
}
