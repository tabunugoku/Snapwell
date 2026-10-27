namespace Snapwell.Platform.Hooks;

internal enum RawHookKind
{
    MoveStart,
    MoveEnd,
    Foreground,
    WindowShown,
    WindowDestroyed,
    MouseButton,
    MouseMove,
    Key,
}

internal readonly record struct RawHookEvent(RawHookKind Kind, nint Handle = 0,
    int X = 0, int Y = 0, uint Time = 0, int Detail = 0, bool IsDown = false);
