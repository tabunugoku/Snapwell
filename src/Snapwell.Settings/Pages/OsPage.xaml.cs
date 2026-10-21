using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Snapwell.Core.Settings;
using Snapwell.Platform.SystemSettings;

namespace Snapwell.Settings.Pages;

/// <summary>Windows の標準スナップの停止と復元を端末状態に保存する。</summary>
public sealed partial class OsPage : Page
{
    private readonly ISettingsStore<DeviceState> _deviceStore = SettingsStores.Device();
    private DeviceState _state = new();
    private bool? _savedDragSnap;
    private bool _readOnly = true;
    private bool _syncing = true;
    private bool _busy;

    /// <summary>ページを開くたびに端末状態と退避値を読み直す。</summary>
    public OsPage()
    {
        InitializeComponent();
        _syncing = false;
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        ErrorNotice.IsOpen = false;
        try
        {
            ReadState();
            if (_readOnly)
            {
                ReportFailure("新しい版の端末状態のため、標準のスナップを変更できません。Snapwell を更新してください。");
            }
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            _readOnly = true;
            ReportFailure("端末状態を読み取れません。ファイルの場所とアクセス権を確認して、開き直してください。");
        }
        Render();
    }

    private void ReadState()
    {
        var loaded = _deviceStore.Load();
        _state = loaded.Value;
        _readOnly = loaded.IsReadOnly;
        _savedDragSnap = SnapSettingsGuard.ReadSavedDragSnap();
    }

    // 表示用の IsOn 更新を保存として扱わず、一時停止中も端末に保存した選択を表示する。
    private void Render()
    {
        var view = StandardSnapView.Create(_state, _savedDragSnap);
        var previous = _syncing;
        _syncing = true;
        try
        {
            StandardSnapToggle.IsOn = view.ToggleOn;
            StandardSnapToggle.IsEnabled = !_readOnly && !_busy;
            RestoreButton.IsEnabled = view.RestoreEnabled && !_readOnly && !_busy;
            SetTag(DragSnapTag, DragSnapTagText, view.ToggleOn ? "停止中" : "標準のまま", view.ToggleOn);
            SetTag(LayoutTag, LayoutTagText, view.SubTagText, view.ToggleOn);
            SetTag(SnapBarTag, SnapBarTagText, view.SubTagText, view.ToggleOn);
            ReplaceWinArrowToggle.IsOn = _state.ReplaceWinArrow;
            ReplaceWinArrowToggle.IsEnabled = !_readOnly && !_busy;
            SetTag(WinArrowTag, WinArrowTagText, _state.ReplaceWinArrow ? "置き換え中" : "標準のまま",
                _state.ReplaceWinArrow);
        }
        finally
        {
            _syncing = previous;
        }
    }

    private static void SetTag(Border border, TextBlock text, string label, bool stopped)
    {
        var resources = Application.Current.Resources;
        border.Style = (Style)resources[stopped ? "SnapwellTagGreenStyle" : "SnapwellTagPendingStyle"];
        text.Style = (Style)resources[stopped ? "SnapwellTagGreenTextStyle" : "SnapwellTagTextStyle"];
        text.Text = label;
    }

