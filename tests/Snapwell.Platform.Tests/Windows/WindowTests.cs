using System.Diagnostics.CodeAnalysis;
using Snapwell.Core.Geometry;
using Snapwell.Platform.Windows;

namespace Snapwell.Platform.Tests.Windows;

/// <summary>分類・枠補正・移動の状態判断の検証。</summary>
public sealed class WindowTests
{
    private const uint Resizable = 0x00040000;
    private const uint Caption = 0x00C00000;

    /// <summary>スタイルの組み合わせに対する分類を確認する。</summary>
    [Theory]
    [InlineData(Resizable, 0u, WindowKind.Normal)]
    [InlineData(0u, 0u, WindowKind.FixedSize)]
    [InlineData(0x80000000u, 0u, WindowKind.Popup)]
    [InlineData(0x80040000u, 0u, WindowKind.Popup)]
    [InlineData(0x80C40000u, 0u, WindowKind.Normal)]
    [InlineData(0x80C00000u, 0u, WindowKind.FixedSize)]
    [InlineData(Resizable, 0x80u, WindowKind.Popup)]
    [InlineData(0x40040000u, 0u, WindowKind.Excluded)]
    public void ClassifierUsesStylePrecedence(uint style, uint extendedStyle, WindowKind expected)
    {
        var window = CreateWindow() with { Style = style, ExtendedStyle = extendedStyle };
        Assert.Equal(expected, WindowClassifier.Classify(window, 99));
        Assert.Equal(expected == WindowKind.Normal, WindowClassifier.CanResize(window, 99));
    }

    /// <summary>シェルのクラスだけを除外し、UWP のフレームを残す。</summary>
    [Theory]
    [InlineData("Progman", WindowKind.Excluded)]
    [InlineData("WorkerW", WindowKind.Excluded)]
    [InlineData("Shell_TrayWnd", WindowKind.Excluded)]
    [InlineData("Shell_SecondaryTrayWnd", WindowKind.Excluded)]
    [InlineData("Windows.UI.Core.CoreWindow", WindowKind.Excluded)]
    [InlineData("ApplicationFrameWindow", WindowKind.Normal)]
    public void ClassifierRecognizesShellAndUwpClasses(string className, WindowKind expected) =>
        Assert.Equal(expected, WindowClassifier.Classify(
            CreateWindow() with { ClassName = className, Style = 0 }, 99));

    /// <summary>表示状態と自プロセスの除外はポップアップより優先する。</summary>
    [Fact]
    public void ClassifierExcludesUnavailableAndOwnWindows()
    {
        var window = CreateWindow() with { ExtendedStyle = 0x80 };
        Assert.Equal(WindowKind.Excluded, WindowClassifier.Classify(window with { IsVisible = false }, 99));
        Assert.Equal(WindowKind.Excluded, WindowClassifier.Classify(window with { IsCloaked = true }, 99));
        Assert.Equal(WindowKind.Excluded, WindowClassifier.Classify(window with { IsMinimized = true }, 99));
        Assert.Equal(WindowKind.Excluded, WindowClassifier.Classify(window, window.ProcessId));
    }

    /// <summary>実測した枠幅で見た目の矩形を往復変換する。</summary>
    [Fact]
    public void FrameInsetsRoundTripMeasuredBounds()
    {
        var visual = new PixelRect(100, 100, 800, 600);
        var native = new PixelRect(93, 100, 814, 607);
        var insets = FrameInsets.FromBounds(native, visual);
        Assert.Equal(new FrameInsets(7, 0, 7, 7), insets);
        Assert.Equal(native, insets.ToWindowBounds(visual));
        Assert.Equal(visual, insets.ToVisualBounds(native));
        Assert.Equal(default, FrameInsets.FromBounds(visual, native));
        Assert.Equal(visual, new FrameInsets(-7, 0, 7, 7).ToWindowBounds(visual));
    }

    /// <summary>最大化を解除して通常状態の枠を取り直し、位置だけを指定する。</summary>
    [Fact]
    public void MoveRestoresBeforeReadingInsetsAndPreservesSize()
    {
        var inspector = new FakeInspector { Current = CreateWindow() with { IsMaximized = true, Insets = default } };
        var api = new FakePositionApi(inspector);
        var mover = new WindowMover(inspector, api);
        Assert.True(mover.MoveVisual(1, -100, 200));
        Assert.Equal(1, api.RestoreCount);
        Assert.True(api.PositionOnly);
        Assert.Equal(new PixelRect(-107, 200, 814, 607), api.LastBounds);
    }

    /// <summary>適用後のサイズを返し、API の失敗を値として返す。</summary>
    [Fact]
    public void SetBoundsReturnsActualSizeAndReportsFailures()
    {
        var inspector = new FakeInspector { Current = CreateWindow() };
        var api = new FakePositionApi(inspector);
        var mover = new WindowMover(inspector, api);
        var requested = new PixelRect(-100, 0, 30, 40);
        Assert.Equal(inspector.Current.VisualBounds, mover.SetVisualBounds(1, requested));
        Assert.False(api.PositionOnly);
        Assert.Equal(new PixelRect(-107, 0, 44, 47), api.LastBounds);
        api.Success = false;
        Assert.Null(mover.SetVisualBounds(1, requested));
        inspector.Current = null;
        Assert.False(mover.MoveVisual(1, 0, 0));
    }

    /// <summary>バッチにも各ウィンドウの枠を加え、空のバッチは成功する。</summary>
    [Fact]
    public void BatchCorrectsInsetsAndHandlesEmptyInput()
    {
        var inspector = new FakeInspector { Current = CreateWindow() };
        var api = new FakePositionApi(inspector);
        var mover = new WindowMover(inspector, api);
        Assert.True(mover.ApplyBatch([]));
        Assert.True(mover.ApplyBatch([new(1, new PixelRect(100, 100, 800, 600))]));
        Assert.Equal(new PixelRect(93, 100, 814, 607), Assert.Single(api.Batch).WindowBounds);
        inspector.Current = null;
        Assert.False(mover.ApplyBatch([new(1, default)]));
    }

    private static WindowSnapshot CreateWindow() => new(1, "Example", "Test", 42,
        Resizable | Caption, 0, new PixelRect(93, 100, 814, 607), new PixelRect(100, 100, 800, 600),
        new FrameInsets(7, 0, 7, 7), false, true, false, false);

    private sealed class FakeInspector : IWindowInspector
    {
        internal WindowSnapshot? Current { get; set; }

        public bool TryInspect(nint handle, [NotNullWhen(true)] out WindowSnapshot? snapshot)
        {
            snapshot = Current;
            return snapshot is not null;
        }

        public nint GetForegroundWindow() => 1;
        public nint GetRootWindowAt(int x, int y) => 1;
    }

    private sealed class FakePositionApi(FakeInspector inspector) : IWindowPositionApi
    {
        internal int RestoreCount { get; private set; }
        internal bool PositionOnly { get; private set; }
        internal PixelRect LastBounds { get; private set; }
        internal IReadOnlyList<NativeWindowPlacement> Batch { get; private set; } = [];
        internal bool Success { get; set; } = true;

        public bool Restore(nint handle)
        {
            RestoreCount++;
            inspector.Current = CreateWindow();
            return Success;
        }

        public bool SetBounds(nint handle, PixelRect bounds, bool positionOnly)
        {
            LastBounds = bounds;
            PositionOnly = positionOnly;
            return Success;
        }

        public bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements)
        {
            Batch = placements;
            return Success;
        }
    }
}
