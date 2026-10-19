using System.Diagnostics.CodeAnalysis;
using Snapwell.App.Dragging;
using Snapwell.App.Hotkeys;
using Snapwell.App.Overlay;
using Snapwell.App.Tests.Motion;
using Snapwell.Core.Geometry;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Hotkeys;

/// <summary>「ポップアップとフライアウトも動かす」は、ドラッグ・ホットキー・ランチャーの対象だけを決める。</summary>
public sealed class PopupWindowSettingTests
{
    private const uint PopupStyle = 0x80000000;
    private const uint PopupWithCaptionStyle = 0x80C00000;

    /// <summary>ドラッグは、オンのポップアップだけを吸着させ、オフなら何もしない。</summary>
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void DragSnapsPopupOnlyWhenSettingIsOn(bool movePopup, int requests)
    {
        var inspector = new FakeInspector();
        inspector.Window = inspector.Window with { Style = PopupStyle };
        var options = new PortableSettings { MovePopupWindows = movePopup };
        var motion = new FakeMotion();
        uint now = 0;
        var controller = new DragSnapController(inspector, new FakeMonitors(), motion, () => options, _ => { }, 999,
            () => now, new NullPreview());
        controller.Handle(new MoveSizeStarted(1));
        controller.Handle(new MouseMoved(0, 0, now));
        now += 40;
        controller.Handle(new MouseMoved(100, 0, now));
        controller.Handle(new MoveSizeEnded(1, now));
        Assert.Equal(requests, motion.Requests.Count);
    }

    /// <summary>ホットキーの配置は、オフならポップアップを動かさず、オンなら動かす。通常のウィンドウは影響を受けない。</summary>
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void HotkeyPlacementMovesPopupOnlyWhenSettingIsOn(bool movePopup, int requests)
    {
        var (executor, motion) = Create(Fixture(), new PortableSettings { MovePopupWindows = movePopup });
        executor.Place(3, new(0, 0, 960, 1040), new(0, 0, 1920, 1040));
        Assert.Equal(requests, motion.Requests.Count);

        executor.Place(1, new(0, 0, 960, 1040), new(0, 0, 1920, 1040));
        Assert.Equal(requests + 1, motion.Requests.Count);
    }

    /// <summary>整列は設定にかかわらず、ポップアップを含めない。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ArrangementNeverIncludesPopupWindows(bool movePopup)
    {
        var (executor, motion) = Create(Fixture(), new PortableSettings { MovePopupWindows = movePopup });
        executor.ArrangeCurrentMonitor();
        Assert.DoesNotContain(motion.Requests, request => request.Handle == 3);
        Assert.NotEmpty(motion.Requests);
    }

    /// <summary>隣接吸着の相手は、設定にかかわらずポップアップを含めない。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NeighborCandidatesNeverIncludePopupWindows(bool movePopup)
    {
        var options = new PortableSettings { MovePopupWindows = movePopup };
        var source = new NeighborWindowSource(Fixture(), new FakeMonitors(), 999, () => options);
        Assert.Equal([new PixelRect(100, 100, 400, 300)], source.Snapshot(9));
    }

    /// <summary>ランチャーの対象は、オフならポップアップを記録せず、設定の変更を次の判定から反映する。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LauncherTargetFollowsPopupSetting(bool movePopup)
    {
        var options = new PortableSettings { MovePopupWindows = movePopup };
        var tracker = new ForegroundTargetTracker(Fixture(), 999, () => options);
        tracker.Record(3);
        Assert.Equal(movePopup ? (nint)3 : 0, tracker.Resolve(3));

        options.MovePopupWindows = !movePopup;
        Assert.Equal(movePopup ? (nint)0 : 3, tracker.Resolve(3));
    }

    /// <summary>キャプションのある WS_POPUP は通常のウィンドウとして扱い、設定の影響を受けない。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PopupStyleWithCaptionIsNotAffectedBySetting(bool movePopup)
    {
        var inspector = Fixture();
        inspector.Windows[3] = inspector.Windows[3] with { Style = PopupWithCaptionStyle };
        var (executor, motion) = Create(inspector, new PortableSettings { MovePopupWindows = movePopup });
        executor.Place(3, new(0, 0, 960, 1040), new(0, 0, 1920, 1040));
        Assert.Single(motion.Requests);
    }

    private static (HotkeyCommandExecutor Executor, FakeMotion Motion) Create(Inspector inspector,
        PortableSettings options)
    {
        var motion = new FakeMotion();
        var executor = new HotkeyCommandExecutor(inspector, new FakeMonitors(), motion, new PositionApi(),
            () => options, () => true, () => { }, 999, cursorPosition: () => new Vec2(500, 500));
        return (executor, motion);
    }

    // 1 は通常、3 はポップアップ（WS_POPUP でキャプションなし）、9 はドラッグ中の通常ウィンドウ。
    private static Inspector Fixture()
    {
        var inspector = new Inspector { Handles = [1, 3, 9] };
        inspector.Windows[1] = Window(1);
        inspector.Windows[3] = Window(3) with { Style = PopupStyle, VisualBounds = new(900, 200, 300, 200) };
        inspector.Windows[9] = Window(9) with { VisualBounds = new(1500, 600, 200, 200) };
        return inspector;
    }

    private static WindowSnapshot Window(nint handle) => new(handle, "Editor", "", 123, 0x00040000, 0,
        new(90, 90, 420, 320), new(100, 100, 400, 300), default, false, true, false, false);

    private sealed class NullPreview : IZonePreview
    {
        public void Show(PixelRect target) { }
        public void Hide() { }
    }

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
        public bool SetBounds(nint handle, PixelRect bounds, bool positionOnly) => true;
        public bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements, bool positionOnly) => true;
    }
}
