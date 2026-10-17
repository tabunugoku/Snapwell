using Snapwell.Platform.SystemSettings;

namespace Snapwell.Platform.Tests.SystemSettings;

/// <summary>偽の API で標準スナップ変更時の引数と戻り値を確認する。</summary>
public sealed class SnapSystemSettingsTests
{
    /// <summary>両方の引数に同じ値を渡し、レジストリ更新を指定せず通知する。</summary>
    [Theory]
    [InlineData(true, 1u)]
    [InlineData(false, 0u)]
    public void SetPassesValueInBothParametersWithSendChangeOnly(bool enabled, uint expected)
    {
        var calls = 0;
        var settings = new Win32SnapSystemSettings((action, uiParam, pvParam, flags) =>
        {
            calls++;
            Assert.Equal(0x0083u, action);
            Assert.Equal(expected, uiParam);
            Assert.Equal((nint)expected, pvParam);
            Assert.Equal(0x0002u, flags);
            return true;
        });

        Assert.True(settings.TrySetEnabled(enabled));
        Assert.Equal(1, calls);
    }

    /// <summary>API が失敗した場合は変更の失敗を返す。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SetReturnsFalseWhenApiFails(bool enabled)
    {
        var calls = 0;
        var settings = new Win32SnapSystemSettings((_, _, _, _) =>
        {
            calls++;
            return false;
        });

        Assert.False(settings.TrySetEnabled(enabled));
        Assert.Equal(1, calls);
    }
}
