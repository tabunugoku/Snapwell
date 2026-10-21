namespace Snapwell.Platform.Hooks;

/// <summary>入力コールバックを待たせず、専用スレッドでダミーキーを送る。</summary>
internal sealed class DummyKeyDispatcher : IDisposable
{
    private readonly IDummyKeySender _sender;
    private readonly AutoResetEvent _wake = new(false);
    private int _stopped;

    internal DummyKeyDispatcher(IDummyKeySender sender)
    {
        _sender = sender;
        var thread = new Thread(Run) { IsBackground = true, Name = "Snapwell dummy key sender" };
        try
        {
            thread.Start();
        }
        catch
        {
            _wake.Dispose();
            throw;
        }
    }

    internal void Request()
    {
        if (Volatile.Read(ref _stopped) == 0)
        {
            Signal();
        }
    }

    /// <summary>送信の完了を待たず、未処理の要求を捨てて停止を依頼する。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 0)
        {
            Signal();
        }
    }

    private void Signal()
    {
        try
        {
            // AutoResetEvent の合図は一件だけ残るため、重なった要求でキューが伸びない。
            _wake.Set();
        }
        catch (ObjectDisposedException)
        {
            // 停止と競合した要求は捨てる。待機ハンドルは送信スレッドだけが破棄する。
        }
    }

    private void Run()
    {
        try
        {
            while (true)
            {
                _wake.WaitOne();
                if (Volatile.Read(ref _stopped) != 0)
                {
                    return;
                }

                try
                {
                    _sender.Send();
                }
                catch (Exception)
                {
                    // 送信失敗はフックの停止や次の要求に影響させない。
                }
            }
        }
        finally
        {
            // Send が戻らなくてもバックグラウンドスレッドなので終了処理を妨げない。
            _wake.Dispose();
        }
    }
}
