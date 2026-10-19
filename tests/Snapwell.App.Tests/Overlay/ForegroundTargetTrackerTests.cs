using Snapwell.App.Overlay;
using Snapwell.App.Tests.Motion;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Overlay;

/// <summary>トレイ操作の対象記録と、記録へ戻す条件を確かめる。</summary>
public sealed class ForegroundTargetTrackerTests
{
    /// <summary>自プロセスの前面通知は完全調査せず、直前のアプリの記録を保つ。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnProcessForegroundIsNotInspected(bool recordApplication)
    {
        var inspector = new FakeInspector();
        var tracker = new ForegroundTargetTracker(new OwnProcessInspector(inspector), 999);
        if (recordApplication)
        {
            tracker.Record(1);
        }

        tracker.Record(2);

        Assert.Equal(recordApplication ? (nint)1 : 0, tracker.Resolve(0));
    }

    /// <summary>自プロセスの指定対象は完全調査せず、記録したアプリへ戻す。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnProcessTargetIsNotInspected(bool recordApplication)
    {
        var inspector = new FakeInspector();
        var tracker = new ForegroundTargetTracker(new OwnProcessInspector(inspector), 999);
        if (recordApplication)
        {
            tracker.Record(1);
        }

        Assert.Equal(recordApplication ? (nint)1 : 0, tracker.Resolve(2));
    }

    /// <summary>記録した HWND が自プロセスに再利用されたら完全調査せず記録を捨てる。</summary>
    [Fact]
    public void RecordedHandleReusedByOwnProcessIsNotInspectedAndForgotten()
    {
        var inspector = new FakeInspector();
        var tracker = new ForegroundTargetTracker(new OwnProcessInspector(inspector), 999);
        tracker.Record(1);
        var application = inspector.Window;
        inspector.Window = application with { ProcessId = 999 };

        Assert.Equal((nint)0, tracker.Resolve(0));
        inspector.Window = application;
        Assert.Equal((nint)0, tracker.Resolve(0));
    }

    /// <summary>他プロセスの有効な指定対象は記録より優先する。</summary>
    [Fact]
    public void OtherProcessExplicitTargetTakesPrecedenceOverRecordedApplication()
    {
        var inspector = new FakeInspector();
        var tracker = new ForegroundTargetTracker(new OwnProcessInspector(inspector), 999);
        tracker.Record(1);
        inspector.Window = inspector.Window with { Handle = 3, ProcessId = 456 };

        Assert.Equal((nint)3, tracker.Resolve(3));
    }

    /// <summary>Windows 11 の溢れ窓の前面通知で直前のアプリを上書きしない。</summary>
    [Fact]
    public void XamlOverflowForegroundDoesNotReplaceRecordedApplication()
    {
        var inspector = new FakeInspector();
        var tracker = new ForegroundTargetTracker(new OverflowInspector(inspector), 999);
        tracker.Record(1);
        tracker.Record(2);

        Assert.Equal((nint)1, tracker.Resolve(0));
    }

    /// <summary>明示対象が Windows 11 の溢れ窓なら記録したアプリへ戻す。</summary>
    [Fact]
    public void ExplicitXamlOverflowTargetFallsBackToRecordedApplication()
    {
        var inspector = new FakeInspector();
        var tracker = new ForegroundTargetTracker(new OverflowInspector(inspector), 999);
        tracker.Record(1);

        Assert.Equal((nint)1, tracker.Resolve(2));
    }

    /// <summary>記録したハンドルの識別属性が変わったら記録を捨て、元に戻っても使わない。</summary>
    [Theory]
    [InlineData(456u, "Editor")]
    [InlineData(123u, "Other")]
    [InlineData(456u, "Other")]
    public void ReusedRecordedHandleIsRejectedAndForgotten(uint processId, string className)
    {
        var inspector = new FakeInspector();
        var tracker = new ForegroundTargetTracker(inspector, 999);
        tracker.Record(1);
        var application = inspector.Window;
        inspector.Window = application with { ProcessId = processId, ClassName = className };

        Assert.Equal((nint)0, tracker.Resolve(0));
        inspector.Window = application;
        Assert.Equal((nint)0, tracker.Resolve(0));
        tracker.Record(1);
        Assert.Equal((nint)1, tracker.Resolve(0));
    }

    /// <summary>識別属性が同じならタイトルや位置が変わっても記録した対象へ戻す。</summary>
    [Fact]
    public void RecordedHandleWithSameIdentityRemainsEligible()
    {
        var inspector = new FakeInspector();
        var tracker = new ForegroundTargetTracker(inspector, 999);
        tracker.Record(1);
        inspector.Window = inspector.Window with
        {
            Title = "Updated",
            VisualBounds = new(200, 200, 600, 400),
        };

        Assert.Equal((nint)1, tracker.Resolve(0));
    }

    private sealed class OwnProcessInspector(FakeInspector application) : IWindowInspector
    {
        public uint GetProcessId(nint handle) => handle == 2 ? 999u :
            application.Exists && handle == application.Window.Handle ? application.Window.ProcessId : 0;

        public bool TryInspect(nint handle,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out WindowSnapshot? snapshot)
        {
            if (GetProcessId(handle) == 999)
            {
                throw new InvalidOperationException("自プロセスのウィンドウを完全調査してはならない。");
            }

            return application.TryInspect(handle, out snapshot);
        }

        public nint GetForegroundWindow() => application.GetForegroundWindow();
        public nint GetRootWindowAt(int x, int y) => application.GetRootWindowAt(x, y);
    }

    private sealed class OverflowInspector(FakeInspector application) : IWindowInspector
    {
        public bool TryInspect(nint handle,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out WindowSnapshot? snapshot)
        {
            if (handle == 2)
            {
                snapshot = application.Window with
                {
                    Handle = handle,
                    ClassName = "TopLevelWindowForOverflowXamlIsland",
                };
                return true;
            }

            return application.TryInspect(handle, out snapshot);
        }

        public nint GetForegroundWindow() => application.GetForegroundWindow();
        public nint GetRootWindowAt(int x, int y) => application.GetRootWindowAt(x, y);
    }
}
