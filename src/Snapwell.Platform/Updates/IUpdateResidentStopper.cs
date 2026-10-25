namespace Snapwell.Platform.Updates;

/// <summary>新版の常駐を停止し、安全に巻き戻せるかを確認する。</summary>
public interface IUpdateResidentStopper
{
    /// <summary>配置先の常駐を期限内にすべて停止できた場合、または存在しない場合に true を返す。</summary>
    bool StopResidents(string installDir, TimeSpan timeout);
}
