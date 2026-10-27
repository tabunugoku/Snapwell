using Snapwell.Platform.Updates;

namespace Snapwell.App.Lifecycle;

/// <summary>常駐の終了要求と、メッセージループ直前の起動確認。</summary>
internal interface IResidentUpdateSignals : IDisposable
{
    void Start(Action quit);
    void SignalReady();
}

/// <summary>Quit の待機を入力フックから切り離し、既存のトレイスレッドへ渡す。</summary>
internal sealed class ResidentUpdateSignals(string sid) : IResidentUpdateSignals
{
    private readonly UpdateSignalNames _names = new(sid);
    private EventWaitHandle? _quit;
    private RegisteredWaitHandle? _wait;

    public void Start(Action quit)
    {
        _quit = UpdateSignalNames.Create(_names.Quit);
        _wait = ThreadPool.RegisterWaitForSingleObject(_quit, (_, _) => quit(), null,
            Timeout.InfiniteTimeSpan, executeOnlyOnce: true);
    }

    public void SignalReady() => UpdateSignalNames.TrySignal(_names.Ready);

    public void Dispose()
    {
        // コールバックは Post だけで待たない。解除後に到着した Post は AppHost の終了状態で抑止する。
        // 実行中のコールバックが _quit を触らないよう、解除の完了を待ってから破棄する。
        if (_wait is not null)
        {
            using var done = new ManualResetEvent(false);
            if (_wait.Unregister(done)) { done.WaitOne(TimeSpan.FromSeconds(1)); }
        }
        _wait = null;
        _quit?.Dispose();
        _quit = null;
    }
}
