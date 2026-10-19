using Snapwell.App.Lifecycle;
using Snapwell.App.Overlay;
using Snapwell.App.Tray;
using Snapwell.App.Tray.Menu;
using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;
using Snapwell.Core.Tray;

namespace Snapwell.App.Tests.Tray;

/// <summary>ヘッダーの状態・実測幅・キャッシュ済みの画素を検証する。</summary>
public sealed class TrayMenuHeaderTests
{
    /// <summary>異常を最優先し、タスクバーと同じ状態を使う。</summary>
    [Theory]
    [InlineData(true, null, "有効")]
    [InlineData(false, null, "一時停止中")]
    [InlineData(true, "復元に失敗した", "異常")]
    [InlineData(false, "復元に失敗した", "異常")]
    public void HeaderUsesTaskbarIconPriorityAndStatusLabel(bool enabled, string? status, string label)
    {
        var snapshot = Snapshot(enabled, status, OverlayTheme.Light, 1);
        Assert.Equal(TrayIconPolicy.KindOf(new TrayState(enabled, false, status)), snapshot.IconKind);
        Assert.Equal(label, snapshot.StatusLabel);
    }

    /// <summary>最も長いタグも実測幅に左右の余白を足し、タイトルとスイッチから離す。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void MeasuredTagsFitBetweenTitleAndToggleAtEveryScale(double scale)
    {
        using var text = new GdiOverlayText(scale);
        var widths = new List<int>();
        foreach (var label in new[] { "有効", "一時停止中", "異常" })
        {
            var layout = Snapshot(true, null, OverlayTheme.Light, scale).Layout
                .WithStatusTagTextWidth(text.Measure(label, 10.5, true, true));
            var title = TrayMenuLayout.CenterVertically(layout.HeaderIcon,
                layout.Panel.X + layout.Px(49), layout.Px(110), layout.Px(24));
            Assert.True(layout.Panel.Contains(layout.StatusTag));
            Assert.False(title.Intersects(layout.StatusTag));
            Assert.True(title.Right < layout.StatusTag.X);
            Assert.Equal(layout.Px(10), layout.Toggle.X - layout.StatusTag.Right);
            Assert.Equal(layout.Px(text.Measure(label, 10.5, true, true) + 16), layout.StatusTag.Width);
            Assert.Equal(layout.StatusTag, layout.WithScrollOffset(int.MaxValue).StatusTag);
            widths.Add(layout.StatusTag.Width);
        }
        Assert.True(widths[1] > widths[0]);
    }

    /// <summary>三つの図柄とタグを明暗・倍率ごとに画素で区別し、ホバー中は同じ背景を使う。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light, 1)]
    [InlineData(OverlayTheme.Light, 1.5)]
    [InlineData(OverlayTheme.Light, 2)]
    [InlineData(OverlayTheme.Dark, 1)]
    [InlineData(OverlayTheme.Dark, 1.5)]
    [InlineData(OverlayTheme.Dark, 2)]
    public void IconsAndTagsUseStateColorsAndPausedOpacity(OverlayTheme theme, double scale)
    {
        var palette = OverlayPalette.For(theme);
        var frames = new List<byte[]>();
        var snapshots = new[] { Snapshot(true, null, theme, scale), Snapshot(false, null, theme, scale),
            Snapshot(true, "異常", theme, scale) };
        var contrasts = new List<double>();
        using var text = new GdiOverlayText(scale);
        foreach (var (snapshot, state) in snapshots.Select((value, index) => (value, index)))
        {
            using var renderer = new TrayMenuRenderer(snapshot);
            var frame = renderer.Render(-1, false);
            var icon = snapshot.Layout.HeaderIcon;
            var pixels = Pixels(frame, snapshot.Layout.Bounds, icon).ToArray();
            frames.Add(pixels.SelectMany(color => new[] { color.Red, color.Green, color.Blue }).ToArray());
            var contrast = pixels.Max(color => Math.Abs(color.Red - palette.Surface.Red)
                + Math.Abs(color.Green - palette.Surface.Green) + Math.Abs(color.Blue - palette.Surface.Blue));
            contrasts.Add(contrast);
            if (state == 0) { Assert.Contains(pixels, color => color.Blue > color.Red + 20); }
            if (state == 2) { Assert.Contains(pixels, color => color.Red > color.Blue + 20); }
            var tag = snapshot.Layout.WithStatusTagTextWidth(text.Measure(snapshot.StatusLabel, 10.5, true, true))
                .StatusTag;
            var colors = Pixels(frame, snapshot.Layout.Bounds, tag).ToArray();
            Assert.Contains(colors, color => state switch
            {
                0 => color.Green > color.Red + 10 && color.Green > color.Blue + 10,
                1 => color.Red > color.Blue + 30 && color.Green > color.Blue + 20,
                _ => color.Red > color.Green + 20 && color.Red > color.Blue + 20,
            });
            Assert.Equal(frames[^1], Pixels(renderer.Render(1, false), snapshot.Layout.Bounds, icon)
                .SelectMany(color => new[] { color.Red, color.Green, color.Blue }).ToArray());
        }
        Assert.False(frames[0].SequenceEqual(frames[1]));
        Assert.False(frames[0].SequenceEqual(frames[2]));
        Assert.False(frames[1].SequenceEqual(frames[2]));
        Assert.InRange(contrasts[1] / contrasts[0], .35, .41);
    }

