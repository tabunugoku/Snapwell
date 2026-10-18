using Snapwell.App.Tray.Menu;
using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;
using Snapwell.Core.Tray;

namespace Snapwell.App.Tests.Tray;

/// <summary>Win32 の同期配送を偽で再現し、閉じた表示の再開を防ぐ。</summary>
public sealed class TrayMenuPresentationTests
{
    /// <summary>初回描画と再描画の途中で閉じたら、表示も捕捉も残らない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SynchronousCloseDuringPresentStopsShowingAndCapturing(bool repaint)
    {
        var platform = new FakePlatform();
        using var view = new FakeView(platform);
        using var controller = new TrayMenuController(view, _ => Assert.Fail("Unexpected command"), Fail);
        if (repaint) { controller.Open(Snapshot()); }
        platform.Callback = operation =>
        {
            if (operation == "Present") { controller.Close(); }
        };
        var captures = platform.Captures;
        if (repaint) { controller.Key(0x28); }
        else { controller.Open(Snapshot()); }
        Assert.False(controller.IsOpen);
        Assert.False(view.Presentation.IsOpen);
        Assert.False(platform.Visible);
        Assert.False(platform.HasCapture);
        Assert.Equal(captures, platform.Captures);
        platform.Callback = null;
        controller.Open(Snapshot());
        Assert.True(platform.Visible);
        Assert.True(platform.HasCapture);
        controller.Key(0x1B);
        Assert.False(platform.HasCapture);
    }

    /// <summary>活性化や捕捉の同期配送で閉じても、後続の操作へ進まない。</summary>
    [Theory]
    [InlineData("Owner")]
    [InlineData("Show")]
    [InlineData("Activate")]
    [InlineData("Foreground")]
    [InlineData("Focus")]
    [InlineData("Capture")]
    [InlineData("Name")]
    [InlineData("NotifyName")]
    public void CloseDuringNativeOperationInvalidatesTheGeneration(string operation)
    {
        var platform = new FakePlatform();
        using var view = new FakeView(platform);
        using var controller = new TrayMenuController(view, _ => { }, Fail);
        platform.Callback = step =>
        {
            if (step == operation) { controller.Close(); }
        };
        controller.Open(Snapshot());
        Assert.False(controller.IsOpen);
        Assert.False(platform.Visible);
        Assert.False(platform.HasCapture);
        Assert.DoesNotContain("NotifyFocus", platform.Operations);
    }

    /// <summary>閉じた直後に別の表示が始まっても、古い描画は再開しない。</summary>
    [Fact]
    public void CloseAndReopenDuringPresentKeepsOnlyTheNewGeneration()
    {
        var platform = new FakePlatform();
        using var view = new FakeView(platform);
        using var controller = new TrayMenuController(view, _ => { }, Fail);
        platform.Callback = operation =>
        {
            if (operation != "Present") { return; }
            platform.Callback = null;
            controller.Close();
            controller.Open(Snapshot());
        };
        controller.Open(Snapshot());
        Assert.True(controller.IsOpen);
        Assert.True(platform.Visible);
        Assert.True(platform.HasCapture);
        Assert.Equal(1, platform.Captures);
        Assert.Single(platform.Operations, operation => operation == "Show");
    }

    private static TrayMenuSnapshot Snapshot() => new(
        TrayMenuLayout.Create(new(900, 900, 24, 24), new(0, 0, 1200, 900), TrayTaskbarEdge.Bottom, 1),
        TrayMenuModel.Create(true), true, null, 1, OverlayTheme.Light);

    private static void Fail(Exception exception) => Assert.Fail(exception.ToString());

    private sealed class FakeView(FakePlatform platform) : ITrayMenuView
    {
        internal TrayMenuPresentation Presentation { get; } = new(platform);
        public void Show(TrayMenuSnapshot snapshot, int selected, bool keyboard) =>
            Presentation.Show(snapshot.Layout.Bounds, new(1, 1, new byte[4]), "Snapwell", keyboard);
        public void Hide() => Presentation.Hide();
        public void Dispose() => Hide();
    }

    private sealed class FakePlatform : ITrayMenuPresentationPlatform
    {
        internal Action<string>? Callback { get; set; }
        internal List<string> Operations { get; } = [];
        internal bool Visible { get; private set; }
        internal int Captures { get; private set; }
        public bool HasCapture { get; private set; }
        public bool IsForeground { get { Step("Foreground"); return true; } }
        public void Present(PixelRect bounds, OverlayFrame frame) => Step("Present");
        public void BringOwnerToFront() => Step("Owner");
        public void Show() { Step("Show"); Visible = true; }
        public void Activate() => Step("Activate");
        public void Focus() => Step("Focus");
        public void Capture()
        {
            Captures++;
            Step("Capture");
            // 同期配送が終わったあとに捕捉が成立する経路も再現する。
            HasCapture = true;
        }
        public void ReleaseCapture() { HasCapture = false; Step("Release"); }
        public void Hide() { Visible = false; Step("Hide"); }
        public void SetName(string name) => Step("Name");
        public void NotifyName() => Step("NotifyName");
        public void NotifyFocus() => Step("NotifyFocus");

        private void Step(string name)
        {
            Operations.Add(name);
            Callback?.Invoke(name);
        }
    }
}
