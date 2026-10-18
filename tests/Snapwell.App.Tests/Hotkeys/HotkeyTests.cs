using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Snapwell.App.Hotkeys;
using Snapwell.App.Motion;
using Snapwell.App.Tests.Motion;
using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Hotkeys;

public sealed class HotkeyTests
{
    /// <summary>開くキーが矢印に割り当てられていても、表示中の選択操作を優先する。</summary>
    [Fact]
    public void LauncherInputKeepsPriorityOverReopenBinding()
    {
        var matcher = new HotkeyMatcher();
        matcher.SetBindings(HotkeyBindingsBuilder.Launcher(
            [new((int)WindowCommand.OpenLauncher, KeyModifiers.None, 0x25)]));
        Assert.Equal((int)LauncherInput.Left, matcher.ProcessKey(0x25, true).Id);
    }

    /// <summary>ランチャーは配置コマンドとして実行せず、専用の呼び出しへ渡す。</summary>
    [Fact]
    public void OpenLauncherInvokesCallbackWithoutMovingWindow()
    {
        var calls = 0;
        var motion = new FakeMotion();
        var inspector = new FakeInspector();
        var executor = new HotkeyCommandExecutor(inspector, new FakeMonitors(), motion, new FakePosition(inspector),
            () => new(), () => true, () => { }, 999, () => calls++);
        executor.Handle(new((int)WindowCommand.OpenLauncher));
        Assert.Equal(1, calls);
        Assert.Empty(motion.Requests);
    }

    /// <summary>ランチャーの範囲も固定サイズを保ち、位置だけを変更する。</summary>
    [Fact]
    public void SelectedRangePreservesFixedWindowSize()
    {
        var fixture = new Fixture();
        fixture.Inspector.Window = fixture.Inspector.Window with { Style = 0 };
        fixture.Executor.Place(1, new(100, 100, 800, 600), new(0, 0, 1920, 1040));
        var request = Assert.Single(fixture.Motion.Requests);
        Assert.Equal(new PixelRect(300, 250, 400, 300), request.Target);
        Assert.False(request.Resize);
    }

    /// <summary>ランチャーの対象は最大化を解除してから、選んだ矩形へ移動する。</summary>
    [Fact]
    public void SelectedRangeUsesSharedMaximizedRestoration()
    {
        using var fixture = new BlockingRestoreFixture();
        var target = new PixelRect(8, 8, 948, 512);
        fixture.Executor.Place(1, target, new(0, 0, 1920, 1040));
        fixture.CompleteRestore();
        Assert.Equal(target, Assert.Single(fixture.Requests).Target);
        Assert.Equal(1, fixture.RestoreCalls);
    }

    [Fact]
    public void DefaultsUseWindowsModifierOnlyForAdjacentCommandsAndSuppressChords()
    {
        var result = HotkeyBindingsBuilder.Build(new PortableSettings().Hotkeys);
        Assert.Empty(result.Dropped);
        Assert.All(result.Bindings, binding =>
        {
            var adjacent = (WindowCommand)binding.Id is WindowCommand.SnapAdjacentLeft or
                WindowCommand.SnapAdjacentRight or WindowCommand.SnapAdjacentUp or WindowCommand.SnapAdjacentDown;
            Assert.Equal(adjacent, binding.Modifiers.HasFlag(KeyModifiers.Win));
            Assert.True(binding.Suppress);
        });
    }

    /// <summary>隣の一覧は前面のハンドルで取得し、各方向で一度だけ位置の移動を要求する。</summary>
    [Theory]
    [InlineData(WindowCommand.SnapAdjacentLeft, -100, 100, 150, 300, 58, 100)]
    [InlineData(WindowCommand.SnapAdjacentRight, 700, 100, 100, 300, 292, 100)]
    [InlineData(WindowCommand.SnapAdjacentUp, 100, -100, 400, 150, 100, 58)]
    [InlineData(WindowCommand.SnapAdjacentDown, 100, 600, 400, 100, 100, 292)]
    public void AdjacentCommandRequestsExpectedPositionOnce(WindowCommand command,
        int x, int y, int width, int height, int expectedX, int expectedY)
    {
        var calls = 0;
        var fixture = new Fixture(handle =>
        {
            Assert.Equal((nint)1, handle);
            calls++;
            return [new(x, y, width, height)];
        });
        fixture.Executor.Handle(new((int)command));
        var request = Assert.Single(fixture.Motion.Requests);
        Assert.Equal(1, calls);
        Assert.Equal(new PixelRect(expectedX, expectedY, 400, 300), request.Target);
        Assert.Equal(fixture.Inspector.Window.VisualBounds, request.Current);
        Assert.Equal(new PixelRect(0, 0, 1920, 1040), request.Walls);
        Assert.False(request.Resize);
    }

