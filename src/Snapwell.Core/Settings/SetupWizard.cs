using System.Text.Json;
using Snapwell.Core.Commands;

namespace Snapwell.Core.Settings;

/// <summary>初回セットアップの手順。</summary>
public enum SetupStep { Welcome, StandardFeatures, Hotkeys, Grid }

/// <summary>ウィザードに表示する分割案。</summary>
public sealed record SetupPlan(GridSettings Grid, string Description);

/// <summary>キーの登録結果と設定内の重複をまとめた表示状態。</summary>
public enum SetupHotkeyStatus { Available, Conflict, Unassigned }

/// <summary>手順の遷移と保存の順序を、画面や Win32 に依存せず管理する。</summary>
public sealed class SetupWizard(ISettingsStore<PortableSettings> portableStore, ISettingsStore<DeviceState> deviceStore)
{
    public static IReadOnlyList<SetupPlan> Plans { get; } = Array.AsReadOnly<SetupPlan>([
        new(new(8, 8), "おすすめ。半分・四分割・1/4 を作れます"),
        new(new(12, 6), "1/3 や 2/3 も作れます"),
        new(new(6, 4), "大きな区画で手早く"),
    ]);

    public SetupStep Step { get; private set; }
    public int PlanIndex { get; set; }
    public double Gap { get; set; }
    public bool IsCompleted { get; private set; }
    public bool CanGoBack => !IsCompleted && Step > SetupStep.Welcome;

    /// <summary>未来の版を保護し、完了していない端末だけ案内する。</summary>
    public static bool ShouldOpen(SettingsLoadResult<DeviceState> device) =>
        !device.IsReadOnly && !device.Value.SetupCompleted;

    /// <summary>保存した同意や選択を消さず、一つ前の手順に戻る。</summary>
    public void Back()
    {
        if (CanGoBack)
        {
            Step--;
        }
    }

    /// <summary>同意は標準機能の次へ、完了フラグは設定保存の成功後にだけ書く。</summary>
    public bool TryNext(bool consent, out string? error)
    {
        error = null;
        if (IsCompleted)
        {
            return false;
        }

        try
        {
            if (Step == SetupStep.StandardFeatures)
            {
                DeviceStateChanges.Update(deviceStore, device => device with
                {
                    DisableStandardSnap = consent,
                    StandardSnapConsentGiven = consent,
                });
            }
            else if (Step == SetupStep.Grid)
            {
                // 途中で閉じた場合と二つ目の保存に失敗した場合にも、未完了を残す。
                DeviceStateChanges.Update(deviceStore, device => device with { SetupCompleted = false });
                var settings = CreateSettings(ReadWritable(portableStore), PlanIndex, Gap);
                portableStore.Save(settings);
                // 別画面やトレイが更新した、今回の選択以外の値を保持する。
                DeviceStateChanges.Update(deviceStore, device => device with { SetupCompleted = true });
                IsCompleted = true;
                return true;
            }

            Step++;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or
            InvalidOperationException or ArgumentException)
        {
            error = exception is InvalidOperationException
                ? "この設定は読み取り専用のため、保存できません。Snapwell を更新して開き直してください。"
                : "保存できませんでした。ファイルの場所とアクセス権を確認して、もう一度保存してください。";
            return false;
        }
    }

    /// <summary>ウィザードの入力だけを置き換え、ほかの設定と現在の版を保つ。</summary>
    public static PortableSettings CreateSettings(PortableSettings current, int planIndex, double gap)
    {
        if (planIndex < 0 || planIndex >= Plans.Count || !double.IsFinite(gap) || gap is < 0 or > 24)
        {
            throw new ArgumentOutOfRangeException(nameof(planIndex), "分割案または間隔が範囲外。");
        }

        return SettingsTransfer.Normalize(current with { Grid = Plans[planIndex].Grid, Gap = gap, Edge = gap });
    }

    /// <summary>設定内の重複と OS 側で使えないキーを、操作ごとの表示状態に変換する。</summary>
    public static SetupHotkeyStatus CheckHotkeys(IReadOnlyDictionary<WindowCommand, KeyChordSpec> hotkeys,
        IReadOnlyCollection<WindowCommand> commands, IReadOnlySet<KeyChordSpec> unavailable)
    {
        var conflicts = HotkeyCatalog.FindConflicts(hotkeys).SelectMany(value => value.Commands).ToHashSet();
        if (commands.Any(command => conflicts.Contains(command) ||
            hotkeys.TryGetValue(command, out var chord) && unavailable.Contains(chord)))
        {
            return SetupHotkeyStatus.Conflict;
        }

        return commands.All(hotkeys.ContainsKey) ? SetupHotkeyStatus.Available : SetupHotkeyStatus.Unassigned;
    }

    private static T ReadWritable<T>(ISettingsStore<T> store)
    {
        var loaded = store.TryReload() ?? store.Load();
        if (loaded.IsReadOnly)
        {
            throw new InvalidOperationException("新しい版の設定は上書きできない。");
        }

        return loaded.Value;
    }
}
