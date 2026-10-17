using System.Collections.Concurrent;
using Snapwell.App.Hotkeys;
using Snapwell.App.Overlay;
using Snapwell.App.Tests.Motion;
using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;

namespace Snapwell.App.Tests.Overlay;

/// <summary>描画失敗からの復帰を実際のウィンドウを作らずに検証する。</summary>
public sealed class OverlaySessionTests
{
    /// <summary>初回の作成失敗はキーを戻し、開き直した要求を新しい surface へ渡す。</summary>
    [Fact]
    public void FactoryFaultClosesLauncherAndNextOpenRecreatesThread()
    {
        using var fail = new ManualResetEventSlim(false);
        var recovered = new FakeOverlay();
        var attempts = 0;
        using var session = new OverlaySession(true, _ =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            return new(() =>
            {
                if (attempt == 1)
                {
                    fail.Wait();
                    throw new InvalidOperationException();
                }

                return (recovered, recovered);
            });
        });
        var normal = HotkeyBindingsBuilder.Build(new PortableSettings().Hotkeys).Bindings;
        IReadOnlyList<HotkeyBinding> applied = [];
        var placements = 0;
        using var launcher = new LauncherController(new FakeInspector(), new FakeMonitors(), () => new(),
            session, (_, _, _) => placements++, value => applied = value, normal, 999);
        try
        {
            launcher.Open();
            Assert.True(launcher.IsOpen);
            fail.Set();
            Assert.True(SpinWait.SpinUntil(() => !launcher.IsOpen, 2000));
            Assert.Equal(normal, applied);
            launcher.Handle(new((int)LauncherInput.Accept));
            Assert.Equal(0, placements);
            launcher.Open();
            Assert.True(recovered.Drawn.Wait(2000));
            Assert.True(launcher.IsOpen);
            Assert.Equal(2, attempts);
            Assert.Equal(new PixelRect(0, 0, 1920, 1040), Assert.Single(recovered.Shown));
        }
        finally
        {
            fail.Set();
        }
    }

    /// <summary>プレビュー用の接続も描画失敗後の要求で再作成する。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PaintOrSurfaceFaultAllowsNextRequest(bool surfaceFault)
    {
        var broken = new FakeOverlay { ThrowOnShow = surfaceFault };
        var recovered = new FakeOverlay();
        var attempts = 0;
        using var session = new OverlaySession(false, _ =>
        {
            var fake = Interlocked.Increment(ref attempts) == 1 ? broken : recovered;
            return new(() => (fake, fake));
        });
        session.Request(new(0, 0, 1, 1), () => surfaceFault ? OverlayPainter.Create(1, 1) :
            throw new InvalidOperationException());
        Assert.True(broken.Disposed.Wait(2000));
        session.Request(new(1, 2, 1, 1), () => OverlayPainter.Create(1, 1));
        Assert.True(recovered.Drawn.Wait(2000));
        Assert.Equal(2, attempts);
        Assert.Equal(new PixelRect(1, 2, 1, 1), Assert.Single(recovered.Shown));
    }

    /// <summary>作成が常に失敗しても短時間の試行に上限を設け、入力を捕捉し続けない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedFactoryFailuresAreBounded(bool synchronous)
    {
        var attempts = 0;
        using var session = new OverlaySession(true, _ =>
        {
            Interlocked.Increment(ref attempts);
            if (synchronous)
            {
                throw new InvalidOperationException();
            }

            return new(() => throw new InvalidOperationException());
        });
        var normal = HotkeyBindingsBuilder.Build(new PortableSettings().Hotkeys).Bindings;
        IReadOnlyList<HotkeyBinding> applied = [];
        using var launcher = new LauncherController(new FakeInspector(), new FakeMonitors(), () => new(),
            session, (_, _, _) => { }, value => applied = value, normal, 999);
        for (var index = 0; index < 20; index++)
        {
            launcher.Open();
            Assert.True(SpinWait.SpinUntil(() => !launcher.IsOpen, 2000));
            Assert.Equal(normal, applied);
        }

        Assert.InRange(attempts, 2, 3);
    }

    /// <summary>購読前に終了済みでも失敗を拾い、ランチャーの一時割り当てを残さない。</summary>
    [Fact]
    public void FaultBeforeSubscriptionClosesLauncher()
    {
        var failedBeforeSubscription = false;
        using var session = new OverlaySession(true, _ =>
        {
            var thread = new OverlayThread(() => throw new InvalidOperationException());
            failedBeforeSubscription = SpinWait.SpinUntil(() => thread.IsFaulted, 2000);
            return thread;
        });
        var normal = HotkeyBindingsBuilder.Build(new PortableSettings().Hotkeys).Bindings;
        IReadOnlyList<HotkeyBinding> applied = [];
        using var launcher = new LauncherController(new FakeInspector(), new FakeMonitors(), () => new(),
            session, (_, _, _) => { }, value => applied = value, normal, 999);
        launcher.Open();
        Assert.True(failedBeforeSubscription);
        Assert.False(launcher.IsOpen);
        Assert.Equal(normal, applied);
    }

    /// <summary>失敗通知が Launcher のロックを待っていても Open の描画要求を塞がない。</summary>
    [Fact]
    public async Task FaultNotificationAndConcurrentOpenCloseDoNotDeadlock()
    {
        using var fail = new ManualResetEventSlim(false);
        using var faultEntered = new ManualResetEventSlim(false);
        using var faultFinished = new ManualResetEventSlim(false);
        var attempts = 0;
        var race = false;
        var session = new OverlaySession(true, _ =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            return new(() =>
            {
                if (attempt == 1)
                {
                    fail.Wait();
                    throw new InvalidOperationException();
                }

                var fake = new FakeOverlay();
                return (fake, fake);
            });
        });
        session.Faulted += faultEntered.Set;
        var normal = HotkeyBindingsBuilder.Build(new PortableSettings().Hotkeys).Bindings;
        var launcher = new LauncherController(new FakeInspector(), new FakeMonitors(), () => new(),
            session, (_, _, _) => { }, bindings =>
            {
                if (race && bindings.Any(binding => binding.Id == (int)LauncherInput.Accept))
                {
                    // Open が Launcher のロックを持つ間に描画スレッドから通知を始める。
                    fail.Set();
                    Assert.True(faultEntered.Wait(2000));
                }
            }, normal, 999);
        session.Faulted += faultFinished.Set;
        try
        {
            launcher.Open();
            launcher.Close();
            race = true;
            await Task.Run(launcher.Open).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(faultFinished.Wait(2000));
            Assert.False(launcher.IsOpen);
        }
        finally
        {
            fail.Set();
            // 回帰でロックが逆転した場合も、テストの後片付けを無限に待たない。
            await Task.Run(() =>
            {
                launcher.Dispose();
                session.Dispose();
            }).WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    private sealed class FakeOverlay : IOverlaySurface, IOverlayLoop
    {
        private readonly AutoResetEvent _wake = new(false);
        internal bool ThrowOnShow { get; init; }
        internal ConcurrentQueue<PixelRect> Shown { get; } = new();
        internal ManualResetEventSlim Drawn { get; } = new(false);
        internal ManualResetEventSlim Disposed { get; } = new(false);
        public event Action<OverlayPointer>? Pointer { add { } remove { } }
        public void Show(PixelRect bounds, OverlayFrame frame)
        {
            if (ThrowOnShow)
            {
                throw new InvalidOperationException();
            }

            Shown.Enqueue(bounds);
            Drawn.Set();
        }

        public void Hide() { }
        public void Wake() => _wake.Set();
        public void Run(Func<bool> process)
        {
            while (process())
            {
                _wake.WaitOne();
            }
        }

        public void Dispose() => Disposed.Set();
    }
}
