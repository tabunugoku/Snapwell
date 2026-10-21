using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Snapwell.Platform.Hooks;

/// <summary>VK_NONAME を二つのスタック上の入力イベントとして送る。</summary>
internal sealed class Win32DummyKeySender : IDummyKeySender
{
    /// <summary>入力を一度だけ送り、失敗しても再試行でフックを待たせない。</summary>
    public unsafe bool Send()
    {
        var inputs = stackalloc INPUT[2];
        inputs[0] = new INPUT
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                ki = new KEYBDINPUT { wVk = VIRTUAL_KEY.VK_NONAME },
            },
        };
        inputs[1] = inputs[0];
        inputs[1].Anonymous.ki.dwFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
        return PInvoke.SendInput(2, inputs, sizeof(INPUT)) == 2;
    }
}
