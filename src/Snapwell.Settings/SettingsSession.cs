using System.Text.Json;
using Snapwell.Core.Settings;

namespace Snapwell.Settings;

/// <summary>保存済みの値と編集中の値を分けて保持する。</summary>
public sealed class SettingsSession
{
    private readonly SettingsStore<PortableSettings> _store = SettingsStores.Portable();
    private PortableSettings _saved = new();
    private bool _isTransferInProgress;

    /// <summary>起動時に一度だけ設定を読み込み、読み取れない場合は保存を止める。</summary>
    public SettingsSession()
    {
        try
        {
            var result = _store.Load();
            _saved = SettingsTransfer.Normalize(result.Value);
            Current = SettingsTransfer.Normalize(_saved);
            IsReadOnly = result.IsReadOnly;
            LoadMessage = result.IsReadOnly
                ? "新しい版の設定のため、この画面では保存できません。Snapwell を更新してください。"
                : result.ResetToDefaults ? "設定ファイルが壊れていたため、既定値を表示しています。" : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            IsReadOnly = true;
            LoadMessage = "設定ファイルを読み取れません。ファイルの場所とアクセス権を確認して、開き直してください。";
        }
    }

    public PortableSettings Current { get; private set; } = new();
    public bool HasChanges { get; private set; }
    public bool IsReadOnly { get; private set; }
    public bool IsTransferInProgress
    {
        get => _isTransferInProgress;
        set
        {
            _isTransferInProgress = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
    public string? LoadMessage { get; }
    public event EventHandler? Changed;

    /// <summary>検証は Core に任せ、保存せずに画面の値だけを更新する。</summary>
    public void Update(PortableSettings value)
    {
        if (IsReadOnly)
        {
            return;
        }

        Current = SettingsTransfer.Normalize(value);
        HasChanges = SettingsTransfer.HasChanges(_saved, Current);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>編集中の値を既存ストアで保存し、失敗理由を表示用に返す。</summary>
    public bool TrySave(out string message)
    {
        if (IsReadOnly)
        {
            message = "この設定は読み取り専用のため、保存できません。";
            return false;
        }

        try
        {
            // 本体は読み込みだけを行う。保存はストアの原子的な置換と未来の版の保護に任せる。
            _store.Save(Current);
            _saved = SettingsTransfer.Normalize(Current);
            HasChanges = false;
            message = "保存しました。Snapwell が起動していれば、すぐに反映されます。";
        }
        catch (FutureSettingsVersionException)
        {
            IsReadOnly = true;
            message = "新しい版の設定に変わったため、保存できません。Snapwell を更新して開き直してください。";
            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or
            InvalidOperationException)
        {
            message = "保存できませんでした。ファイルの場所とアクセス権を確認して、もう一度保存してください。";
            return false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
