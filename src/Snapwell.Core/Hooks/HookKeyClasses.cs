namespace Snapwell.Core.Hooks;

/// <summary>キーの種類の分類。フックが生きているかの証拠に使うキーを選ぶ。</summary>
public static class HookKeyClasses
{
    /// <summary>
    /// 押している間、キーボードのコールバックが繰り返し届く通常のキーか。
    /// IME・OEM のキー (0xE5、0xF0〜0xF6 など) は、押されていないのに押下状態が続くことがあり (実機で 0xF4 を確認)、
    /// 押下状態を見ても低レベルフックの証拠にならない。ファンクションキーは F1〜F12 だけにする。0xE2 は JIS/ISO の \ キー。
    /// </summary>
    public static bool IsAutoRepeating(int virtualKey) => virtualKey is
        0x08 or 0x09 or 0x0D or (>= 0x20 and <= 0x28) or 0x2D or 0x2E or
        (>= 0x30 and <= 0x39) or (>= 0x41 and <= 0x5A) or (>= 0x60 and <= 0x6F) or (>= 0x70 and <= 0x7B) or
        (>= 0xBA and <= 0xC0) or (>= 0xDB and <= 0xDF) or 0xE2;
}
