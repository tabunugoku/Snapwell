using Snapwell.Core.Updates;

namespace Snapwell.App.Tray;

/// <summary>トレイのスレッド上で案内の表示と解除を反映する境界。</summary>
internal interface ITrayUpdateSink
{
    /// <summary>案内なしならメニューから更新の項目を取り除く。</summary>
    void SetUpdateOffer(UpdateOffer? offer);
}
