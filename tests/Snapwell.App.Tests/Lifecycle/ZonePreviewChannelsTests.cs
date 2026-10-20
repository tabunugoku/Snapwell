using Snapwell.App.Lifecycle;
using Snapwell.App.Overlay;
using Snapwell.Core.Geometry;

namespace Snapwell.App.Tests.Lifecycle;

/// <summary>ドラッグとリサイズの表示元を分けても、描画内容と所有権を保つことを確かめる。</summary>
public sealed class ZonePreviewChannelsTests
{
    /// <summary>描画に渡す起点・予測線を保ち、リサイズには余分な線を渡さない。</summary>
    [Fact]
    public void ChannelsForwardTheOriginalPreviewArguments()
    {
        var preview = new Preview();
        var channels = new ZonePreviewChannels(preview);
        var target = new PixelRect(0, 0, 800, 600);
        var origin = new Vec2(100, 100);
        var prediction = new Vec2(400, 15);
        channels.Drag.Show(target, origin, prediction);
        Assert.Equal((target, (Vec2?)origin, (Vec2?)prediction), Assert.Single(preview.Shown));
        channels.Drag.Show(target, origin);
        Assert.Equal((target, (Vec2?)origin, (Vec2?)null), preview.Shown[1]);
        channels.Resize.Show(target);
        Assert.Equal((target, (Vec2?)null, (Vec2?)null), preview.Shown[2]);
    }

    /// <summary>一方の後始末が、後から表示した他方の区画を消さない。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InactiveChannelCannotHideAnotherChannelsPreview(bool resizeLast)
    {
        var preview = new Preview();
        var channels = new ZonePreviewChannels(preview);
        var first = resizeLast ? channels.Drag : channels.Resize;
        var last = resizeLast ? channels.Resize : channels.Drag;
        first.Show(new(0, 0, 800, 600));
        last.Show(new(800, 0, 800, 600));
        first.Hide();
        Assert.Equal(0, preview.HideCalls);
        last.Hide();
        Assert.Equal(1, preview.HideCalls);
    }

    /// <summary>表示要求が例外になっても、その窓口から部分的な描画を消去できる。</summary>
    [Fact]
    public void ThrowingShowCanStillBeHiddenByItsChannel()
    {
        var preview = new Preview { ThrowOnShow = true };
        var channels = new ZonePreviewChannels(preview);
        Assert.Throws<InvalidOperationException>(() => channels.Resize.Show(new(0, 0, 800, 600)));
        channels.Resize.Hide();
        Assert.Equal(1, preview.HideCalls);
    }

    private sealed class Preview : IZonePreview
    {
        internal List<(PixelRect Target, Vec2? Origin, Vec2? Prediction)> Shown { get; } = [];
        internal int HideCalls;
        internal bool ThrowOnShow;

        public void Show(PixelRect target) => Record(target, null, null);
        public void Show(PixelRect target, Vec2 origin) => Record(target, origin, null);
        public void Show(PixelRect target, Vec2 origin, Vec2 prediction) => Record(target, origin, prediction);
        public void Hide() => HideCalls++;

        private void Record(PixelRect target, Vec2? origin, Vec2? prediction)
        {
            Shown.Add((target, origin, prediction));
            if (ThrowOnShow)
            {
                throw new InvalidOperationException("Preview unavailable");
            }
        }
    }
}
