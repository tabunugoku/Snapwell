namespace Snapwell.Platform.Hooks;

/// <summary>所有スレッドの終了で放棄される Mutex を、取り込み中の合図として読む。</summary>
public sealed class HotkeyCaptureMutex(string name) : IDisposable
{
    private readonly Mutex _mutex = new(false, name);

    /// <summary>開いてあるハンドルを待ち時間ゼロで調べ、所有中だけ取り込み中とする。</summary>
    public bool IsCapturing
    {
        get
        {
            try
            {
                if (!_mutex.WaitOne(0)) { return true; }
            }
            catch (AbandonedMutexException)
            {
                // 設定画面が異常終了しても、本体が持つハンドルに抑止状態を残さない。
            }
            _mutex.ReleaseMutex();
            return false;
        }
    }

    /// <summary>UI スレッドで所有権を取り、同じスレッドで破棄する合図を返す。</summary>
    public static IDisposable? TryAcquire(string name)
    {
        var mutex = new Mutex(false, name);
        try
        {
            try
            {
                // 本体の即時確認との一時的な競合だけを UI 側で待ち、二重起動は取り込ませない。
                if (!mutex.WaitOne(100))
                {
                    mutex.Dispose();
                    return null;
                }
            }
            catch (AbandonedMutexException)
            {
                // 前の設定画面が落ちた場合も、このスレッドが取得した所有権を解放する。
            }
            return new Lease(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    /// <summary>フック停止後に、監視用のハンドルを閉じる。</summary>
    public void Dispose() => _mutex.Dispose();

    private sealed class Lease(Mutex mutex) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}