    /// <summary>隣の取得関数が省略されても各方向の作業領域端へ動く。</summary>
    [Theory]
    [InlineData(WindowCommand.SnapAdjacentLeft, 8, 100)]
    [InlineData(WindowCommand.SnapAdjacentRight, 1512, 100)]
    [InlineData(WindowCommand.SnapAdjacentUp, 100, 8)]
    [InlineData(WindowCommand.SnapAdjacentDown, 100, 732)]
    public void AdjacentCommandWithoutNeighborSourceUsesInsetEdge(WindowCommand command, int x, int y)
    {
        var fixture = new Fixture();
        fixture.Executor.Handle(new((int)command));
        var request = Assert.Single(fixture.Motion.Requests);
        Assert.Equal(new PixelRect(x, y, 400, 300), request.Target);
        Assert.False(request.Resize);
    }

    /// <summary>サイズ固定の前面ウィンドウも隣へ位置だけ動かす。</summary>
    [Fact]
    public void AdjacentCommandAllowsFixedSizeWindow()
    {
        var fixture = new Fixture(_ => [new(700, 100, 100, 300)]);
        fixture.Inspector.Window = fixture.Inspector.Window with { Style = 0 };
        fixture.Executor.Handle(new((int)WindowCommand.SnapAdjacentRight));
        var request = Assert.Single(fixture.Motion.Requests);
        Assert.Equal(new PixelRect(292, 100, 400, 300), request.Target);
        Assert.False(request.Resize);
    }

    /// <summary>一時停止中は隣を取得せず、移動も要求しない。</summary>
    [Fact]
    public void DisabledAdjacentCommandDoesNotReadNeighborsOrRequestMotion()
    {
        var fixture = new Fixture(_ => throw new InvalidOperationException()) { Enabled = false };
        fixture.Executor.Handle(new((int)WindowCommand.SnapAdjacentRight));
        Assert.Empty(fixture.Motion.Requests);
    }

    /// <summary>端に揃っている場合は移動を要求しない。</summary>
    [Fact]
    public void AdjacentCommandAtInsetEdgeDoesNotRequestMotion()
    {
        var fixture = new Fixture();
        fixture.Inspector.Window = fixture.Inspector.Window with { VisualBounds = new(1512, 100, 400, 300) };
        fixture.Executor.Handle(new((int)WindowCommand.SnapAdjacentRight));
        Assert.Empty(fixture.Motion.Requests);
    }

    /// <summary>最大化を解除して取り直した通常の寸法から、隣への移動を計画する。</summary>
    [Fact]
    public void AdjacentCommandUsesSharedMaximizedRestoration()
    {
        using var fixture = new BlockingRestoreFixture(_ => [new(700, 100, 100, 300)]);
        fixture.Executor.Handle(new((int)WindowCommand.SnapAdjacentRight));
        Assert.Empty(fixture.Requests);
        fixture.CompleteRestore();
        var request = Assert.Single(fixture.Requests);
        Assert.Equal(new PixelRect(100, 100, 400, 300), request.Current);
        Assert.Equal(new PixelRect(292, 100, 400, 300), request.Target);
        Assert.False(request.Resize);
        Assert.Equal(1, fixture.RestoreCalls);
    }

    [Fact]
    public void DuplicateChordKeepsFirstAndReportsLaterCommand()
    {
        var chord = new KeyChordSpec(ChordModifiers.Control, 0x25);
        var result = HotkeyBindingsBuilder.Build(new Dictionary<WindowCommand, KeyChordSpec>
        {
            [WindowCommand.LeftHalf] = chord,
            [WindowCommand.RightHalf] = chord,
        });
        Assert.Equal((int)WindowCommand.LeftHalf, Assert.Single(result.Bindings).Id);
        Assert.Equal(WindowCommand.RightHalf, Assert.Single(result.Dropped));
    }

