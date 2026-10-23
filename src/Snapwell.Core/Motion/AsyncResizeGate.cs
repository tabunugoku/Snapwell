using Snapwell.Core.Geometry;

namespace Snapwell.Core.Motion;

/// <summary>非同期要求の反映を観測した結果。</summary>
public enum GateObservation
{
    NoRequest,
    Pending,
    Reflected,
    TimedOut,
}

/// <summary>矩形の変化と時計だけで非同期サイズ変更の送信と後退を判断する。</summary>
public sealed class AsyncResizeGate
{
    /// <summary>反映を待つ時間の既定値。</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>遅い反映と見なす時間の既定値。</summary>
    public static readonly TimeSpan DefaultSlowThreshold = TimeSpan.FromMilliseconds(50);

    /// <summary>後退する連続回数の既定値。</summary>
    public const int DefaultDegradeCount = 3;

    private readonly double _timeout;
    private readonly double _slowThreshold;
    private readonly int _degradeCount;
    private bool _pending;
    private double _sentAt;
    private PixelRect _before;
    private int _slowCount;

    /// <summary>反映の時間切れ、遅い反映の基準、後退する連続回数を指定する。</summary>
    public AsyncResizeGate(TimeSpan? timeout = null, TimeSpan? slowThreshold = null,
        int degradeCount = DefaultDegradeCount)
    {
        var wait = timeout ?? DefaultTimeout;
        var slow = slowThreshold ?? DefaultSlowThreshold;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(wait, TimeSpan.Zero, nameof(timeout));
        ArgumentOutOfRangeException.ThrowIfLessThan(slow, TimeSpan.Zero, nameof(slowThreshold));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(degradeCount);
        _timeout = wait.TotalSeconds;
        _slowThreshold = slow.TotalSeconds;
        _degradeCount = degradeCount;
    }

    /// <summary>このアニメーションで着地時のサイズ変更へ後退すべきか。</summary>
    public bool IsDegraded { get; private set; }

    /// <summary>未反映の要求がなく、次を送れるか。</summary>
    public bool CanSend(double now)
    {
        ValidateTime(now);
        return !_pending;
    }

    /// <summary>送信時刻と送信直前の矩形を記録する。</summary>
    public void Sent(double now, PixelRect before)
    {
        ValidateTime(now);
        if (_pending)
        {
            throw new InvalidOperationException("未反映の要求がある。");
        }

        _pending = true;
        _sentAt = now;
        _before = before;
    }

    /// <summary>送信前からの矩形の変化を観測し、反映または時間切れで要求を解除する。</summary>
    public GateObservation Observe(double now, PixelRect current)
    {
        ValidateTime(now);
        if (!_pending)
        {
            return GateObservation.NoRequest;
        }

        var elapsed = Math.Max(0, now - _sentAt);
        var reflected = current != _before;
        if (!reflected && elapsed < _timeout)
        {
            return GateObservation.Pending;
        }

        _pending = false;
        if (!reflected || elapsed > _slowThreshold)
        {
            // 後退後は数を増やさず、長時間動いても桁あふれしない。
            if (_slowCount < _degradeCount)
            {
                _slowCount++;
            }
            IsDegraded |= _slowCount >= _degradeCount;
        }
        else
        {
            _slowCount = 0;
        }

        return reflected ? GateObservation.Reflected : GateObservation.TimedOut;
    }

    /// <summary>未反映の要求と後退の履歴をすべて消す。</summary>
    public void Reset()
    {
        _pending = false;
        _sentAt = 0;
        _before = default;
        _slowCount = 0;
        IsDegraded = false;
    }

    private static void ValidateTime(double now)
    {
        if (!double.IsFinite(now))
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }
    }
}
