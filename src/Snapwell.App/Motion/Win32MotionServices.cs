using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapwell.App.Motion;

/// <summary>高分解能の単調時計。</summary>
internal sealed class MonotonicClock : IClock
{
    public double Seconds => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
}

/// <summary>DWM の合成完了まで待つ。ディスプレイ切断時の挙動は実機未確認。</summary>
internal sealed class DwmFramePacer : IFramePacer
{
    public void Wait(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (PInvoke.DwmFlush().Failed)
        {
            // 合成停止時に空回りしないための待機。動きの長さには使わない。
            cancellationToken.WaitHandle.WaitOne(16);
        }
    }
}

/// <summary>OS と設定のどちらかが削減を指定していれば即時配置にする。</summary>
internal sealed class Win32MotionPreference(Func<bool> reduceMotion) : IMotionPreference
{
    public unsafe bool ReduceMotion
    {
        get
        {
            BOOL animation = true;
            return reduceMotion() ||
                (PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETCLIENTAREAANIMATION,
                    0, &animation, 0) && !animation);
        }
    }
}
