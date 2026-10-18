using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;
using Snapwell.Core.Tray;

namespace Snapwell.App.Tray.Menu;

/// <summary>表示を始める時点の、設定を推測しないスナップショット。</summary>
internal sealed record TrayMenuSnapshot(TrayMenuLayout Layout, IReadOnlyList<TrayMenuEntry> Items,
    bool Enabled, string? Status, int MonitorCount, OverlayTheme Theme);

/// <summary>ネイティブの描画と入力を差し替える境界。</summary>
internal interface ITrayMenuView : IDisposable
{
    void Show(TrayMenuSnapshot snapshot, int selected, bool keyboard);
    void Hide();
}

/// <summary>表示・選択・終了を同じトレイ STA 上で扱う。</summary>
internal sealed class TrayMenuController(ITrayMenuView view, Action<uint> command, Action<Exception> error)
    : IDisposable
{
    private TrayMenuSnapshot? _snapshot;
    private int _selected = -1;
    private int _pressed = -1;
    private bool _keyboard;
    private long _wheelRemainder;

    internal bool IsOpen => _snapshot is not null;

    internal void Open(TrayMenuSnapshot snapshot)
    {
        _snapshot = snapshot;
        _selected = -1;
        _pressed = -1;
        _keyboard = false;
        _wheelRemainder = 0;
        Paint();
    }

    internal void Update(TrayMenuSnapshot snapshot)
    {
        if (!IsOpen)
        {
            return;
        }

        _snapshot = _snapshot is { } previous
            ? snapshot with { Layout = snapshot.Layout.WithScrollOffset(previous.Layout.ScrollOffset) } : snapshot;
        if (TrayMenuModel.CommandAt(snapshot.Items, _selected) is null)
        {
            _selected = _keyboard ? TrayMenuModel.Move(snapshot.Items, _selected, 1) : -1;
        }

        _pressed = -1;
        Paint();
    }

    internal void Wheel(int delta)
    {
        if (_snapshot is not { } snapshot || delta == 0) { return; }
        if (Math.Sign(_wheelRemainder) != Math.Sign(delta)) { _wheelRemainder = 0; }
        // 120 分の 1 px 単位で保持し、細粒度の入力でも端数を失わない。
        var movement = (long)delta * snapshot.Layout.Px(32) + _wheelRemainder;
        _wheelRemainder = movement % 120;
        var offset = (int)Math.Clamp(snapshot.Layout.ScrollOffset - movement / 120,
            0, snapshot.Layout.MaxScrollOffset);
        if ((offset == 0 && delta > 0) || (offset == snapshot.Layout.MaxScrollOffset && delta < 0))
        {
            _wheelRemainder = 0;
        }

        var layout = snapshot.Layout.WithScrollOffset(offset);
        if (layout.ScrollOffset == snapshot.Layout.ScrollOffset) { return; }
        _snapshot = snapshot with { Layout = layout };
        _selected = -1;
        _pressed = -1;
        _keyboard = false;
        Paint();
    }

    internal void Move(Vec2 point)
    {
        if (_snapshot is not { } snapshot)
        {
            return;
        }

        var selected = TrayMenuModel.HitTest(snapshot.Layout, snapshot.Items, point);
        if (_selected == selected && !_keyboard)
        {
            return;
        }

        _selected = selected;
        _keyboard = false;
        Paint();
    }

    internal void Down(Vec2 point)
    {
        if (_snapshot is not { } snapshot)
        {
            return;
        }

        if (!snapshot.Layout.Panel.Contains(point))
        {
            Close();
            return;
        }

        Move(point);
        _pressed = _selected;
    }

    internal void Up(Vec2 point)
    {
        var pressed = _pressed;
        _pressed = -1;
        Move(point);
        if (pressed >= 0 && pressed == _selected)
        {
            Select();
        }
    }

    internal void Key(uint key)
    {
        if (_snapshot is not { } snapshot)
        {
            return;
        }

        if (key == 0x1B)
        {
            Close();
        }
        else if (key is 0x26 or 0x28 or 0x25 or 0x27 or 0x24 or 0x23)
        {
            var from = key == 0x24 ? -1 : key == 0x23 ? 0 : _selected;
            _selected = TrayMenuModel.Move(snapshot.Items, from, key is 0x26 or 0x25 or 0x23 ? -1 : 1);
            _keyboard = true;
            Paint();
        }
        else if (key is 0x0D or 0x20)
        {
            Select();
        }
    }

    internal void Close()
    {
        _snapshot = null;
        _selected = -1;
        _pressed = -1;
        try
        {
            view.Hide();
        }
        catch (Exception exception)
        {
            error(exception);
        }
    }

    private void Select()
    {
        if (_snapshot is not { } snapshot || TrayMenuModel.CommandAt(snapshot.Items, _selected) is not { } id)
        {
            return;
        }

        // ヘッダーのスイッチだけは開いたまま状態更新を待つ。
        if (_selected != 0)
        {
            Close();
        }

        command((uint)id);
    }

    private void Paint()
    {
        if (_snapshot is not { } snapshot)
        {
            return;
        }

        try
        {
            if (_keyboard)
            {
                var layout = snapshot.Layout.EnsureVisible(_selected);
                if (!ReferenceEquals(layout, snapshot.Layout))
                {
                    snapshot = snapshot with { Layout = layout };
                    _snapshot = snapshot;
                }
            }

            view.Show(snapshot, _selected, _keyboard);
        }
        catch (Exception exception)
        {
            Close();
            error(exception);
        }
    }

    public void Dispose()
    {
        Close();
        try
        {
            view.Dispose();
        }
        catch (Exception exception)
        {
            error(exception);
        }
    }
}
