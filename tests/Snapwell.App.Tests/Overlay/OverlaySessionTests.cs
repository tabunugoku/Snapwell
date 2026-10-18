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
            Assert.True(launcher.IsOpen);
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

    /// <summary>旧世代の通知が到着する前の要求でも、死んだスレッドを交換して表示を保つ。</summary>
    [Fact]
    public void OpenBeforeFaultDeliveryReplacesUnavailableGeneration()
    {
        using var fail = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        using var delivered = new ManualResetEventSlim(false);
        var recovered = new FakeOverlay();
        var attempts = 0;
        using var session = new OverlaySession(true, _ =>
        {
            if (Interlocked.Increment(ref attempts) > 1)
            {
                return new(() => (recovered, recovered));
            }

            var thread = new OverlayThread(() =>
            {
                fail.Wait();
                throw new InvalidOperationException();
            });
            thread.Faulted += () =>
            {
                entered.Set();
                release.Wait();
            };
            thread.Faulted += delivered.Set;
            return thread;
        });
        using var launcher = CreateLauncher(session);
        try
        {
            launcher.Open();
            fail.Set();
            Assert.True(entered.Wait(2000));
            Assert.True(launcher.IsOpen);
            Assert.False(session.IsAvailable);
            launcher.Open();
            Assert.True(recovered.Drawn.Wait(3000));
            Assert.True(launcher.IsOpen);
            release.Set();
            Assert.True(delivered.Wait(2000));
            Assert.True(launcher.IsOpen);
            Assert.Equal(2, attempts);
        }
        finally
        {
            fail.Set();
            release.Set();
        }
    }

    /// <summary>失敗通知が Launcher に届くまでに次の表示を開いても、その表示を閉じない。</summary>
    [Fact]
    public void DelayedFailureDoesNotCloseNewLauncher()
    {
        using var fail = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        using var delivered = new ManualResetEventSlim(false);
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
        session.Faulted += () =>
        {
            entered.Set();
            release.Wait();
        };
        using var launcher = CreateLauncher(session);
        session.Faulted += delivered.Set;
        try
        {
            launcher.Open();
            fail.Set();
            Assert.True(entered.Wait(2000));
            launcher.Close();
            launcher.Open();
            Assert.True(recovered.Drawn.Wait(3000));
            release.Set();
            Assert.True(delivered.Wait(2000));
            Assert.True(launcher.IsOpen);
        }
        finally
        {
            fail.Set();
            release.Set();
        }
    }

    /// <summary>起床通知が失敗しても入力を戻し、次の要求を新しい表示先へ送る。</summary>
    [Fact]
    public void WakeFailureRecoversOnNextOpen()
    {
        var broken = new FakeOverlay();
        var recovered = new FakeOverlay();
        var attempts = 0;
        var diagnostics = new ConcurrentQueue<string>();
        using var session = new OverlaySession(true, _ =>
        {
            var fake = Interlocked.Increment(ref attempts) == 1 ? broken : recovered;
            return new(() => (fake, fake));
        }, (_, state, _) => diagnostics.Enqueue(state));
        using var launcher = CreateLauncher(session);
        launcher.Open();
        Assert.True(broken.Drawn.Wait(3000));
        broken.ThrowNextWake = true;
        launcher.Handle(new((int)LauncherInput.Right));
        Assert.False(launcher.IsOpen);
        launcher.Open();
        Assert.True(recovered.Drawn.Wait(3000));
        Assert.True(launcher.IsOpen);
        Assert.Contains("request-failed", diagnostics);
        Assert.Contains("recovery-requested", diagnostics);
    }

    /// <summary>閉じる起床通知の失敗でも入力を戻し、次回は新しい世代で表示する。</summary>
    [Theory]
    [InlineData("close")]
    [InlineData("cancel")]
    [InlineData("accept")]
    [InlineData("pointer")]
    public void HideWakeFailureRestoresBindingsAndRecovers(string operation)
    {
        var broken = new FakeOverlay();
        var recovered = new FakeOverlay();
        var attempts = 0;
        using var session = new OverlaySession(true, _ =>
        {
            var fake = Interlocked.Increment(ref attempts) == 1 ? broken : recovered;
            return new(() => (fake, fake));
        });
        var normal = HotkeyBindingsBuilder.Build(new PortableSettings().Hotkeys).Bindings;
        IReadOnlyList<HotkeyBinding> applied = [];
        var placements = 0;
        using var launcher = new LauncherController(new FakeInspector(), new FakeMonitors(), () => new(),
            session, (_, _, _) => placements++, value => applied = value, normal, 999,
            () => OverlayTheme.Light);
        launcher.Open();
        Assert.True(broken.Drawn.Wait(3000));
        broken.ThrowNextWake = true;
        var exception = Record.Exception(() =>
        {
            switch (operation)
            {
                case "close":
                    launcher.Close();
                    break;
                case "pointer":
                    broken.Raise(new(OverlayPointerKind.RightDown, new(10, 10)));
                    break;
                default:
                    launcher.Handle(new((int)(operation == "accept" ? LauncherInput.Accept : LauncherInput.Cancel)));
                    break;
            }
        });
        Assert.Null(exception);
        Assert.False(launcher.IsOpen);
        Assert.Equal(normal, applied);
        Assert.Equal(operation == "accept" ? 1 : 0, placements);
        Assert.False(session.IsAvailable);
        Assert.True(broken.Disposed.Wait(2000));
        launcher.Open();
        Assert.True(recovered.Drawn.Wait(3000));
        Assert.True(launcher.IsOpen);
        Assert.Equal(2, attempts);
        Assert.Contains(applied, binding => binding.Id == (int)LauncherInput.Accept);
    }

    /// <summary>Hide の失敗通知ではセッションのロックを解放し、一度だけ通知する。</summary>
    [Fact]
    public void HideWakeFailureNotifiesOutsideSessionLock()
    {
        var fake = new FakeOverlay();
        using var session = new OverlaySession(false, _ => new(() => (fake, fake)));
        var notifications = 0;
        session.Faulted += () =>
        {
            Assert.True(Task.Run(session.Hide).Wait(1000));
            notifications++;
        };
        session.Request(new(0, 0, 1, 1), () => OverlayPainter.Create(1, 1));
        Assert.True(fake.Drawn.Wait(2000));
        fake.ThrowNextWake = true;
        session.Hide();
        Assert.True(fake.Disposed.Wait(2000));
        Assert.Equal(1, notifications);
    }

    /// <summary>失敗通知自体が例外を出しても、選択を消した時点の割り当てを必ず戻す。</summary>
    [Fact]
    public void HideFailureNotificationCannotSkipBindingRestoration()
    {
        var fake = new FakeOverlay();
        using var session = new OverlaySession(true, _ => new(() => (fake, fake)));
        session.Faulted += () => throw new InvalidOperationException();
        var normal = HotkeyBindingsBuilder.Build(new PortableSettings().Hotkeys).Bindings;
        IReadOnlyList<HotkeyBinding> applied = [];
        using var launcher = new LauncherController(new FakeInspector(), new FakeMonitors(), () => new(),
            session, (_, _, _) => { }, value => applied = value, normal, 999, () => OverlayTheme.Light);
        launcher.Open();
        Assert.True(fake.Drawn.Wait(3000));
        fake.ThrowNextWake = true;
        Assert.Throws<InvalidOperationException>(launcher.Close);
        Assert.False(launcher.IsOpen);
        Assert.Equal(normal, applied);
    }

    /// <summary>250 ms で停止を打ち切った旧世代が後から失敗しても、新しい表示を守る。</summary>
    [Fact]
    public void TimedOutGenerationDoesNotAffectNextOpen()
    {
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        using var finished = new ManualResetEventSlim(false);
        var recovered = new FakeOverlay();
        var attempts = 0;
        var diagnostics = new ConcurrentQueue<string>();
        using var session = new OverlaySession(true, _ =>
        {
            if (Interlocked.Increment(ref attempts) > 1)
            {
                return new(() => (recovered, recovered));
            }

            var thread = new OverlayThread(() =>
            {
                entered.Set();
                release.Wait();
                throw new InvalidOperationException();
            }, (_, state, _) => diagnostics.Enqueue(state));
            thread.Faulted += finished.Set;
            return thread;
        });
        using var launcher = CreateLauncher(session);
        try
        {
            launcher.Open();
            Assert.True(entered.Wait(2000));
            launcher.SetEnabled(false);
            session.SetEnabled(false);
            Assert.Contains("stop-timeout", diagnostics);
            session.SetEnabled(true);
            launcher.SetEnabled(true);
            launcher.Open();
            Assert.True(recovered.Drawn.Wait(3000));
            release.Set();
            Assert.True(finished.Wait(2000));
            Assert.True(launcher.IsOpen);
        }
        finally
        {
            release.Set();
        }
    }

    private static LauncherController CreateLauncher(IOverlayPresenter presenter) => new(
        new FakeInspector(), new FakeMonitors(), () => new(), presenter, (_, _, _) => { }, _ => { },
        HotkeyBindingsBuilder.Build(new PortableSettings().Hotkeys).Bindings, 999, () => OverlayTheme.Light);

    private sealed class FakeOverlay : IOverlaySurface, IOverlayLoop
    {
        private readonly AutoResetEvent _wake = new(false);
        internal bool ThrowOnShow { get; init; }
        internal bool ThrowNextWake { get; set; }
        internal ConcurrentQueue<PixelRect> Shown { get; } = new();
        internal ManualResetEventSlim Drawn { get; } = new(false);
        internal ManualResetEventSlim Disposed { get; } = new(false);
        public event Action<OverlayPointer>? Pointer;
        internal void Raise(OverlayPointer pointer) => Pointer?.Invoke(pointer);
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
        public void Wake()
        {
            if (ThrowNextWake)
            {
                ThrowNextWake = false;
                throw new InvalidOperationException();
            }

            _wake.Set();
        }
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
