using Snapwell.App.Dragging;
using Snapwell.App.Hotkeys;
using Snapwell.App.Lifecycle;
using Snapwell.App.Overlay;
using Snapwell.App.Tests.Dragging;
using Snapwell.App.Tests.Motion;
using Snapwell.App.Tests.Overlay;
using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Lifecycle;

public sealed class ResidentRuntimeTests
{
    /// <summary>整列要求を実行役へ一度だけ渡し、一時停止・破棄後は止める。</summary>
    [Fact]
    public void CurrentMonitorArrangementReachesExecutorOnceAndStopsWhenPausedOrDisposed()
    {
        using var fixture = new OverlayFixture(withLongPress: true);
        fixture.Inspector.Foreground = 99;
        fixture.Runtime.ArrangeCurrentMonitor();
        var request = Assert.Single(fixture.Motion.Requests);
        Assert.Equal((nint)1, request.Handle);
        var expected = WindowArrangement.Arrange(ArrangeLayout.Grid, new(0, 0, 1920, 1040), new(8, 8), 1)!;
        Assert.Equal(expected[0], request.Target);
        fixture.Runtime.SetEnabled(false);
        fixture.Runtime.ArrangeCurrentMonitor();
        Assert.Single(fixture.Motion.Requests);
        fixture.Runtime.SetEnabled(true);
        fixture.Runtime.ArrangeCurrentMonitor();
        Assert.Equal(2, fixture.Motion.Requests.Count);
        fixture.Runtime.Dispose();
        fixture.Runtime.ArrangeCurrentMonitor();
        Assert.Equal(2, fixture.Motion.Requests.Count);
    }

