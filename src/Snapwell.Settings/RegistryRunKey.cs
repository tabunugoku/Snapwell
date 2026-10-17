using Microsoft.Win32;
using Snapwell.Core.Settings;

namespace Snapwell.Settings;

/// <summary>自動起動の登録先をこのファイルだけに閉じ込める。</summary>
internal sealed class RegistryRunKey : IRunKey
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>登録を読み取り、値が無ければ null を返す。</summary>
    public string? Read(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(name) switch
        {
            null => null,
            string value => value,
            _ => string.Empty,
        };
    }

    /// <summary>利用者がオンにした登録だけを現在のパスで置き換える。</summary>
    public void Write(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    /// <summary>利用者がオフにした登録を削除する。</summary>
    public void Delete(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
