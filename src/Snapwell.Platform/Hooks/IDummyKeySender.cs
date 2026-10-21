namespace Snapwell.Platform.Hooks;

/// <summary>Win の解放前にスタートメニューの起動を抑える入力を送る境界。</summary>
internal interface IDummyKeySender
{
    /// <summary>割り当てのないキーの押下と解放を送り、失敗時は false を返す。</summary>
    bool Send();
}
