using System.Security.Cryptography;
using System.Text;

namespace Snapwell.Core.Settings;

/// <summary>本体とウィザードの端末状態の更新を直列化し、互いの変更を保持する。</summary>
public static class DeviceStateChanges
{
    /// <summary>ファイルごとの Mutex 内で最新値を読み、指定された項目だけを更新する。</summary>
    public static DeviceState Update(ISettingsStore<DeviceState> store, Func<DeviceState, DeviceState> update)
    {
        if (store is not SettingsStore<DeviceState> file)
        {
            return Apply(store, update);
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(file.FilePath.ToUpperInvariant())));
        using var mutex = new Mutex(false, @"Local\Snapwell.DeviceState.Write." + hash);
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(TimeSpan.FromSeconds(2));
        }
        catch (AbandonedMutexException)
        {
            // 前の書き手の異常終了後も、原子的に保存されたファイルから再開する。
            acquired = true;
        }
        if (!acquired)
        {
            throw new IOException("端末状態の保存がほかのプロセスで続いている。");
        }
        try
        {
            return Apply(store, update);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    /// <summary>有効・無効だけを書き換える。標準スナップの選択など、ほかの項目は最新値のまま残す。</summary>
    public static DeviceState SetEnabled(ISettingsStore<DeviceState> store, bool enabled) =>
        Update(store, current => current with { Enabled = enabled });

    /// <summary>Win と矢印の置き換えだけを更新し、ほかの項目は最新値を保持する。</summary>
    public static DeviceState SetReplaceWinArrow(ISettingsStore<DeviceState> store, bool replace) =>
        Update(store, current => current with { ReplaceWinArrow = replace });

    /// <summary>最新値か今回の操作に同意があるときだけ停止を保存し、同意不足は例外で返す。</summary>
    public static DeviceState SetStandardSnap(ISettingsStore<DeviceState> store, bool disable, bool consentObtained) =>
        Update(store, current =>
        {
            if (disable && !current.StandardSnapConsentGiven && !consentObtained)
            {
                throw new StandardSnapConsentRequiredException(current);
            }
            return current with
            {
                DisableStandardSnap = disable,
                StandardSnapConsentGiven = current.StandardSnapConsentGiven || (disable && consentObtained),
            };
        });

    private static DeviceState Apply(ISettingsStore<DeviceState> store, Func<DeviceState, DeviceState> update)
    {
        var loaded = store.TryReload() ?? store.Load();
        if (loaded.IsReadOnly)
        {
            throw new FutureSettingsVersionException("新しい版の端末状態は上書きできない。");
        }
        var value = update(loaded.Value);
        store.Save(value);
        return value;
    }
}

/// <summary>保存直前の同意不足と、保存されなかった最新の端末状態を返す。</summary>
public sealed class StandardSnapConsentRequiredException(DeviceState state)
    : InvalidOperationException("標準スナップの停止には同意が必要。")
{
    /// <summary>Mutex 内で読み取った、変更していない端末状態。</summary>
    public DeviceState State { get; } = state;
}
