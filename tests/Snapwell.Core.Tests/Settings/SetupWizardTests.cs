using Snapwell.Core.Commands;
using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

/// <summary>手順の遷移・保存の順序・入力と競合の判断を確かめる。</summary>
public sealed class SetupWizardTests
{
    /// <summary>同意しない選択も認め、標準機能の次へを押すまで保存しない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsentIsOptionalAndOnlyWrittenWhenLeavingItsStep(bool consent)
    {
        var portable = new Store<PortableSettings>(new());
        var device = new Store<DeviceState>(new());
        var wizard = new SetupWizard(portable, device);
        wizard.Back();
        Assert.Equal(SetupStep.Welcome, wizard.Step);
        Assert.False(wizard.CanGoBack);
        Assert.True(wizard.TryNext(consent, out _));
        Assert.Equal(0, device.Saves);
        wizard.Back();
        Assert.True(wizard.TryNext(consent, out _));
        Assert.True(wizard.TryNext(consent, out _));
        Assert.Equal(SetupStep.Hotkeys, wizard.Step);
        Assert.Equal(consent, device.Value.DisableStandardSnap);
        Assert.Equal(consent, device.Value.ReplaceWinArrow);
        Assert.Equal(consent, device.Value.StandardSnapConsentGiven);
        Assert.False(device.Value.SetupCompleted);
        Assert.Equal(0, portable.Saves);
    }

    /// <summary>間隔を触らずに完了しても、設定の既定値（8 px）で保存する。</summary>
    [Fact]
    public void GapStartsAtTheSettingsDefault()
    {
        var portable = new Store<PortableSettings>(new());
        var wizard = AtGrid(portable, new Store<DeviceState>(new()));
        Assert.Equal(8, wizard.Gap);
        Assert.True(wizard.TryNext(false, out _));
        Assert.Equal(8, portable.Value.Gap);
        Assert.Equal(8, portable.Value.Edge);
    }

    /// <summary>分割案と間隔以外の最新設定を保ち、保存成功後だけ完了にする。</summary>
    [Fact]
    public void FinishingPreservesOtherSettingsAndLatestDeviceFields()
    {
        var portable = new Store<PortableSettings>(new() { Feel = "Smooth", Edge = 15 });
        var device = new Store<DeviceState>(new());
        var wizard = AtGrid(portable, device);
        wizard.PlanIndex = 1;
        wizard.Gap = 24;
        device.Value = device.Value with { Enabled = false };
        Assert.True(wizard.TryNext(true, out var error));
        Assert.Null(error);
        Assert.Equal(new GridSettings(12, 6), portable.Value.Grid);
        Assert.Equal(24, portable.Value.Gap);
        Assert.Equal(24, portable.Value.Edge);
        Assert.Equal("Smooth", portable.Value.Feel);
        Assert.Equal(PortableSettings.CurrentSchemaVersion, portable.Value.SchemaVersion);
        Assert.False(device.Value.Enabled);
        Assert.True(device.Value.SetupCompleted);
        Assert.True(wizard.IsCompleted);
        Assert.False(wizard.TryNext(true, out _));
        Assert.False(wizard.CanGoBack);
    }

    /// <summary>どの保存が失敗しても未完了を残し、同じ画面で再試行できる。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SaveFailuresAndReadOnlySettingsNeverCompleteAndCanBeRetried(int failure)
    {
        var portable = new Store<PortableSettings>(new());
        var device = new Store<DeviceState>(new());
        var wizard = AtGrid(portable, device);
        portable.ReadOnly = failure == 0;
        portable.FailSave = failure == 1;
        device.FailAtSave = failure == 2 ? device.Saves + 2 : -1;
        Assert.False(wizard.TryNext(true, out var error));
        Assert.NotNull(error);
        Assert.False(wizard.IsCompleted);
        Assert.False(device.Value.SetupCompleted);
        Assert.Equal(SetupStep.Grid, wizard.Step);
        portable.ReadOnly = false;
        portable.FailSave = false;
        device.FailAtSave = -1;
        Assert.True(wizard.TryNext(true, out _));
        Assert.True(device.Value.SetupCompleted);
    }