    /// <summary>確定後も長押しが使った移動ループは、配置の取り消しと追加の吸着を起こさない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptedHoldConsumesMoveSizeLoopUntilEnd(bool delayedStart)
    {
        using var fixture = new OverlayFixture(withLongPress: true);
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, true, 100, 100, 0));
        if (!delayedStart)
        {
            fixture.Runtime.Handle(new MoveSizeStarted(1));
        }

        fixture.Timer.Advance(450);
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, false, 100, 100, 451));
        var target = fixture.Launcher.Selection!.Bounds;
        fixture.Runtime.Handle(new KeyChord((int)LauncherInput.Accept));
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Equal(target, Assert.Single(fixture.Motion.Requests).Target);
        var cancellations = fixture.Motion.Canceled.Count;
        if (delayedStart)
        {
            fixture.Runtime.Handle(new MoveSizeStarted(1));
        }

        Assert.Equal(cancellations, fixture.Motion.Canceled.Count);
        Assert.True(fixture.Motion.IsMoving(1));
        fixture.Runtime.Handle(new MouseMoved(100, 100, 452));
        fixture.Runtime.Handle(new MouseMoved(600, 100, 492));
        fixture.Runtime.Handle(new MoveSizeEnded(1, 492));
        Assert.Equal(cancellations, fixture.Motion.Canceled.Count);
        Assert.Equal(target, Assert.Single(fixture.Motion.Requests).Target);
        Assert.True(fixture.Motion.IsMoving(1));
        Assert.Empty(fixture.PreviewPresenter.Requests);
    }

    /// <summary>終了通知の有無にかかわらず、次の左押下から通常のドラッグと吸着に戻す。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NextPressAfterAcceptedHoldCanSnap(bool deliverEnd)
    {
        using var fixture = new OverlayFixture(withLongPress: true);
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, true, 100, 100, 0));
        fixture.Timer.Advance(450);
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, false, 100, 100, 451));
        fixture.Runtime.Handle(new KeyChord((int)LauncherInput.Accept));
        if (deliverEnd)
        {
            fixture.Runtime.Handle(new MoveSizeStarted(1));
            fixture.Runtime.Handle(new MoveSizeEnded(1, 452));
        }

        Assert.Single(fixture.Motion.Requests);
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, true, 100, 100, 453));
        fixture.Runtime.Handle(new MoveSizeStarted(1));
        fixture.Runtime.Handle(new MouseMoved(100, 100, 454));
        fixture.Runtime.Handle(new MouseMoved(600, 100, 494));
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, false, 600, 100, 494));
        fixture.Runtime.Handle(new MoveSizeEnded(1, 494));
        Assert.Equal(2, fixture.Motion.Requests.Count);
        Assert.True(fixture.Drag.Enabled);
        Assert.NotEmpty(fixture.PreviewPresenter.Requests);
    }

    /// <summary>表示だけを閉じても、消費済みの移動通知で次のランチャーを閉じない。</summary>
    [Theory]
    [InlineData("accept")]
    [InlineData("cancel")]
    [InlineData("external")]
    public void ConsumedMoveSizeLoopKeepsSubsequentLauncherOpen(string close)
    {
        using var fixture = new OverlayFixture(withLongPress: true);
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, true, 100, 100, 0));
        fixture.Timer.Advance(450);
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, false, 100, 100, 451));
        if (close == "external")
        {
            fixture.Launcher.Close();
        }
        else
        {
            fixture.Runtime.Handle(new KeyChord((int)(close == "accept" ? LauncherInput.Accept : LauncherInput.Cancel)));
        }

        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        fixture.Runtime.Handle(new MoveSizeStarted(1));
        Assert.True(fixture.Launcher.IsOpen);
        fixture.Runtime.Handle(new MoveSizeEnded(1, 452));
        Assert.True(fixture.Launcher.IsOpen);
        fixture.Runtime.Handle(new MoveSizeStarted(1));
        Assert.False(fixture.Launcher.IsOpen);
    }

    /// <summary>一時停止で終了の来ない移動ループの記録を消し、再開後のドラッグを通す。</summary>
    [Fact]
    public void PauseClearsAcceptedHoldMoveSizeLoop()
    {
        using var fixture = new OverlayFixture(withLongPress: true);
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, true, 100, 100, 0));
        fixture.Timer.Advance(450);
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, false, 100, 100, 451));
        fixture.Runtime.Handle(new KeyChord((int)LauncherInput.Accept));
        fixture.Runtime.SetEnabled(false);
        fixture.Runtime.SetEnabled(true);
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        fixture.Runtime.Handle(new MoveSizeStarted(1));
        Assert.False(fixture.Launcher.IsOpen);
        Assert.True(fixture.Drag.Enabled);
    }

    /// <summary>長押しの解放前は Enter を消費し、選択を保ったまま解放後に一度だけ配置する。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptWaitsForHoldRelease(bool endBeforeRelease)
    {
        using var fixture = new OverlayFixture(withLongPress: true);
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, true, 100, 100, 0));
        fixture.Runtime.Handle(new MoveSizeStarted(1));
        fixture.Timer.Advance(450);
        if (endBeforeRelease)
        {
            fixture.Runtime.Handle(new MoveSizeEnded(1, 451));
        }

        fixture.Runtime.Handle(new KeyChord((int)LauncherInput.ExtendRight));
        var target = fixture.Launcher.Selection!.Bounds;
        fixture.Runtime.Handle(new KeyChord((int)LauncherInput.Accept));
        fixture.Runtime.Handle(new KeyChord((int)LauncherInput.Accept));
        Assert.Empty(fixture.Motion.Requests);
        Assert.True(fixture.Launcher.IsOpen);
        Assert.False(fixture.Drag.Enabled);
        Assert.Contains(fixture.Applied, binding => binding.Id == (int)LauncherInput.Accept && binding.Suppress);
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, false, 100, 100, 452));
        fixture.Runtime.Handle(new KeyChord((int)LauncherInput.Accept));
        fixture.Runtime.Handle(new KeyChord((int)LauncherInput.Accept));
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Equal(target, Assert.Single(fixture.Motion.Requests).Target);
    }

    /// <summary>解放後に遅配された同じ移動ループは表示を保ち、追跡と吸着を始めない。</summary>
    [Fact]
    public void MoveSizeStartDeliveredAfterReleaseKeepsLauncherWithoutSnapping()
    {
        using var fixture = new OverlayFixture(withLongPress: true);
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, true, 100, 100, 0));
        fixture.Timer.Advance(450);
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, false, 100, 100, 451));
        fixture.Runtime.Handle(new MoveSizeStarted(1));
        Assert.True(fixture.Launcher.IsOpen);
        Assert.False(fixture.Drag.Enabled);
        Assert.False(fixture.Tracking.Last());
        fixture.Runtime.Handle(new MouseMoved(500, 100, 460));
        fixture.Runtime.Handle(new MoveSizeEnded(1, 461));
        Assert.True(fixture.Launcher.IsOpen);
        Assert.False(fixture.Drag.Enabled);
        Assert.Empty(fixture.Motion.Requests);
        Assert.Empty(fixture.PreviewPresenter.Requests);
    }

    /// <summary>移動終了後の次の押下からは同じ対象も別対象も通常のドラッグに戻す。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void NextPressAndMoveSizeStartCloseHeldLauncher(int handle)
    {
        using var fixture = new OverlayFixture(withLongPress: true);
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, true, 100, 100, 0));
        fixture.Runtime.Handle(new MoveSizeStarted(1));
        fixture.Timer.Advance(450);
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, false, 100, 100, 451));
        fixture.Runtime.Handle(new MoveSizeEnded(1, 452));
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, true, 100, 100, 453));
        fixture.Runtime.Handle(new MoveSizeStarted(handle));
        Assert.False(fixture.Launcher.IsOpen);
        Assert.True(fixture.Drag.Enabled);
    }

    /// <summary>所有中の停止と Cancel は、古いタイマーと移動通知を次の表示へ持ち越さない。</summary>
    [Theory]
    [InlineData("pause")]
    [InlineData("dispose")]
    [InlineData("cancel")]
    public void StoppingOwnedLauncherInvalidatesLateNotifications(string action)
    {
        using var fixture = new OverlayFixture(withLongPress: true);
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, true, 100, 100, 0));
        fixture.Timer.Advance(450);
        if (action == "dispose")
        {
            fixture.Runtime.Dispose();
        }
        else if (action == "pause")
        {
            fixture.Runtime.SetEnabled(false);
        }
        else
        {
            fixture.Runtime.Handle(new KeyChord((int)LauncherInput.Cancel));
        }

        Assert.False(fixture.Launcher.IsOpen);
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, false, 100, 100, 451));
        fixture.Runtime.Handle(new MoveSizeStarted(1));
        fixture.Runtime.Handle(new MoveSizeEnded(1, 452));
        fixture.Timer.FireEvenIfCanceled(0);
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Empty(fixture.Motion.Requests);
        if (action != "dispose")
        {
            fixture.Runtime.SetEnabled(true);
            fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
            fixture.Runtime.Handle(new MoveSizeStarted(1));
            Assert.False(fixture.Launcher.IsOpen);
            Assert.True(fixture.Drag.Enabled);
        }
    }

    /// <summary>先行する移動ループと解放通知は、長押しの表示を閉じたり吸着させたりしない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaptionHoldOpensPressedTargetAndReleaseKeepsLauncher(bool endBeforeRelease)
    {
        using var fixture = new OverlayFixture(withLongPress: true);
        fixture.Inspector.Foreground = 2;
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, true, 100, 100, 0));
        fixture.Runtime.Handle(new MoveSizeStarted(1));
        fixture.Timer.Advance(450);
        Assert.True(fixture.Launcher.IsOpen);
        Assert.False(fixture.Drag.Enabled);
        Assert.True(fixture.Tracking.Last());
        if (endBeforeRelease)
        {
            fixture.Runtime.Handle(new MoveSizeEnded(1, 451));
        }

        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, false, 100, 100, 451));
        fixture.Runtime.Handle(new MoveSizeEnded(1, 452));
        Assert.True(fixture.Launcher.IsOpen);
        Assert.Empty(fixture.Motion.Requests);
        fixture.Runtime.Handle(new KeyChord((int)LauncherInput.Accept));
        Assert.Equal((nint)1, Assert.Single(fixture.Motion.Requests).Handle);
    }

    /// <summary>同じ押下の遅い開始通知でも表示を保つ。</summary>
    [Fact]
    public void LateMoveSizeStartFromHeldTargetKeepsLauncher()
    {
        using var fixture = new OverlayFixture(withLongPress: true);
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, true, 100, 100, 0));
        fixture.Timer.Advance(450);
        fixture.Runtime.Handle(new MoveSizeStarted(1));
        Assert.True(fixture.Launcher.IsOpen);
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, false, 100, 100, 451));
        fixture.Runtime.Handle(new MoveSizeEnded(1, 452));
        Assert.Empty(fixture.Motion.Requests);
    }

    /// <summary>長押し後の移動は表示を閉じ、そのドラッグの解放では吸着しない。</summary>
    [Fact]
    public void MovementAfterHoldClosesLauncherWithoutSnapping()
    {
        using var fixture = new OverlayFixture(withLongPress: true);
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, true, 100, 100, 0));
        fixture.Runtime.Handle(new MoveSizeStarted(1));
        fixture.Timer.Advance(450);
        fixture.Runtime.Handle(new MouseMoved(107, 100, 451));
        Assert.False(fixture.Launcher.IsOpen);
        fixture.Runtime.Handle(new MouseMoved(500, 100, 460));
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, false, 500, 100, 461));
        fixture.Runtime.Handle(new MoveSizeEnded(1, 462));
        Assert.Empty(fixture.Motion.Requests);
        Assert.False(fixture.Tracking.Last());
    }

    /// <summary>外部で閉じた長押し表示のあとに開いた表示を、古い押下の移動で閉じない。</summary>
    [Fact]
    public void ClosedHoldCannotCloseSubsequentHotkeyLauncher()
    {
        using var fixture = new OverlayFixture(withLongPress: true);
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, true, 100, 100, 0));
        fixture.Timer.Advance(450);
        fixture.Launcher.Close();
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        fixture.Runtime.Handle(new MouseMoved(107, 100, 451));
        Assert.True(fixture.Launcher.IsOpen);
        Assert.False(fixture.Tracking.Last());
    }

    /// <summary>停止や一時停止は中止済みタイマーの遅配も無効にする。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PauseOrDisposeInvalidatesPendingTimer(bool dispose)
    {
        using var fixture = new OverlayFixture(withLongPress: true);
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, true, 100, 100, 0));
        if (dispose)
        {
            fixture.Runtime.Dispose();
        }
        else
        {
            fixture.Runtime.SetEnabled(false);
            fixture.Runtime.SetEnabled(true);
        }

        fixture.Timer.FireEvenIfCanceled(0);
        Assert.False(fixture.Launcher.IsOpen);
        Assert.False(fixture.Tracking.Last());
        Assert.Empty(fixture.Motion.Requests);
    }

    /// <summary>停止中や表示中の押下ではタイマーを作らない。</summary>
    [Theory]
    [InlineData("paused")]
    [InlineData("disposed")]
    [InlineData("open")]
    public void IneligibleRuntimeDoesNotStartHold(string state)
    {
        using var fixture = new OverlayFixture(withLongPress: true);
        if (state == "paused")
        {
            fixture.Runtime.SetEnabled(false);
        }
        else if (state == "disposed")
        {
            fixture.Runtime.Dispose();
        }
        else
        {
            fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        }

        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, true, 100, 100, 0));
        Assert.Equal(0, fixture.Timer.Count);
    }

    /// <summary>応答待ちの間にも別スレッドの配送と停止が完了し、古い結果は表示しない。</summary>
    [Theory]
    [InlineData("release")]
    [InlineData("move")]
    [InlineData("pause")]
    [InlineData("dispose")]
    [InlineData("open")]
    [InlineData("newPress")]
    public void HitTestRunsOutsideRuntimeLockAndRevalidatesState(string action)
    {
        using var fixture = new OverlayFixture(withLongPress: true);
        fixture.CaptionHitTest = (_, _) =>
        {
            var delivery = Task.Run(() =>
            {
                switch (action)
                {
                    case "release":
                        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, false, 100, 100, 450));
                        break;
                    case "move":
                        fixture.Runtime.Handle(new MouseMoved(107, 100, 450));
                        break;
                    case "pause":
                        fixture.Runtime.SetEnabled(false);
                        fixture.Runtime.SetEnabled(true);
                        break;
                    case "dispose":
                        fixture.Runtime.Dispose();
                        break;
                    case "open":
                        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
                        break;
                    case "newPress":
                        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, true, 100, 100, 450));
                        break;
                }
            });
            Assert.True(delivery.Wait(TimeSpan.FromSeconds(2)));
            return true;
        };
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, true, 100, 100, 0));
        fixture.Timer.Advance(450);
        Assert.Equal(action == "open", fixture.Launcher.IsOpen);
        Assert.Empty(fixture.Motion.Requests);
    }

    /// <summary>ヒットテスト失敗と、発火直前のカーソル移動では表示しない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedHitTestOrMissedMovementDoesNotOpen(bool moved)
    {
        using var fixture = new OverlayFixture(withLongPress: true);
        fixture.CaptionHitTest = (_, _) => moved;
        // 上端の帯の外（クライアント領域）を押す。
        fixture.Cursor = new(100, 250);
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, true, 100, 250, 0));
        if (moved)
        {
            fixture.Cursor = new(107, 250);
        }

        fixture.Timer.Advance(450);
        Assert.False(fixture.Launcher.IsOpen);
    }

    /// <summary>一方が要求をやめても、他方の追跡要求を保つ。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MouseTrackingContinuesUntilBothVotesAreFalse(bool longPressFirst)
    {
        var enabled = false;
        var votes = new MouseTrackingVotes(value => enabled = value);
        votes.SetDrag(true);
        votes.SetLongPress(true);
        if (longPressFirst)
        {
            votes.SetLongPress(false);
            Assert.True(enabled);
            votes.SetDrag(false);
        }
        else
        {
            votes.SetDrag(false);
            Assert.True(enabled);
            votes.SetLongPress(false);
        }

        Assert.False(enabled);
    }

    /// <summary>対象が生きていても別の前面へ移ったら閉じ、遅れた Enter では動かさない。</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    public void ForegroundChangeClosesLauncherAndReleasesBindings(int handle)
    {
        using var fixture = new OverlayFixture();
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        fixture.Runtime.Handle(new ForegroundChanged(handle));
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Equal(fixture.Normal, fixture.Applied);
        Assert.True(fixture.Drag.Enabled);
        fixture.Runtime.Handle(new KeyChord((int)LauncherInput.Accept));
        Assert.Empty(fixture.Motion.Requests);
    }

    /// <summary>遅れた対象自身の前面化と自プロセスの前面化では選択を保つ。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(99)]
    public void TargetOrOwnForegroundKeepsLauncherOpen(int handle)
    {
        using var fixture = new OverlayFixture();
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        fixture.Runtime.Handle(new ForegroundChanged(handle));
        Assert.True(fixture.Launcher.IsOpen);
        Assert.Contains(fixture.Applied, binding => binding.Id == (int)LauncherInput.Accept);
    }

    /// <summary>通常のキーとランチャー用のキーを、それぞれの実行経路へ渡す。</summary>
    [Fact]
    public void RoutesNormalAndLauncherChordsAndPreventsSimultaneousPreview()
    {
        using var fixture = new OverlayFixture();
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.LeftHalf));
        Assert.Single(fixture.Motion.Requests);
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        Assert.True(fixture.Launcher.IsOpen);
        Assert.False(fixture.Drag.Enabled);
        fixture.Runtime.SetEnabled(true);
        Assert.Contains(fixture.Applied, binding => binding.Id == (int)LauncherInput.Accept);
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.RightHalf));
        Assert.Single(fixture.Motion.Requests);
        fixture.Runtime.Handle(new KeyChord((int)LauncherInput.ExtendRight));
        Assert.Equal(new ZoneRange(0, 0, 2, 1), fixture.Launcher.Selection!.Range);
        var target = fixture.Launcher.Selection.Bounds;
        fixture.Runtime.Handle(new KeyChord((int)LauncherInput.Accept));
        Assert.Equal(target, fixture.Motion.Requests.Last().Target);
        Assert.False(fixture.Launcher.IsOpen);
        Assert.True(fixture.Drag.Enabled);
        Assert.True(fixture.PreviewPresenter.Hides > 0);
        Assert.Equal(fixture.Normal, fixture.Applied);
    }

    /// <summary>一時停止は表示とスレッドを閉じ、再開の割り当てだけを残す。</summary>
    [Fact]
    public void PausingClosesLauncherAndStopsOverlays()
    {
        using var fixture = new OverlayFixture();
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        fixture.Runtime.SetEnabled(false);
        Assert.False(fixture.Launcher.IsOpen);
        Assert.False(fixture.Drag.Enabled);
        Assert.Equal((int)WindowCommand.ToggleEnabled, Assert.Single(fixture.Applied).Id);
        Assert.False(fixture.OverlayStates.Last());
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        fixture.Runtime.Handle(new KeyChord((int)LauncherInput.Accept));
        fixture.Runtime.Handle(new ForegroundChanged(2));
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Empty(fixture.Motion.Requests);
        Assert.Equal((int)WindowCommand.ToggleEnabled, Assert.Single(fixture.Applied).Id);
        fixture.Runtime.SetEnabled(true);
        Assert.True(fixture.OverlayStates.Last());
        Assert.Equal(fixture.Normal, fixture.Applied);
    }

    /// <summary>Toggle の配送時と別ウィンドウのドラッグ開始時にランチャーを閉じる。</summary>
    [Fact]
    public void ToggleAndNewDragCloseLauncher()
    {
        using var fixture = new OverlayFixture();
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.ToggleEnabled));
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Equal(1, fixture.Toggles);
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        fixture.Runtime.Handle(new MoveSizeStarted(1));
        Assert.False(fixture.Launcher.IsOpen);
        Assert.True(fixture.Drag.Enabled);
        fixture.Runtime.Handle(new MouseMoved(0, 0, 0));
        fixture.Runtime.Handle(new MouseMoved(500, 0, 40));
        Assert.Single(fixture.PreviewPresenter.Requests);
    }

    /// <summary>前面変更の通知で閉じた対象を点検し、作業領域外のクリックも配送する。</summary>
    [Fact]
    public void ForegroundAndGlobalMouseEventsCloseInvalidOrOutsideLauncher()
    {
        using var fixture = new OverlayFixture();
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        fixture.Inspector.Exists = false;
        fixture.Runtime.Handle(new ForegroundChanged(2));
        Assert.False(fixture.Launcher.IsOpen);
        fixture.Inspector.Exists = true;
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, true, -1, -1, 0));
        Assert.False(fixture.Launcher.IsOpen);
    }

    /// <summary>終了は入力と表示を閉じ、オーバーレイを止めてから移動を止める。</summary>
    [Fact]
    public void DisposeStopsOverlaysBeforeMotionAndIgnoresLateInput()
    {
        using var fixture = new OverlayFixture();
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        fixture.BeforeOverlayDispose = () =>
        {
            Assert.Empty(fixture.Applied);
            Assert.False(fixture.Launcher.IsOpen);
            Assert.False(fixture.Drag.Enabled);
            Assert.Equal(0, fixture.Motion.DisposeCalls);
        };
        fixture.Runtime.Dispose();
        Assert.Equal(1, fixture.Motion.DisposeCalls);
        Assert.Equal(1, fixture.OverlayDisposals);
        fixture.Runtime.SetEnabled(true);
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.LeftHalf));
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Empty(fixture.Motion.Requests);
        Assert.Empty(fixture.Applied);
    }

    /// <summary>停止した描画処理を終了の待機に引きずらない。</summary>
    [Fact]
    public void RuntimeDisposeReturnsWithBlockedOverlay()
    {
        using var release = new ManualResetEventSlim(false);
        var fake = new ControlledOverlay { Block = release };
        var overlay = new OverlayThread(() => (fake, fake));
        var inspector = new FakeInspector();
        var motion = new FakeMotion();
        var drag = new DragSnapController(inspector, new FakeMonitors(), motion, () => new(), _ => { }, 999);
        var hotkeys = new HotkeyCommandExecutor(inspector, new FakeMonitors(), motion, new NoPosition(),
            () => new(), () => true, () => { }, 999);
        using var runtime = new ResidentRuntime(drag, hotkeys, motion, [], _ => { }, overlays: [overlay]);
        try
        {
            overlay.Request(new(0, 0, 1, 1), () => OverlayPainter.Create(1, 1));
            Assert.True(fake.Entered.Wait(2000));
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            runtime.Dispose();
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1));
            Assert.Equal(1, motion.DisposeCalls);
        }
        finally
        {
            release.Set();
        }

        Assert.True(fake.Disposed.Wait(2000));
    }

    /// <summary>一時停止でスレッドを破棄し、再開後の描画だけで作り直す。</summary>
    [Fact]
    public void OverlaySessionRestartsLazilyAfterPause()
    {
        var fakes = new List<ControlledOverlay>();
        using var session = new OverlaySession(true, _ =>
        {
            var fake = new ControlledOverlay();
            fakes.Add(fake);
            return new(() => (fake, fake));
        });
        session.Request(new(0, 0, 1, 1), () => OverlayPainter.Create(1, 1));
        Assert.True(fakes[0].Entered.Wait(2000));
        session.SetEnabled(false);
        Assert.True(fakes[0].Disposed.Wait(2000));
        session.Request(new(0, 0, 1, 1), () => throw new InvalidOperationException());
        Assert.Single(fakes);
        session.SetEnabled(true);
        Assert.Single(fakes);
        session.Request(new(0, 0, 1, 1), () => OverlayPainter.Create(1, 1));
        Assert.Equal(2, fakes.Count);
        Assert.True(fakes[1].Entered.Wait(2000));
        session.Dispose();
        Assert.True(fakes[1].Disposed.Wait(2000));
    }

    /// <summary>表示先が失われて通知が遅れても、実際のキー判定から次の表示へ回復する。</summary>
    [Fact]
    public void ReopenChordRecoversUnavailableLauncherBeforeFailureNotification()
    {
        var presenter = new RecoveringPresenter();
        using var fixture = new OverlayFixture(launcherPresenter: presenter);
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        var matcher = new HotkeyMatcher();
        matcher.SetBindings(fixture.Applied);
        matcher.ProcessKey(0x11, true);
        matcher.ProcessKey(0x12, true);
        var match = matcher.ProcessKey(0x20, true);
        Assert.True(match.Matched);
        Assert.Equal((int)WindowCommand.OpenLauncher, match.Id);
        fixture.Runtime.Handle(new KeyChord(match.Id));
        Assert.Equal(1, presenter.Requests);

        presenter.IsAvailable = false;
        matcher.ProcessKey(0x20, false);
        match = matcher.ProcessKey(0x20, true);
        Assert.True(match.Matched);
        fixture.Runtime.Handle(new KeyChord(match.Id));
        Assert.True(fixture.Launcher.IsOpen);
        Assert.Equal(2, presenter.Requests);
    }

    private sealed class RecoveringPresenter : IOverlayPresenter
    {
        public bool IsAvailable { get; set; } = true;
        internal int Requests { get; private set; }
        public event Action<OverlayPointer>? Pointer { add { } remove { } }
        public void Request(PixelRect bounds, Func<OverlayFrame> paint)
        {
            Requests++;
            IsAvailable = true;
        }

        public void Hide() { }
        public void Dispose() { }
    }

    private sealed class OverlayFixture : IDisposable
    {
        internal FakeInspector Inspector { get; } = new();
        internal FakeMotion Motion { get; } = new();
        internal RecordingPresenter Presenter { get; } = new();
        internal RecordingPresenter PreviewPresenter { get; } = new();
        internal IReadOnlyList<HotkeyBinding> Normal { get; } =
            HotkeyBindingsBuilder.Build(new PortableSettings().Hotkeys).Bindings;
        internal IReadOnlyList<HotkeyBinding> Applied = [];
        internal List<bool> OverlayStates { get; } = [];
        internal Action? BeforeOverlayDispose;
        internal int OverlayDisposals;
        internal int Toggles;
        internal ControlledLongPressTimer Timer { get; } = new();
        internal Vec2? Cursor = new(100, 100);
        internal Func<nint, Vec2, bool>? CaptionHitTest;
        internal List<bool> Tracking { get; } = [];
        internal DragSnapController Drag { get; }
        internal LauncherController Launcher { get; }
        internal ResidentRuntime Runtime { get; }

        internal OverlayFixture(bool withLongPress = false, IOverlayPresenter? launcherPresenter = null)
        {
            var monitors = new FakeMonitors();
            var inspector = new ForegroundInspector(Inspector);
            var preview = new ZonePreview(PreviewPresenter, monitors);
            var tracking = new MouseTrackingVotes(Tracking.Add);
            Drag = new(inspector, monitors, Motion, () => new(), tracking.SetDrag, 999, () => 0, preview);
            var longPress = withLongPress ? new LongPressDetector(inspector, monitors,
                (handle, point) => CaptionHitTest?.Invoke(handle, point) ?? true, () => Cursor,
                tracking.SetLongPress, Timer.Schedule) : null;
            LauncherController? launcher = null;
            var hotkeys = new HotkeyCommandExecutor(inspector, monitors, Motion, new NoPosition(), () => new(),
                () => true, () => Toggles++, 999, () => launcher?.Open(), cursorPosition: () => Cursor);
            Launcher = launcher = new(inspector, monitors, () => new(), launcherPresenter ?? Presenter, hotkeys.Place,
                value => Applied = value, Normal, 999);
            Runtime = new(Drag, hotkeys, Motion, Normal, value => Applied = value, Launcher, preview,
                OverlayStates.Add, [new CallbackDisposable(() =>
                {
                    BeforeOverlayDispose?.Invoke();
                    OverlayDisposals++;
                })], longPress);
        }

        public void Dispose() => Runtime.Dispose();
    }

    private sealed class ForegroundInspector(FakeInspector target) : IWindowInspector
    {
        public bool TryInspect(nint handle,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out WindowSnapshot? snapshot)
        {
            if (handle == 99)
            {
                snapshot = target.Window with { Handle = handle, ProcessId = 999 };
                return true;
            }

            return target.TryInspect(handle, out snapshot);
        }

        public nint GetForegroundWindow() => target.GetForegroundWindow();
        public nint GetRootWindowAt(int x, int y) => target.GetRootWindowAt(x, y);
        public IReadOnlyList<nint> GetTopLevelWindows() => [target.Window.Handle];
    }

    private sealed class CallbackDisposable(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    private sealed class ControlledOverlay : IOverlaySurface, IOverlayLoop
    {
        private readonly AutoResetEvent _wake = new(false);
        internal ManualResetEventSlim Entered { get; } = new(false);
        internal ManualResetEventSlim Disposed { get; } = new(false);
        internal ManualResetEventSlim? Block { get; init; }
        public event Action<OverlayPointer>? Pointer { add { } remove { } }
        public void Show(PixelRect bounds, OverlayFrame frame)
        {
            Entered.Set();
            Block?.Wait();
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

    [Fact]
    public async Task DisposeDoesNotWaitForBlockedRestoreAndReturningRestoreCannotRequestMotion()
    {
        var inspector = new FakeInspector();
        inspector.Window = inspector.Window with { IsMaximized = true };
        var monitors = new FakeMonitors();
        var motion = new FakeMotion();
        using var position = new BlockingPosition(inspector);
        var drag = new DragSnapController(inspector, monitors, motion, () => new(), _ => { }, 999);
        var hotkeys = new HotkeyCommandExecutor(inspector, monitors, motion, position, () => new(),
            () => true, () => { }, 999);
        using var runtime = new ResidentRuntime(drag, hotkeys, motion, [], _ => { });
        try
        {
            await Task.Run(() => runtime.Handle(new KeyChord((int)WindowCommand.LeftHalf)))
                .WaitAsync(TimeSpan.FromSeconds(1));
            Assert.True(position.Entered.Wait(5000));
            await Task.Run(runtime.Dispose).WaitAsync(TimeSpan.FromSeconds(1));
            position.Continue.Set();
            Assert.True(position.Worker!.Join(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, motion.DisposeCalls);
            Assert.Empty(motion.Requests);
        }
        finally
        {
            position.Continue.Set();
        }
    }

    private sealed class BlockingPosition(FakeInspector inspector) : IWindowPositionApi, IDisposable
    {
        internal ManualResetEventSlim Entered { get; } = new(false);
        internal ManualResetEventSlim Continue { get; } = new(false);
        internal Thread? Worker;
        public bool Restore(nint handle)
        {
            Worker = Thread.CurrentThread;
            Entered.Set();
            Continue.Wait();
            inspector.Window = inspector.Window with { IsMaximized = false };
            return true;
        }

        public bool SetBounds(nint handle, PixelRect bounds, bool positionOnly) => throw new NotSupportedException();
        public bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements) => throw new NotSupportedException();
        public void Dispose()
        {
            Continue.Set();
            if (Worker is { IsThreadPoolThread: false } thread)
            {
                thread.Join(TimeSpan.FromSeconds(5));
            }
        }
    }

    [Fact]
    public void PauseKeepsOnlyTheResumeChord()
    {
        var fixture = new Fixture();
        fixture.Runtime.SetEnabled(false);
        Assert.Equal((int)WindowCommand.ToggleEnabled, Assert.Single(fixture.Applied!).Id);
    }

    [Fact]
    public void ResumeRestoresEveryBinding()
    {
        var fixture = new Fixture();
        fixture.Runtime.SetEnabled(false);
        fixture.Runtime.SetEnabled(true);
        Assert.Equal(fixture.All, fixture.Applied);
    }

    [Fact]
    public void PausedDefaultsReleaseCtrlAltLeft()
    {
        var all = HotkeyBindingsBuilder.Build(new PortableSettings().Hotkeys).Bindings;
        var paused = HotkeyBindingsBuilder.Paused(all);
        Assert.Contains(all, binding => binding.Id == (int)WindowCommand.LeftHalf);
        Assert.DoesNotContain(paused, binding =>
            binding.Modifiers == (KeyModifiers.Control | KeyModifiers.Alt) && binding.VirtualKey == 0x25);
    }

    private sealed class Fixture
    {
        internal IReadOnlyList<HotkeyBinding> All { get; } =
            HotkeyBindingsBuilder.Build(new PortableSettings().Hotkeys).Bindings;
        internal IReadOnlyList<HotkeyBinding>? Applied;
        internal ResidentRuntime Runtime { get; }

        internal Fixture()
        {
            var inspector = new FakeInspector();
            var monitors = new FakeMonitors();
            var motion = new FakeMotion();
            var drag = new DragSnapController(inspector, monitors, motion, () => new(), _ => { }, 999);
            var hotkeys = new HotkeyCommandExecutor(inspector, monitors, motion, new NoPosition(), () => new(),
                () => true, () => { }, 999);
            Runtime = new(drag, hotkeys, motion, All, value => Applied = value);
        }
    }

    private sealed class NoPosition : IWindowPositionApi
    {
        public bool Restore(nint handle) => true;
        public bool SetBounds(nint handle, PixelRect bounds, bool positionOnly) => true;
        public bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements) => true;
    }
}
