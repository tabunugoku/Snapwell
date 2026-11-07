using Snapwell.Core.Hooks;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapwell.Platform.Hooks;

/// <summary>フックのスレッドへ送る独自のスレッドメッセージ（WM_APP の範囲）。</summary>
internal static class HookMessages
{
    internal const uint Rehook = 0x8000 + 0x51;
}

internal interface IHookApi
{
    uint GetThreadId();
    void CreateMessageQueue();
    nint RegisterEvent(uint eventId, nint callback);
    nint RegisterMouse(nint callback);
    nint RegisterKeyboard(nint callback);
    bool UnregisterEvent(nint hook);
    bool UnregisterLowLevel(nint hook);
    int ReadAndDispatchMessage(out uint messageId);
    bool PostQuit(uint threadId);

    /// <summary>フックのスレッドへ、低レベルフックの張り直しを頼む。</summary>
    bool PostRehook(uint threadId);

    /// <summary>GetTickCount 相当のミリ秒。GetLastInputInfo の時刻と同じ基準。</summary>
    uint GetTickCount();

    /// <summary>OS が見た最後の入力の時刻（GetTickCount 基準）。</summary>
    bool TryGetLastInputTick(out uint tick);

    /// <summary>修飾キー・ロックキー・マウスボタン以外のキーが、今押されている。</summary>
    bool IsNonModifierKeyDown();

    /// <summary>カーソルの位置。フックを通らない SetCursorPos でも変わる。</summary>
    bool TryGetCursorPosition(out int x, out int y);
}

internal sealed unsafe class Win32HookApi : IHookApi
{
    public uint GetThreadId() => PInvoke.GetCurrentThreadId();

    public void CreateMessageQueue() => PInvoke.PeekMessage(out _, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);

    public nint RegisterEvent(uint eventId, nint callback) =>
        (nint)PInvoke.SetWinEventHook(eventId, eventId, default(HMODULE),
            (delegate* unmanaged[Stdcall]<HWINEVENTHOOK, uint, HWND, int, int, uint, uint, void>)callback,
            0, 0, PInvoke.WINEVENT_OUTOFCONTEXT | PInvoke.WINEVENT_SKIPOWNPROCESS).Value;

    public nint RegisterMouse(nint callback) =>
        (nint)PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL,
            (delegate* unmanaged[Stdcall]<int, WPARAM, LPARAM, LRESULT>)callback,
            new HINSTANCE(PInvoke.GetModuleHandle(default(PCWSTR)).Value), 0).Value;

    public nint RegisterKeyboard(nint callback) =>
        (nint)PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL,
            (delegate* unmanaged[Stdcall]<int, WPARAM, LPARAM, LRESULT>)callback,
            new HINSTANCE(PInvoke.GetModuleHandle(default(PCWSTR)).Value), 0).Value;

    public bool UnregisterEvent(nint hook) => PInvoke.UnhookWinEvent(new HWINEVENTHOOK(hook));
    public bool UnregisterLowLevel(nint hook) => PInvoke.UnhookWindowsHookEx(new HHOOK(hook));

    public int ReadAndDispatchMessage(out uint messageId)
    {
        var result = PInvoke.GetMessage(out var message, default, 0, 0).Value;
        messageId = message.message;
        // 宛先のないスレッドメッセージは、呼び出し側が処理する。送っても捨てられるだけ。
        if (result > 0 && !(message.hwnd.IsNull && message.message == HookMessages.Rehook))
        {
            PInvoke.TranslateMessage(in message);
            PInvoke.DispatchMessage(in message);
        }

        return result;
    }

    public bool PostQuit(uint threadId) => PInvoke.PostThreadMessage(threadId, PInvoke.WM_QUIT, default, default);

    public bool PostRehook(uint threadId) => PInvoke.PostThreadMessage(threadId, HookMessages.Rehook, default, default);

    public bool IsNonModifierKeyDown()
    {
        for (var key = 8; key < 0xFF; key++)
        {
            // 繰り返しの届く通常のキーだけを見る。修飾キー・ロックキー・マウスボタン・IME/OEM のキーは除く。
            if (HookKeyClasses.IsAutoRepeating(key) && (PInvoke.GetAsyncKeyState(key) & 0x8000) != 0)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryGetCursorPosition(out int x, out int y)
    {
        if (PInvoke.GetCursorPos(out var point))
        {
            x = point.X;
            y = point.Y;
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }

    public uint GetTickCount() => unchecked((uint)Environment.TickCount);

    public bool TryGetLastInputTick(out uint tick)
    {
        var info = new LASTINPUTINFO { cbSize = (uint)sizeof(LASTINPUTINFO) };
        if (PInvoke.GetLastInputInfo(ref info))
        {
            tick = info.dwTime;
            return true;
        }

        tick = 0;
        return false;
    }
}
