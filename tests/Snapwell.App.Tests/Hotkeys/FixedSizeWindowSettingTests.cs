using System.Diagnostics.CodeAnalysis;
using Snapwell.App.Hotkeys;
using Snapwell.App.Tests.Motion;
using Snapwell.Core.Geometry;
using Snapwell.Core.Settings;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Hotkeys;

/// <summary>「サイズ固定のウィンドウも動かす」がオフのとき、整列とランチャー経由の配置から固定サイズが外れる。</summary>
public sealed class FixedSizeWindowSettingTests
{
    /// <summary>オフなら固定サイズを整列の対象にせず、オンなら従来どおり通常と一緒に並べる。</summary>
    [Theory]
    [InlineData(true, new[] { 1, 2 })]
    [InlineData(false, new[] { 1 })]
    public void ArrangeCurrentMonitorSkipsFixedSizeWindowsOnlyWhenSettingIsOff(bool moveFixedSize, int[] expected)
    {
        var inspector = FixtureInspector();
        var (executor, motion) = Create(inspector, new PortableSettings { MoveFixedSizeWindows = moveFixedSize });
        executor.ArrangeCurrentMonitor();
        Assert.Equal(expected, motion.Requests.Select(request => (int)request.Handle).Order());
    }

    /// <summary>設定の変更は次の操作から反映し、再作成を要らない。</summary>
    [Fact]
    public void SettingChangeAppliesToTheNextArrangement()
    {
        var options = new PortableSettings { MoveFixedSizeWindows = false };
        var (executor, motion) = Create(FixtureInspector(), options);
        executor.ArrangeCurrentMonitor();
        Assert.Equal([1], motion.Requests.Select(request => (int)request.Handle));

        options.MoveFixedSizeWindows = true;
        motion.Requests.Clear();
        executor.ArrangeCurrentMonitor();
        Assert.Equal([1, 2], motion.Requests.Select(request => (int)request.Handle).Order());
    }

    /// <summary>ランチャーなどの配置の入口でも、オフなら固定サイズを動かさず、通常のウィンドウは動かす。</summary>
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void PlaceMovesFixedSizeWindowOnlyWhenSettingIsOn(bool moveFixedSize, int requests)
    {
        var options = new PortableSettings { MoveFixedSizeWindows = moveFixedSize };
        var (executor, motion) = Create(FixtureInspector(), options);
        executor.Place(2, new(0, 0, 960, 1040), new(0, 0, 1920, 1040));
        Assert.Equal(requests, motion.Requests.Count);

        executor.Place(1, new(0, 0, 960, 1040), new(0, 0, 1920, 1040));
        Assert.Equal(requests + 1, motion.Requests.Count);
    }

    private static (HotkeyCommandExecutor Executor, FakeMotion Motion) Create(Inspector inspector,
        PortableSettings options)
    {
        var motion = new FakeMotion();
        var executor = new HotkeyCommandExecutor(inspector, new FakeMonitors(), motion, new PositionApi(),
            () => options, () => true, () => { }, 999, cursorPosition: () => new Vec2(500, 500));
        return (executor, motion);
    }

    // 1 は通常（サイズ変更の枠あり）、2 は枠のないサイズ固定のウィンドウ。
    private static Inspector FixtureInspector()
    {
        var inspector = new Inspector { Handles = [1, 2] };
        inspector.Windows[1] = Window(1);
        inspector.Windows[2] = Window(2) with { Style = 0, VisualBounds = new(900, 200, 300, 200) };
        return inspector;
    }

    private static WindowSnapshot Window(nint handle) => new(handle, "Editor", "", 123, 0x00040000, 0,
        new(90, 90, 420, 320), new(100, 100, 400, 300), default, false, true, false, false);

    private sealed class Inspector : IWindowInspector
    {
        internal Dictionary<nint, WindowSnapshot> Windows { get; } = [];
        internal IReadOnlyList<nint> Handles = [];

        public bool TryInspect(nint handle, [NotNullWhen(true)] out WindowSnapshot? snapshot) =>
            Windows.TryGetValue(handle, out snapshot);

        public nint GetForegroundWindow() => 1;
        public nint GetRootWindowAt(int x, int y) => 0;
        public IReadOnlyList<nint> GetTopLevelWindows() => Handles;
        public nint GetOwner(nint handle) => 0;
    }

    private sealed class PositionApi : IWindowPositionApi
    {
        public bool Restore(nint handle) => true;
        /// <summary>既存の同期経路では非同期送信を許さない。</summary>
        public bool SetBoundsAsync(nint handle, PixelRect bounds) => throw new NotSupportedException();

        public bool SetBounds(nint handle, PixelRect bounds, bool positionOnly) => true;
        public bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements, bool positionOnly) => true;
    }
}
