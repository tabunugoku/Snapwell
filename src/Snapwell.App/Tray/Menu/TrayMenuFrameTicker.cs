namespace Snapwell.App.Tray.Menu;

/// <summary>STA が処理できる数だけフレームを配送し、古い世代のタイマーを停止する。</summary>
internal sealed class TrayMenuFrameTicker : IDisposable
{
    private readonly Timer _timer;
    private int _pending;

    internal TrayMenuFrameTicker(Func<bool> post)
    {
        // 16 ms は配送の間隔だけ。位置と静止は実測 dt とばねから決める。
        _timer = new Timer(_ =>
        {
            if (Interlocked.Exchange(ref _pending, 1) == 0 && !post())
            {
                Volatile.Write(ref _pending, 0);
            }
        }, null, TimeSpan.FromMilliseconds(16), TimeSpan.FromMilliseconds(16));
    }

    internal void Acknowledge() => Volatile.Write(ref _pending, 0);

    /// <summary>周期配送を止める。すでに配送されたメッセージは呼び出し側の世代で除外する。</summary>
    public void Dispose() => _timer.Dispose();
}