    /// <summary>更新後はアイコンとタグを描き直し、異常中のスイッチだけを切り替える。</summary>
    [Fact]
    public void OpenMenuStateUpdatesInvalidateHeaderAndKeepAttentionWhilePaused()
    {
        var view = new RenderingView();
        var commands = new List<uint>();
        using var menu = new TrayMenuController(view, commands.Add, exception => Assert.Fail(exception.ToString()));
        var active = Snapshot(true, null, OverlayTheme.Light, 1);
        menu.Open(active);
        var activePixels = view.Header;
        menu.Update(Snapshot(false, null, OverlayTheme.Light, 1));
        Assert.False(activePixels.SequenceEqual(view.Header));
        var pausedPixels = view.Header;
        var attention = Snapshot(true, "異常", OverlayTheme.Light, 1);
        menu.Update(attention);
        Assert.False(pausedPixels.SequenceEqual(view.Header));
        var attentionPixels = view.Header;
        var enabledToggle = view.Toggle;
        // 状態案内の追加で本体が上へ広がるため、更新後のスイッチを押す。
        menu.Down(attention.Layout.Toggle.Center);
        menu.Up(attention.Layout.Toggle.Center);
        Assert.Equal([(uint)TrayMenuCommand.Toggle], commands);
        Assert.True(menu.IsOpen);
        menu.Update(Snapshot(false, "異常", OverlayTheme.Light, 1));
        Assert.Equal(attentionPixels, view.Header);
        Assert.False(enabledToggle.SequenceEqual(view.Toggle));
        Assert.True(menu.IsOpen);
    }

    private static TrayMenuSnapshot Snapshot(bool enabled, string? status, OverlayTheme theme, double scale) =>
        new(TrayMenuLayout.Create(new(-20, 900, 20, 20), new(-1600, -100, 1600, 1000),
            TrayTaskbarEdge.Bottom, scale, status is not null), TrayMenuModel.Create(enabled, true),
            enabled, status, 2, theme);

    private static IEnumerable<OverlayColor> Pixels(OverlayFrame frame, PixelRect bounds, PixelRect region)
    {
        for (var y = region.Y; y < region.Bottom; y++)
        {
            for (var x = region.X; x < region.Right; x++)
            {
                var index = ((y - bounds.Y) * frame.Width + x - bounds.X) * 4;
                yield return new(frame.Pixels[index + 2], frame.Pixels[index + 1], frame.Pixels[index],
                    frame.Pixels[index + 3]);
            }
        }
    }

    private sealed class RenderingView : ITrayMenuView
    {
        private TrayMenuSnapshot? _snapshot;
        private TrayMenuRenderer? _renderer;
        internal byte[] Header { get; private set; } = [];
        internal byte[] Toggle { get; private set; } = [];

        public void Show(TrayMenuSnapshot snapshot, int selected, bool keyboard)
        {
            if (!ReferenceEquals(snapshot, _snapshot))
            {
                _renderer?.Dispose();
                _renderer = new(snapshot);
                _snapshot = snapshot;
            }
            var region = snapshot.Layout.Rect(15, 15, 240, 26);
            var frame = _renderer!.Render(selected, keyboard);
            Header = Pixels(frame, snapshot.Layout.Bounds, region)
                .SelectMany(color => new[] { color.Red, color.Green, color.Blue }).ToArray();
            Toggle = Pixels(frame, snapshot.Layout.Bounds, snapshot.Layout.Toggle)
                .SelectMany(color => new[] { color.Red, color.Green, color.Blue }).ToArray();
        }

        public void Hide() { }
        public void Dispose() => _renderer?.Dispose();
    }
}