    private void StandardSnap_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncing || _busy)
        {
            return;
        }

        var disable = StandardSnapToggle.IsOn;
        _busy = true;
        // Toggled が戻った後で表示を更新し、NativeAOT のアニメーション再入を避ける。
        DispatcherQueue.TryEnqueue(() => _ = ChangeStandardSnapAsync(disable));
    }

    private void ReplaceWinArrow_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncing || _busy)
        {
            return;
        }

        var replace = ReplaceWinArrowToggle.IsOn;
        _busy = true;
        // Toggled が戻るまで待ち、失敗時の表示更新によるアニメーション再入を避ける。
        DispatcherQueue.TryEnqueue(() => ChangeReplaceWinArrow(replace));
    }

    private void ChangeReplaceWinArrow(bool replace)
    {
        ErrorNotice.IsOpen = false;
        try
        {
            _state = DeviceStateChanges.SetReplaceWinArrow(_deviceStore, replace);
        }
        catch (FutureSettingsVersionException)
        {
            Revert("新しい版の端末状態のため、Win + 矢印を変更できません。Snapwell を更新してください。");
            _readOnly = true;
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            Revert("Win + 矢印の選択を保存できませんでした。ファイルの場所とアクセス権を確認してください。");
        }
        finally
        {
            _busy = false;
            Render();
        }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        ErrorNotice.IsOpen = false;
        try
        {
            _state = DeviceStateChanges.RestoreStandardBehavior(_deviceStore);
            _savedDragSnap = SnapSettingsGuard.ReadSavedDragSnap();
        }
        catch (FutureSettingsVersionException)
        {
            Revert("新しい版の端末状態のため、標準の動作に戻せません。Snapwell を更新してください。");
            _readOnly = true;
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            Revert("標準の動作に戻す選択を保存できませんでした。ファイルの場所とアクセス権を確認してください。");
        }
        finally
        {
            _busy = false;
            Render();
        }
    }

    private async Task ChangeStandardSnapAsync(bool disable)
    {
        StandardSnapToggle.IsEnabled = false;
        RestoreButton.IsEnabled = false;
        ErrorNotice.IsOpen = false;
        try
        {
            var loaded = _deviceStore.Load();
            if (loaded.IsReadOnly)
            {
                throw new FutureSettingsVersionException("新しい版の端末状態は上書きできない。");
            }
            _state = loaded.Value;
            var consentObtained = false;
            while (true)
            {
                if (disable && !_state.StandardSnapConsentGiven)
                {
                    var dialog = new ContentDialog
                    {
                        XamlRoot = XamlRoot,
                        Title = "Windows 標準のスナップを停止しますか？",
                        Content = "Windows の設定アプリの「ウィンドウのスナップ」をこのセッションで停止します。" +
                            "ドラッグでのスナップ・最大化ボタンのメニュー・画面上端のスナップ バーが一緒に止まります。" +
                            "Snapwell の一時停止時と終了時には、変更前の設定に戻します。",
                        PrimaryButtonText = "停止する",
                        CloseButtonText = "やめる",
                        DefaultButton = ContentDialogButton.Close,
                    };
                    Dialogs.DialogButtons.UseSnapwellColors(dialog);
                    if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                    {
                        return;
                    }
                    consentObtained = true;
                }

                try
                {
                    // 本体の監視が追従するため、本体が起動していなくても端末状態だけを保存する。
                    _state = DeviceStateChanges.SetStandardSnap(_deviceStore, disable, consentObtained);
                    break;
                }
                catch (StandardSnapConsentRequiredException exception)
                {
                    // 保存待ちに取り消された同意は、最新の状態を表示して取り直す。
                    _state = exception.State;
                }
            }

            _savedDragSnap = SnapSettingsGuard.ReadSavedDragSnap();
        }
        catch (FutureSettingsVersionException)
        {
            Revert("新しい版の端末状態のため、標準のスナップを変更できません。Snapwell を更新してください。");
            _readOnly = true;
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            Revert("標準のスナップの選択を保存できませんでした。ファイルの場所とアクセス権を確認してください。");
        }
        finally
        {
            _busy = false;
            Render();
        }
    }

    private void Revert(string message)
    {
        ReportFailure(message);
        try
        {
            ReadState();
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            // 保存値も読めないときは最後に表示した値へ戻し、再操作を止める。
            _readOnly = true;
        }
    }

    private void ReportFailure(string message)
    {
        ErrorNotice.Message = message;
        ErrorNotice.IsOpen = true;
    }

    private static bool IsFileFailure(Exception exception) =>
        exception is UnauthorizedAccessException or IOException or System.Text.Json.JsonException;
}
