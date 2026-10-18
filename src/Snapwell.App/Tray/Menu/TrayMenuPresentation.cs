using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;

namespace Snapwell.App.Tray.Menu;

/// <summary>同期メッセージで再入するネイティブ操作の境界。</summary>
internal interface ITrayMenuPresentationPlatform
{
    void Present(PixelRect bounds, OverlayFrame frame);
    void BringOwnerToFront();
    void Show();
    void Activate();
    bool IsForeground { get; }
    void Focus();
    void Capture();
    bool HasCapture { get; }
    void ReleaseCapture();
    void Hide();
    void SetName(string name);
    void NotifyName();
    void NotifyFocus();
}

/// <summary>表示の世代を使い、同期的に閉じられた処理の続行を防ぐ。</summary>
internal sealed class TrayMenuPresentation(ITrayMenuPresentationPlatform platform)
{
    private int _generation;
    private string _name = "";

    internal bool IsOpen { get; private set; }

    internal void Show(PixelRect bounds, OverlayFrame frame, string name, bool keyboard)
    {
        var opening = !IsOpen;
        if (opening)
        {
            _generation++;
            IsOpen = true;
        }

        var generation = _generation;
        platform.Present(bounds, frame);
        if (!IsCurrent(generation)) { return; }
        if (opening)
        {
            platform.BringOwnerToFront();
            if (!IsCurrent(generation)) { return; }
            platform.Show();
            if (!IsCurrent(generation)) { return; }
            platform.Activate();
            if (!IsCurrent(generation)) { return; }
            var foreground = platform.IsForeground;
            if (!IsCurrent(generation)) { return; }
            if (!foreground) { throw new InvalidOperationException("メニューを前面にできない。"); }
            platform.Focus();
            if (!IsCurrent(generation)) { return; }
            platform.Capture();
            if (!IsCurrent(generation)) { return; }

            var captured = platform.HasCapture;
            if (!IsCurrent(generation)) { return; }
            if (!captured) { throw new InvalidOperationException("メニューのマウス入力を捕捉できない。"); }
        }

        if (_name != name)
        {
            _name = name;
            platform.SetName(name);
            if (!IsCurrent(generation)) { return; }
            platform.NotifyName();
            if (!IsCurrent(generation)) { return; }
            if (keyboard) { platform.NotifyFocus(); }
        }
    }

    internal void Hide()
    {
        // ReleaseCapture と ShowWindow が同期配送する前に世代を無効にする。
        _generation++;
        IsOpen = false;
        _name = "";
        if (platform.HasCapture) { platform.ReleaseCapture(); }
        platform.Hide();
    }

    private bool IsCurrent(int generation)
    {
        if (IsOpen && _generation == generation) { return true; }
        if (!IsOpen)
        {
            // ShowWindow / SetCapture の復帰時点で成立した表示や捕捉も回収する。
            // 別世代が開いている場合は、その表示には触れない。
            if (platform.HasCapture) { platform.ReleaseCapture(); }
            platform.Hide();
        }

        return false;
    }
}
