using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace Snapwell.Core.Commands;

/// <summary>取り込んだキーを、継続・取り消し・削除・確定に分類する。</summary>
public enum HotkeyCaptureInput
{
    Ignored, Modifier, Invalid, Cancelled, Cleared, Accepted,
}

/// <summary>キー取り込みと合図の寿命を一対にし、UI に依存せず終了を判断する。</summary>
public sealed class HotkeyCaptureSession(Func<IDisposable?> acquireSignal) : IDisposable
{
    private IDisposable? _signal;

    public WindowCommand? Command { get; private set; }

    /// <summary>同じユーザーの本体と設定画面が共有する、セッション内の Mutex 名を返す。</summary>
    public static string MutexName(string userSid) => @"Local\Snapwell.HotkeyCapture." +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userSid)));

    /// <summary>前の取り込みを終了し、ほかの設定画面が取り込み中でなければ開始する。</summary>
    public bool Begin(WindowCommand command)
    {
        End();
        try
        {
            _signal = acquireSignal();
            if (_signal is null) { return false; }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            WaitHandleCannotBeOpenedException or SecurityException or InvalidOperationException)
        {
            // 本体が動いていない環境でも、合図の作成失敗で編集を使えなくしない。
        }
        Command = command;
        return true;
    }

    /// <summary>確定する入力だけで合図を終了し、不完全な入力では取り込みを続ける。</summary>
    public HotkeyCaptureInput ReadKey(uint key, ChordModifiers modifiers)
    {
        if (Command is null) { return HotkeyCaptureInput.Ignored; }
        HotkeyCaptureInput result;
        if (key == 0x1B) { result = HotkeyCaptureInput.Cancelled; }
        else if (key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5))
        {
            return HotkeyCaptureInput.Modifier;
        }
        else if (modifiers == ChordModifiers.None && key is 0x08 or 0x2E)
        {
            result = HotkeyCaptureInput.Cleared;
        }
        else if (!HotkeyEditing.IsAssignable(new(modifiers, key))) { return HotkeyCaptureInput.Invalid; }
        else { result = HotkeyCaptureInput.Accepted; }
        End();
        return result;
    }

    /// <summary>取り消し・フォーカス離脱・非活性化・ページ離脱で合図を終了する。</summary>
    public void End()
    {
        Command = null;
        var signal = _signal;
        _signal = null;
        signal?.Dispose();
    }

    /// <summary>ページの寿命が終わった場合も、二重解放せず合図を終了する。</summary>
    public void Dispose() => End();
}
