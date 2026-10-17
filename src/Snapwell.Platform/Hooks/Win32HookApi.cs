using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapwell.Platform.Hooks;

internal interface IHookApi
{
    uint GetThreadId();
    void CreateMessageQueue();
    nint RegisterEvent(uint eventId, nint callback);
    nint RegisterMouse(nint callback);
    nint RegisterKeyboard(nint callback);
    bool UnregisterEvent(nint hook);
    bool UnregisterLowLevel(nint hook);
    int ReadAndDispatchMessage();
    bool PostQuit(uint threadId);
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

    public int ReadAndDispatchMessage()
    {
        var result = PInvoke.GetMessage(out var message, default, 0, 0).Value;
        if (result > 0)
        {
            PInvoke.TranslateMessage(in message);
            PInvoke.DispatchMessage(in message);
        }

        return result;
    }

    public bool PostQuit(uint threadId) => PInvoke.PostThreadMessage(threadId, PInvoke.WM_QUIT, default, default);
}
