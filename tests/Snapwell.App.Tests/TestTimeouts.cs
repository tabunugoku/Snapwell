namespace Snapwell.App.Tests;

/// <summary>成功を待つテストの上限。成功時は即座に返るため、負荷時に偽の失敗が出ない長さにする。</summary>
internal static class TestTimeouts
{
    /// <summary>別スレッドの処理や実描画の完了を待つ上限。</summary>
    internal static readonly TimeSpan Completion = TimeSpan.FromSeconds(30);

    /// <summary>ミリ秒を受け取る API（SpinWait.SpinUntil など）向け。</summary>
    internal static int CompletionMs => (int)Completion.TotalMilliseconds;
}