    /// <summary>同意を保存できないときは手順を進めない。</summary>
    [Fact]
    public void ConsentSaveFailureKeepsCurrentStep()
    {
        var device = new Store<DeviceState>(new()) { FailSave = true };
        var wizard = new SetupWizard(new Store<PortableSettings>(new()), device);
        wizard.TryNext(true, out _);
        Assert.False(wizard.TryNext(true, out var error));
        Assert.NotNull(error);
        Assert.Equal(SetupStep.StandardFeatures, wizard.Step);
        Assert.False(device.Value.StandardSnapConsentGiven);
    }

    /// <summary>三つの分割案とスライダーの境界を設定へ反映する。</summary>
    [Theory]
    [InlineData(0, 0, 8, 8)]
    [InlineData(1, 12, 12, 6)]
    [InlineData(2, 24, 6, 4)]
    public void PlansAndSpacingMatchWizardChoices(int plan, double gap, int columns, int rows)
    {
        var settings = SetupWizard.CreateSettings(new(), plan, gap);
        Assert.Equal(new GridSettings(columns, rows), settings.Grid);
        Assert.Equal(gap, settings.Gap);
        Assert.Equal(gap, settings.Edge);
    }

    /// <summary>範囲外や非有限の入力を保存前に拒否する。</summary>
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(3, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 25)]
    [InlineData(0, double.NaN)]
    public void InvalidInputIsRejected(int plan, double gap) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SetupWizard.CreateSettings(new(), plan, gap));

    /// <summary>設定内の重複・OS での登録失敗・未割り当てを区別する。</summary>
    [Fact]
    public void ConflictStatusesIncludeInternalDuplicatesOsFailuresAndMissingBindings()
    {
        var hotkeys = DefaultHotkeys.Create();
        WindowCommand[] commands = [WindowCommand.OpenLauncher];
        Assert.Equal(SetupHotkeyStatus.Available, SetupWizard.CheckHotkeys(hotkeys, commands, new HashSet<KeyChordSpec>()));
        var unavailable = new HashSet<KeyChordSpec> { hotkeys[WindowCommand.OpenLauncher] };
        Assert.Equal(SetupHotkeyStatus.Conflict, SetupWizard.CheckHotkeys(hotkeys, commands, unavailable));
        hotkeys[WindowCommand.LeftHalf] = hotkeys[WindowCommand.OpenLauncher];
        Assert.Equal(SetupHotkeyStatus.Conflict, SetupWizard.CheckHotkeys(hotkeys, commands, new HashSet<KeyChordSpec>()));
        Assert.Equal(SetupHotkeyStatus.Unassigned, SetupWizard.CheckHotkeys(hotkeys,
            [WindowCommand.MoveCellLeft], new HashSet<KeyChordSpec>()));
    }

    private static SetupWizard AtGrid(Store<PortableSettings> portable, Store<DeviceState> device)
    {
        var wizard = new SetupWizard(portable, device);
        for (var i = 0; i < 3; i++) { Assert.True(wizard.TryNext(true, out _)); }
        return wizard;
    }

    private sealed class Store<T>(T value) : ISettingsStore<T>
    {
        internal T Value = value;
        internal bool ReadOnly;
        internal bool FailSave;
        internal int FailAtSave = -1;
        internal int Saves;
        public SettingsLoadResult<T> Load() => new(Value, IsReadOnly: ReadOnly);
        public SettingsLoadResult<T>? TryReload() => Load();
        public void Save(T updated)
        {
            if (FailSave || Saves + 1 == FailAtSave) { throw new IOException("save"); }
            Value = updated;
            Saves++;
        }
    }
}
