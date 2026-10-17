namespace Snapwell.Platform.Hooks;

internal enum RawHookKind
{
    MoveStart,
    MoveEnd,
    Foreground,
    MouseButton,
    MouseMove,
    Key,
}

internal readonly record struct RawHookEvent(RawHookKind Kind, nint Handle = 0,
    int X = 0, int Y = 0, uint Time = 0, int Detail = 0, bool IsDown = false);

// 単一のフックスレッドが書き、単一の配送スレッドが読む。遅れた読者は古いイベントを捨てる。
internal sealed class HookEventBuffer(int capacity)
{
    private readonly Slot[] _slots = new Slot[capacity];
    private long _written;
    private long _next = 1;

    internal void Write(RawHookEvent value)
    {
        var sequence = _written + 1;
        ref var slot = ref _slots[(int)(sequence % _slots.Length)];
        // 書き始めの完全フェンスと読み終わりの完全フェンスで、構造体の途中の値を採用させない。
        Interlocked.Exchange(ref slot.Sequence, -sequence);
        slot.Value = value;
        Volatile.Write(ref slot.Sequence, sequence);
        Volatile.Write(ref _written, sequence);
    }

    internal bool TryRead(out RawHookEvent value)
    {
        var written = Volatile.Read(ref _written);
        _next = Math.Max(_next, written - _slots.Length + 1);
        value = default;
        if (_next > written)
        {
            return false;
        }

        ref var slot = ref _slots[(int)(_next % _slots.Length)];
        if (Volatile.Read(ref slot.Sequence) != _next)
        {
            return false;
        }

        var candidate = slot.Value;
        if (Interlocked.CompareExchange(ref slot.Sequence, 0, 0) != _next)
        {
            return false;
        }

        value = candidate;
        _next++;
        return true;
    }

    private struct Slot
    {
        internal long Sequence;
        internal RawHookEvent Value;
    }
}
