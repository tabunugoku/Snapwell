using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Snapwell.Settings.Controls;

/// <summary>キーボードでフォーカスした場合だけナビのフォーカス枠を表示する。</summary>
public sealed partial class NavigationItem : NavigationViewItem
{
    private Border? _focusOutline;

    /// <summary>入力方法が変わった場合もフォーカス枠を更新する。</summary>
    public NavigationItem()
    {
        // 自分の依存関係プロパティを監視し、反射を使う Binding を避ける。
        RegisterPropertyChangedCallback(FocusStateProperty, (_, _) => UpdateFocusOutline());
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _focusOutline = GetTemplateChild("KeyboardFocusOutline") as Border;
        UpdateFocusOutline();
    }

    private void UpdateFocusOutline()
    {
        if (_focusOutline is not null)
        {
            _focusOutline.Visibility = FocusState == FocusState.Keyboard ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
