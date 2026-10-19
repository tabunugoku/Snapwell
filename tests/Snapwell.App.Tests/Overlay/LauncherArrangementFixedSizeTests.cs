using System.Diagnostics.CodeAnalysis;
using Snapwell.App.Overlay;
using Snapwell.App.Tests.Motion;
using Snapwell.Core.Settings;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Overlay;

/// <summary>ランチャーの一覧が、設定に従ってサイズ固定のウィンドウを含めるか外す。</summary>
public sealed class LauncherArrangementFixedSizeTests
{
    /// <summary>オフなら固定サイズを一覧から外し、オンと設定の取得なしは従来どおり含める。</summary>
    [Theory]
    [InlineData(true, true, 2)]
    [InlineData(false, true, 1)]
    [InlineData(true, false, 2)]
    public void SnapshotIncludesFixedSizeWindowsOnlyWhenSettingIsOn(bool moveFixedSize, bool hasSettings, int expected)
    {
        var inspector = new Inspector { Handles = [1, 2] };
        inspector.Windows[1] = Window(1);
        inspector.Windows[2] = Window(2) with { Style = 0 };
        var options = new PortableSettings { MoveFixedSizeWindows = moveFixedSize };
        var arrangement = new LauncherArrangement(inspector, new FakeMonitors(), 999, (_, _, _) => { },
            settings: hasSettings ? () => options : null);
        Assert.Equal(expected, arrangement.Snapshot().Count);

        // 変更は次の一覧から反映する。
        options.MoveFixedSizeWindows = true;
        Assert.Equal(2, arrangement.Snapshot().Count);
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
}
