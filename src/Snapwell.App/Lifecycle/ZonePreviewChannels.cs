using Snapwell.App.Overlay;
using Snapwell.Core.Geometry;

namespace Snapwell.App.Lifecycle;

/// <summary>同じ操作通知を受けるドラッグとリサイズが、互いの表示を消さないようにする。</summary>
internal sealed class ZonePreviewChannels
{
    private readonly IZonePreview _preview;
    private readonly object _gate = new();
    private Channel? _active;

    internal IZonePreview Drag { get; }
    internal IZonePreview Resize { get; }

    /// <summary>各表示元を一つの非同期描画先へ接続する。</summary>
    internal ZonePreviewChannels(IZonePreview preview)
    {
        _preview = preview;
        Drag = new Channel(this);
        Resize = new Channel(this);
    }

    private sealed class Channel(ZonePreviewChannels owner) : IZonePreview
    {
        /// <summary>リサイズの区画だけの表示を、そのまま描画先へ渡す。</summary>
        public void Show(PixelRect target) => ShowCore(() => owner._preview.Show(target));

        /// <summary>起点を持つ表示も、同じ描画先へ渡す。</summary>
        public void Show(PixelRect target, Vec2 origin) => ShowCore(() => owner._preview.Show(target, origin));

        /// <summary>ドラッグの起点と予測線を欠かさず描画先へ渡す。</summary>
        public void Show(PixelRect target, Vec2 origin, Vec2 prediction) =>
            ShowCore(() => owner._preview.Show(target, origin, prediction));

        private void ShowCore(Action show)
        {
            lock (owner._gate)
            {
                owner._active = this;
                show();
            }
        }

        /// <summary>最後に表示した窓口からの消去だけを描画先へ渡す。</summary>
        public void Hide()
        {
            lock (owner._gate)
            {
                if (owner._active == this)
                {
                    owner._active = null;
                    owner._preview.Hide();
                }
            }
        }
    }
}