    [Fact]
    public void LeftHalfUsesPlannerBoundsAndZeroVelocity()
    {
        var fixture = new Fixture();
        fixture.Executor.Handle(new((int)WindowCommand.LeftHalf));
        var request = Assert.Single(fixture.Motion.Requests);
        Assert.Equal(new PixelRect(8, 8, 948, 1024), request.Target);
        Assert.Equal(default, request.Velocity);
        Assert.True(request.Resize);
        Assert.Equal((nint)1, request.Handle);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExcludedAndAbsentForegroundDoNothing(bool excluded)
    {
        var fixture = new Fixture();
        fixture.Inspector.Window = fixture.Inspector.Window with { IsCloaked = excluded };
        fixture.Inspector.Foreground = excluded ? 1 : 0;
        fixture.Executor.Handle(new((int)WindowCommand.LeftHalf));
        Assert.Empty(fixture.Motion.Requests);
    }

    [Fact]
    public void DisabledStateProcessesOnlyToggle()
    {
        var fixture = new Fixture { Enabled = false };
        fixture.Executor.Handle(new((int)WindowCommand.LeftHalf));
        fixture.Executor.Handle(new((int)WindowCommand.ToggleEnabled));
        Assert.Empty(fixture.Motion.Requests);
        Assert.Equal(1, fixture.Toggles);
    }

    [Fact]
    public void OwnProcessWindowIsExcluded()
    {
        var fixture = new Fixture();
        fixture.Inspector.Window = fixture.Inspector.Window with { ProcessId = 999 };
        fixture.Executor.Handle(new((int)WindowCommand.LeftHalf));
        Assert.Empty(fixture.Motion.Requests);
    }

    [Fact]
    public void MaximizedWindowUsesReinspectedNormalBounds()
    {
        using var fixture = new BlockingRestoreFixture();
        fixture.Executor.Handle(new((int)WindowCommand.LeftHalf));
        fixture.CompleteRestore();
        Assert.Equal(1, fixture.RestoreCalls);
        Assert.Equal(new PixelRect(100, 100, 400, 300), Assert.Single(fixture.Requests).Current);
    }

    [Fact]
    public async Task BlockedRestoreReturnsPromptlyAndAllowsOtherWindowsAndToggleWhileDroppingDuplicates()
    {
        using var fixture = new BlockingRestoreFixture();
        await Task.Run(() => fixture.Executor.Handle(new((int)WindowCommand.LeftHalf)))
            .WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(fixture.Entered.Wait(5000));
        Assert.True(fixture.RestoreThread!.IsBackground);
        Assert.StartsWith("Snapwell restore ", fixture.RestoreThread.Name);
        fixture.Executor.Handle(new((int)WindowCommand.RightHalf));
        fixture.Executor.Handle(new((int)WindowCommand.ToggleEnabled));
        Assert.Equal(1, fixture.Toggles);
        fixture.Foreground = 2;
        fixture.Executor.Handle(new((int)WindowCommand.LeftHalf));
        Assert.Equal((nint)2, Assert.Single(fixture.Requests).Handle);
        fixture.CompleteRestore();
        Assert.Equal(1, fixture.RestoreCalls);
        Assert.Single(fixture.Requests, request => request.Handle == 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StopOrDisableWhileRestoringPreventsLateRequest(bool stop)
    {
        using var fixture = new BlockingRestoreFixture();
        fixture.Executor.Handle(new((int)WindowCommand.LeftHalf));
        Assert.True(fixture.Entered.Wait(5000));
        await Task.Run(() =>
        {
            if (stop)
            {
                fixture.Executor.Stop();
            }
            else
            {
                Volatile.Write(ref fixture.Enabled, false);
            }
        }).WaitAsync(TimeSpan.FromSeconds(1));
        fixture.CompleteRestore();
        Assert.Empty(fixture.Requests);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("maximized")]
    [InlineData("excluded")]
    [InlineData("failed")]
    [InlineData("throwing")]
    [InlineData("settings")]
    public void FailedRestorationOrReinspectionDoesNotRequestAndWorkerExceptionsStayContained(string failure)
    {
        using var fixture = new BlockingRestoreFixture { Failure = failure };
        fixture.Executor.Handle(new((int)WindowCommand.LeftHalf));
        fixture.CompleteRestore();
        Assert.Empty(fixture.Requests);
        fixture.Failure = "";
        fixture.Foreground = 2;
        fixture.Executor.Handle(new((int)WindowCommand.LeftHalf));
        Assert.Equal((nint)2, Assert.Single(fixture.Requests).Handle);
    }

    [Fact]
    public void CompletedRestoreReleasesWindowForSubsequentCommands()
    {
        using var fixture = new BlockingRestoreFixture();
        fixture.Executor.Handle(new((int)WindowCommand.LeftHalf));
        fixture.CompleteRestore();
        fixture.Executor.Handle(new((int)WindowCommand.RightHalf));
        Assert.Equal(2, fixture.Requests.Count);
        Assert.Equal(1, fixture.RestoreCalls);
    }

    private sealed class BlockingRestoreFixture : IWindowInspector, IWindowPositionApi, IMotionEngine, IDisposable
    {
        private readonly ManualResetEventSlim _continue = new(false);
        private readonly WindowSnapshot _normal = new FakeInspector().Window;
        private int _restored;
        internal ManualResetEventSlim Entered { get; } = new(false);
        internal ConcurrentQueue<MotionRequest> Requests { get; } = new();
        internal HotkeyCommandExecutor Executor { get; }
        internal Thread? RestoreThread;
        internal nint Foreground = 1;
        internal bool Enabled = true;
        internal int Toggles;
        internal int RestoreCalls;
        internal string Failure = "";

        internal BlockingRestoreFixture(Func<nint, IReadOnlyList<PixelRect>>? neighbors = null) =>
            Executor = new(this, new FakeMonitors(), this, this,
                () => Failure == "settings" ? throw new InvalidOperationException() : new(),
                () => Volatile.Read(ref Enabled), () => Toggles++, 999, neighbors: neighbors);

        public bool TryInspect(nint handle, [NotNullWhen(true)] out WindowSnapshot? snapshot)
        {
            var restored = Volatile.Read(ref _restored) != 0;
            snapshot = handle == 1 && restored && Failure == "missing" ? null : _normal with
            {
                Handle = handle,
                IsMaximized = handle == 1 && (!restored || Failure == "maximized"),
                IsCloaked = handle == 1 && restored && Failure == "excluded",
            };
            return snapshot is not null;
        }

        public nint GetForegroundWindow() => Foreground;
        public nint GetRootWindowAt(int x, int y) => Foreground;
        public bool Restore(nint handle)
        {
            Interlocked.Increment(ref RestoreCalls);
            RestoreThread = Thread.CurrentThread;
            Entered.Set();
            _continue.Wait();
            Interlocked.Exchange(ref _restored, 1);
            if (Failure == "throwing")
            {
                throw new InvalidOperationException();
            }

            return Failure != "failed";
        }

        internal void CompleteRestore()
        {
            Assert.True(Entered.Wait(5000));
            _continue.Set();
            Assert.True(RestoreThread!.Join(TimeSpan.FromSeconds(5)));
        }

        public void Request(MotionRequest request) => Requests.Enqueue(request);
        public bool IsMoving(nint handle) => false;
        public void Cancel(nint handle) { }
        public void CancelAll() { }
        public bool SetBounds(nint handle, PixelRect bounds, bool positionOnly) => throw new NotSupportedException();
        public bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements) => throw new NotSupportedException();
        public void Dispose()
        {
            Executor.Stop();
            _continue.Set();
            if (RestoreThread is { IsThreadPoolThread: false } thread)
            {
                thread.Join(TimeSpan.FromSeconds(5));
            }
        }
    }

    [Fact]
    public void RepeatedCommandsGoDirectlyToMotionForRetargeting()
    {
        var fixture = new Fixture();
        fixture.Executor.Handle(new((int)WindowCommand.LeftHalf));
        fixture.Executor.Handle(new((int)WindowCommand.RightHalf));
        Assert.Equal(2, fixture.Motion.Requests.Count);
        Assert.Empty(fixture.Motion.Canceled);
        Assert.Equal((nint)1, fixture.Motion.Requests[1].Handle);
    }

    private sealed class Fixture
    {
        internal FakeInspector Inspector { get; } = new();
        internal FakeMotion Motion { get; } = new();
        internal FakePosition Position { get; }
        internal bool Enabled = true;
        internal int Toggles;
        internal HotkeyCommandExecutor Executor { get; }

        internal Fixture(Func<nint, IReadOnlyList<PixelRect>>? neighbors = null)
        {
            Position = new(Inspector);
            Executor = new(Inspector, new FakeMonitors(), Motion, Position, () => new(),
                () => Enabled, () => Toggles++, 999, neighbors: neighbors);
        }
    }

    private sealed class FakePosition(FakeInspector inspector) : IWindowPositionApi
    {
        internal int RestoreCalls;
        public bool Restore(nint handle)
        {
            RestoreCalls++;
            inspector.Window = inspector.Window with { IsMaximized = false };
            return true;
        }

        public bool SetBounds(nint handle, PixelRect bounds, bool positionOnly) => throw new NotSupportedException();
        public bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements) => throw new NotSupportedException();
    }
}
