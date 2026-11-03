using Snapwell.Core.Settings;
using Snapwell.Core.Setup;

namespace Snapwell.Core.Tests.Setup;

/// <summary>導入中と最初の手順で、操作できない戻るボタンを隠すことを確かめる。</summary>
public sealed class SetupBackVisibilityTests
{
    /// <summary>導入中はどの手順でも隠し、通常は手順 2 から表示する。</summary>
    [Theory]
    [InlineData(SetupStep.Welcome, false, false)]
    [InlineData(SetupStep.StandardFeatures, false, true)]
    [InlineData(SetupStep.Hotkeys, false, true)]
    [InlineData(SetupStep.Grid, false, true)]
    [InlineData(SetupStep.Welcome, true, false)]
    [InlineData(SetupStep.StandardFeatures, true, false)]
    [InlineData(SetupStep.Hotkeys, true, false)]
    [InlineData(SetupStep.Grid, true, false)]
    public void BackIsVisibleOnlyAfterWelcomeOutsideIntro(SetupStep step, bool introActive, bool expected) =>
        Assert.Equal(expected, SetupPresentation.IsBackVisible(step, introActive));

    /// <summary>戻った先でも現在の手順から判断し、ようこそに戻るたびに隠す。</summary>
    [Fact]
    public void ReturningToWelcomeHidesBackWithoutSavingSettings()
    {
        var wizard = new SetupWizard(new UnusedStore<PortableSettings>(), new UnusedStore<DeviceState>());
        for (var visit = 0; visit < 2; visit++)
        {
            Assert.False(SetupPresentation.IsBackVisible(wizard.Step, introActive: false));
            Assert.True(wizard.TryNext(false, out _));
            Assert.True(wizard.CanGoBack);
            Assert.True(SetupPresentation.IsBackVisible(wizard.Step, introActive: false));
            wizard.Back();
            Assert.Equal(SetupStep.Welcome, wizard.Step);
            Assert.False(SetupPresentation.IsBackVisible(wizard.Step, introActive: false));
        }
    }

    private sealed class UnusedStore<T> : ISettingsStore<T>
    {
        public SettingsLoadResult<T> Load() => throw new InvalidOperationException();
        public SettingsLoadResult<T>? TryReload() => throw new InvalidOperationException();
        public void Save(T settings) => throw new InvalidOperationException();
    }
}
