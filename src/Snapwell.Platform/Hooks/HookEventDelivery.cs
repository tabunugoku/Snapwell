using System.Threading.Channels;

namespace Snapwell.Platform.Hooks;

// 単一のフックスレッドから単一の配送スレッドへ渡す。満杯時は表示・破棄通知だけを採用しない。
internal sealed class HookInputBuffer
{
    private readonly int _capacity;
    private Node _head;
    private Node _tail;
    private int _count;

    internal HookInputBuffer(int capacity)
    {
        _capacity = capacity;
        _head = _tail = new(default);
    }

    internal bool Write(RawHookEvent value)
    {
        if (value.Kind is RawHookKind.WindowShown or RawHookKind.WindowDestroyed &&
            Volatile.Read(ref _count) >= _capacity)
        {
            return false;
        }

        // 重要な入力は伸長可能な SPSC キューで保持する。コールバックではロックや空き待ちをしない。
        var next = new Node(value);
        Interlocked.Increment(ref _count);
        Volatile.Write(ref _tail.Next, next);
        _tail = next;
        return true;
    }

    internal bool TryRead(out RawHookEvent value)
    {
        var next = Volatile.Read(ref _head.Next);
        if (next is null)
        {
            value = default;
            return false;
        }

        value = next.Value;
        _head = next;
        Interlocked.Decrement(ref _count);
        return true;
    }

    private sealed class Node(RawHookEvent value)
    {
        internal readonly RawHookEvent Value = value;
        internal Node? Next;
    }
}

// 配送後も重要な入力を捨てない。表示・破棄通知は読者が消費するまでの件数に上限を設ける。
internal sealed class HookEventDelivery(int capacity) : ChannelReader<HookEvent>
{
    private readonly Channel<HookEvent> _channel = Channel.CreateUnbounded<HookEvent>(new UnboundedChannelOptions
    {
        SingleWriter = true,
        AllowSynchronousContinuations = false,
    });
    private int _lifecycleCount;

    internal void Write(HookEvent value)
    {
        var lifecycle = value is WindowShown or WindowDestroyed;
        if (lifecycle)
        {
            if (Volatile.Read(ref _lifecycleCount) >= capacity)
            {
                return;
            }

            Interlocked.Increment(ref _lifecycleCount);
        }

        if (!_channel.Writer.TryWrite(value) && lifecycle)
        {
            Interlocked.Decrement(ref _lifecycleCount);
        }
    }

    internal void Complete() => _channel.Writer.TryComplete();

    /// <summary>保留中の入力がすべて消費されたときに完了する。</summary>
    public override Task Completion => _channel.Reader.Completion;

    /// <summary>消費した表示・破棄通知の枠を、次の通知へ返す。</summary>
    public override bool TryRead(out HookEvent item)
    {
        if (!_channel.Reader.TryRead(out item!))
        {
            return false;
        }

        if (item is WindowShown or WindowDestroyed)
        {
            Interlocked.Decrement(ref _lifecycleCount);
        }

        return true;
    }

    /// <summary>待機は配送先の読者で行い、フックのコールバックには持ち込まない。</summary>
    public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.WaitToReadAsync(cancellationToken);
}
