namespace Snapwell.App.Overlay;

/// <summary>最前面の状態と、活性化しない Z オーダーの変更を分離する。</summary>
internal interface IOverlayZOrderApi
{
    bool IsTopmost { get; }
    bool HasWindowAbove { get; }
    bool TryRaise();
}

/// <summary>表示開始時は必ず、更新時は脱落を検出した場合だけ最前面を付け直す。</summary>
internal sealed class OverlayZOrder(IOverlayZOrderApi api, long generation,
    Action<long, string, Exception?> diagnostic, Func<long>? milliseconds = null)
{
    private readonly Func<long> _milliseconds = milliseconds ?? (() => Environment.TickCount64);
    private bool _visible;
    private long? _retryAfter;

    /// <summary>描画 STA から呼び、通常の表示開始ではログを増やさない。</summary>
    internal void Show()
    {
        var starting = !_visible;
        _visible = true;
        var now = _milliseconds();
        if (!starting && _retryAfter is { } retryAfter && now < retryAfter)
        {
            return;
        }

        try
        {
            var reason = starting ? null : !api.IsTopmost ? "zorder-topmost-lost" :
                api.HasWindowAbove ? "zorder-window-above" : null;
            if (!starting && reason is null)
            {
                return;
            }

            if (!api.TryRaise())
            {
                // 失敗時の再試行で描画を圧迫しない。アニメーションの時間とは無関係。
                _retryAfter = now + 250;
                return;
            }

            _retryAfter = null;
            if (reason is not null)
            {
                diagnostic(generation, reason, null);
            }
        }
        catch (Exception)
        {
            // 最前面の回復や診断の失敗で、常駐や描画スレッドを終了させない。
            _retryAfter = now + 250;
        }
    }

    /// <summary>次の表示を新しい表示開始として扱う。</summary>
    internal void Hide()
    {
        _visible = false;
        _retryAfter = null;
    }
}
