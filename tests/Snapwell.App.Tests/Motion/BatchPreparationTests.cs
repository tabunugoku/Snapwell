using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Snapwell.App.Motion;
using Snapwell.Core.Geometry;
using Snapwell.Core.Physics;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Motion;

/// <summary>実際の枠補正と復元を通して、準備中に無効になったバッチ対象を検証する。</summary>
public sealed class BatchPreparationTests
{
    /// <summary>準備前と準備済みの対象を取り消し、差し替えた要求も古い配置から守る。</summary>
    [Theory]
    [InlineData(false, 1, 2)]
    [InlineData(true, 1, 2)]
    [InlineData(false, 2, 1)]
    [InlineData(true, 2, 1)]
    public async Task CancelOrRetargetDuringRestoreExcludesStalePlacement(bool retarget, int blocked, int changed)
    {
        using var frames = new ControlledFrames();
        using var api = new BlockingPositionApi(blocked);
        var inspector = new Inspector(api);
        using var engine = new MotionEngine(frames, frames, new WindowMover(inspector, api), new FakePreference());
        engine.Request(Request(1));
        engine.Request(Request(2));
        frames.Advance();
        try
        {
            Assert.True(api.Entered.Wait(5000));
            await Task.Run(() =>
            {
                if (retarget)
                {
                    engine.Request(Request(changed) with { Target = new(500, 300, 600, 400) });
                }
                else
                {
                    engine.Cancel(changed);
                }
            }).WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            api.Release.Set();
        }

        WaitUntil(() => frames.WaitCalls == 2);
        Assert.Equal((nint)blocked, Assert.Single(Assert.Single(api.Batches)).Handle);
        Assert.Equal(retarget, engine.IsMoving(changed));
        if (blocked == 1)
        {
            Assert.DoesNotContain((nint)changed, inspector.Inspections);
            Assert.DoesNotContain((nint)changed, api.Restores);
        }

        Assert.Empty(api.SinglePlacements);
    }

    /// <summary>復元中でも終了は待たされず、復帰後に後続の準備やネイティブ適用を始めない。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DisposeDuringRestorePreventsFurtherPreparationAndNativeBatch(int blocked)
    {
        using var frames = new ControlledFrames();
        using var api = new BlockingPositionApi(blocked);
        var inspector = new Inspector(api);
        using var engine = new MotionEngine(frames, frames, new WindowMover(inspector, api), new FakePreference());
        engine.Request(Request(1));
        engine.Request(Request(2));
        frames.Advance();
        try
        {
            Assert.True(api.Entered.Wait(5000));
            await Task.Run(engine.Dispose).WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            api.Release.Set();
        }

        Assert.True(api.Worker!.Join(TimeSpan.FromSeconds(5)));
        Assert.Empty(api.Batches);
        Assert.Empty(api.SinglePlacements);
        if (blocked == 1)
        {
            Assert.DoesNotContain((nint)2, inspector.Inspections);
            Assert.DoesNotContain((nint)2, api.Restores);
        }
    }

    private static MotionRequest Request(nint handle) => new(handle, new(100, 100, 400, 300),
        new(8, 8, 600, 400), new(0, 0, 1920, 1040), FeelPreset.Airy, new(-1000, -200), SizeMode.Land);

    private static void WaitUntil(Func<bool> condition) => Assert.True(SpinWait.SpinUntil(condition, 5000));

    private sealed class Inspector(BlockingPositionApi api) : IWindowInspector
    {
        internal ConcurrentQueue<nint> Inspections { get; } = new();

        public bool TryInspect(nint handle, [NotNullWhen(true)] out WindowSnapshot? snapshot)
        {
            Inspections.Enqueue(handle);
            snapshot = new(handle, "Example", "Test", 42, 0x00040000, 0,
                new(100, 100, 400, 300), new(100, 100, 400, 300), default,
                false, true, false, !api.Restores.Contains(handle));
            return true;
        }

        public nint GetForegroundWindow() => 1;
        public nint GetRootWindowAt(int x, int y) => 1;
    }

    private sealed class BlockingPositionApi(nint blocked) : IWindowPositionApi, IDisposable
    {
        internal ManualResetEventSlim Entered { get; } = new();
        internal ManualResetEventSlim Release { get; } = new();
        internal ConcurrentQueue<nint> Restores { get; } = new();
        internal ConcurrentQueue<NativeWindowPlacement[]> Batches { get; } = new();
        internal ConcurrentQueue<nint> SinglePlacements { get; } = new();
        internal Thread? Worker { get; private set; }

        public bool Restore(nint handle)
        {
            Restores.Enqueue(handle);
            if (handle == blocked)
            {
                Worker = Thread.CurrentThread;
                Entered.Set();
                Release.Wait();
            }

            return true;
        }

        /// <summary>既存の同期経路では非同期送信を許さない。</summary>
        public bool SetBoundsAsync(nint handle, PixelRect bounds) => throw new NotSupportedException();

        public bool SetBounds(nint handle, PixelRect bounds, bool positionOnly)
        {
            SinglePlacements.Enqueue(handle);
            return true;
        }

        public bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements, bool positionOnly)
        {
            Batches.Enqueue([.. placements]);
            return true;
        }

        public void Dispose()
        {
            Entered.Dispose();
            Release.Dispose();
        }
    }
}
