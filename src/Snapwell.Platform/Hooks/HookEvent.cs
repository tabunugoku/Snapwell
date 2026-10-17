namespace Snapwell.Platform.Hooks;

/// <summary>処理スレッドへ配送する入力イベント。</summary>
public abstract record HookEvent;

/// <summary>システムの移動・サイズ変更ループの開始。</summary>
public sealed record MoveSizeStarted(nint Handle) : HookEvent;

/// <summary>システムの移動・サイズ変更ループの終了。</summary>
public sealed record MoveSizeEnded(nint Handle, uint TimeMs) : HookEvent;

/// <summary>前面ウィンドウの変更。</summary>
public sealed record ForegroundChanged(nint Handle) : HookEvent;

/// <summary>低レベルフックで区別するマウスボタン。</summary>
public enum MouseButtonKind
{
    Left,
    Right,
    Middle,
    X1,
    X2,
}

/// <summary>物理ピクセルの位置と OS の時刻を伴うマウスボタンの変更。</summary>
public sealed record MouseButton(MouseButtonKind Button, bool IsDown, int X, int Y, uint TimeMs) : HookEvent;

/// <summary>追跡中の物理ピクセルの位置と OS の時刻。</summary>
public sealed record MouseMoved(int X, int Y, uint TimeMs) : HookEvent;

/// <summary>設定済みのホットキーとの一致。</summary>
public sealed record KeyChord(int Id) : HookEvent;
