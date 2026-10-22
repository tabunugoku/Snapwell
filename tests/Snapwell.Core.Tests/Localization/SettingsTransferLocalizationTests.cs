using Snapwell.Core.Commands;
using Snapwell.Core.Layout;
using Snapwell.Core.Localization;
using Snapwell.Core.Rules;
using Snapwell.Core.Settings;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Tests.Localization;

/// <summary>取り込みの表示言語をほかのテストと共有しない。</summary>
[CollectionDefinition("SettingsTransferLocalization", DisableParallelization = true)]
public sealed class SettingsTransferLocalizationCollection;

/// <summary>取り込みの判断とデータを保ち、差分と失敗理由だけを翻訳する。</summary>
[Collection("SettingsTransferLocalization")]
public sealed class SettingsTransferLocalizationTests : IDisposable
{
    private readonly UiLanguage _previous = UiText.Current.Language;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Snapwell-transfer-i18n-" + Guid.NewGuid());

    /// <summary>テストのファイルと言語だけを元に戻す。</summary>
    public void Dispose()
    {
        UiText.Use(_previous);
        if (Directory.Exists(_directory)) { Directory.Delete(_directory, recursive: true); }
    }

    /// <summary>列挙した全項目の名前と差分を、両言語で欠落なく表示する。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese)]
    [InlineData(UiLanguage.English)]
    public void EverySettingsFieldHasLocalizedDifferenceName(UiLanguage language)
    {
        UiText.Use(language);
        var current = new PortableSettings();
        var imported = ChangedSettings();
        var differences = SettingsTransfer.GetDifferences(current, imported);
        foreach (var field in Enum.GetValues<SettingsField>())
        {
            var key = $"Transfer.Diff.{field}";
            Assert.Contains(key, UiText.Current.Keys);
            var difference = Assert.Single(differences, item => item.Key.Field == field);
            Assert.False(string.IsNullOrWhiteSpace(difference.Name));
            Assert.NotEqual(difference.CurrentValue, difference.ImportedValue);
            Assert.Equal(field == SettingsField.Hotkey
                ? HotkeyCatalog.GetDisplayName(WindowCommand.LeftHalf) : UiText.Current.Get(key), difference.Name);
        }
    }

    /// <summary>日本語の従来の値を保ち、再取得した英語の値だけが変わる。</summary>
    [Fact]
    public void SwitchingLanguageChangesDisplayWithoutChangingImportedData()
    {
        UiText.Use(UiLanguage.Japanese);
        var imported = ChangedSettings();
        var japanese = SettingsTransfer.GetDifferences(new(), imported).ToDictionary(item => item.Key.Field);
        Assert.Equal("ウィンドウの間隔", japanese[SettingsField.Gap].Name);
        Assert.Equal("12.5 px", japanese[SettingsField.Gap].ImportedValue);
        Assert.Equal("2 区画", japanese[SettingsField.ResizeStep].ImportedValue);
        Assert.Equal("操作感に従う", japanese[SettingsField.MagnetStrength].CurrentValue);
        Assert.Equal("カスタム（Heavy から。質量 2、振動数 プリセットのまま、減衰比 プリセットのまま）",
            japanese[SettingsField.Feel].ImportedValue);
        Assert.Equal("DISPLAY1: 6 × 4, 間隔 10, 余白 プリセットのまま",
            japanese[SettingsField.MonitorGrids].ImportedValue);
        Assert.Equal("オン: editor.exe, UserClass, ^private title$, Exclude, Inherit",
            japanese[SettingsField.WindowRules].ImportedValue);
        UiText.Use(UiLanguage.English);
        var english = SettingsTransfer.GetDifferences(new(), imported).ToDictionary(item => item.Key.Field);
        Assert.Equal("Window gap", english[SettingsField.Gap].Name);
        Assert.Equal("2 cells", english[SettingsField.ResizeStep].ImportedValue);
        Assert.Equal("1 cell", english[SettingsField.ResizeStep].CurrentValue);
        Assert.Equal("DISPLAY1: 6 × 4, gap 10, edge margin Use preset", english[SettingsField.MonitorGrids].ImportedValue);
        Assert.Contains("editor.exe, UserClass, ^private title$, Exclude, Inherit",
            english[SettingsField.WindowRules].ImportedValue);
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        SettingsTransfer.Export(path, imported);
        var bytes = File.ReadAllBytes(path);
        var preview = SettingsTransfer.Read(path, new());
        Assert.True(preview.IsSuccess);
        var applied = SettingsTransfer.Apply(new(), preview);
        Assert.False(SettingsTransfer.HasChanges(imported, applied));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    /// <summary>未割り当ては両方向で翻訳し、キーの刻印と保存する割り当ては保つ。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese, "未割り当て")]
    [InlineData(UiLanguage.English, "Not assigned")]
    public void MissingAssignmentsAreLocalizedWithoutTranslatingKeyNames(UiLanguage language, string unassigned)
    {
        UiText.Use(language);
        var current = new PortableSettings();
        var imported = current with { Hotkeys = new(current.Hotkeys) };
        imported.Hotkeys.Remove(WindowCommand.LeftHalf);
        var removed = Assert.Single(SettingsTransfer.GetDifferences(current, imported));
        Assert.Equal(HotkeyCatalog.FormatChord(current.Hotkeys[WindowCommand.LeftHalf]), removed.CurrentValue);
        Assert.Equal(unassigned, removed.ImportedValue);
        var restored = Assert.Single(SettingsTransfer.GetDifferences(imported, current));
        Assert.Equal(unassigned, restored.CurrentValue);
        Assert.Equal(removed.CurrentValue, restored.ImportedValue);
    }

    /// <summary>角・固定位置・吸着方式の全値を両言語で表示する。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese)]
    [InlineData(UiLanguage.English)]
    public void EveryEnumValueHasLocalizedDisplay(UiLanguage language)
    {
        UiText.Use(language);
        foreach (var corner in Enum.GetValues<WindowCornerPreference>())
        {
            var before = corner == WindowCornerPreference.Default ? WindowCornerPreference.Round : WindowCornerPreference.Default;
            var difference = Assert.Single(SettingsTransfer.GetDifferences(new() { CornerPreference = before },
                new() { CornerPreference = corner }));
            var suffix = corner == WindowCornerPreference.Default ? "Default" : corner.ToString();
            Assert.Equal(UiText.Current.Get("Transfer.Corner." + suffix), difference.ImportedValue);
        }
        foreach (var anchor in Enum.GetValues<ResizeAnchor>())
        {
            var before = anchor == ResizeAnchor.TopLeft ? ResizeAnchor.Center : ResizeAnchor.TopLeft;
            var difference = Assert.Single(SettingsTransfer.GetDifferences(new() { ResizeAnchor = before },
                new() { ResizeAnchor = anchor }));
            Assert.Equal(UiText.Current.Get("Transfer.Anchor." + anchor), difference.ImportedValue);
        }
        foreach (var mode in Enum.GetValues<SnapMode>())
        {
            var before = mode == SnapMode.Zones ? SnapMode.Corners : SnapMode.Zones;
            var difference = Assert.Single(SettingsTransfer.GetDifferences(new() { SnapMode = before },
                new() { SnapMode = mode }));
            Assert.Equal(UiText.Current.Get("Transfer.Mode." + mode), difference.ImportedValue);
        }
    }

    /// <summary>すべての失敗理由を用意し、例外の Message を表示しない。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese)]
    [InlineData(UiLanguage.English)]
    public void FailureKindsUseCatalogMessagesAndLeaveFilesUntouched(UiLanguage language)
    {
        UiText.Use(language);
        foreach (var kind in new[] { "NotObject", "Future", "Empty", "Invalid", "Read" })
        {
            Assert.Contains("Transfer.Error." + kind, UiText.Current.Keys);
        }
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        (string Json, string Kind)[] cases =
        [
            ("[]", "NotObject"), ("null", "NotObject"), ("", "Invalid"), ("{", "Invalid"),
            ("{\"SchemaVersion\":999}", "Future"),
        ];
        foreach (var (json, kind) in cases)
        {
            File.WriteAllText(path, json);
            var result = SettingsTransfer.Read(path, new());
            Assert.False(result.IsSuccess);
            Assert.Empty(result.Differences);
            Assert.Equal(UiText.Current.Get("Transfer.Error." + kind), result.Error);
            Assert.Equal(json, File.ReadAllText(path));
        }
        Assert.Equal(UiText.Current.Get("Transfer.Error.Read"),
            SettingsTransfer.Read(Path.Combine(_directory, "private-missing.json"), new()).Error);
    }

    private static PortableSettings ChangedSettings()
    {
        var settings = new PortableSettings
        {
            Grid = new(6, 4),
            Gap = 12.5,
            Edge = 10,
            Feel = "Heavy",
            Mass = 2,
            SnapMode = SnapMode.Corners,
            ReduceMotion = true,
            ShowZonePreview = false,
            MagnetStrength = 1.2,
            SnapRadius = 40,
            ResizeStep = 2,
            ResizeAnchor = ResizeAnchor.Center,
            CornerPreference = WindowCornerPreference.Round,
            ShowZoneLines = false,
            ShowPredictionLine = false,
            SnapAfterResize = true,
            MoveFixedSizeWindows = false,
            MovePopupWindows = false,
            MonitorGrids = new() { ["display1"] = new(6, 4, 10) },
            LauncherPresets = [],
            WindowRules = [new() { ProcessName = "editor.exe", ClassName = "UserClass", TitlePattern = "^private title$",
                Placement = PlacementRule.Exclude }],
        };
        settings.Hotkeys[WindowCommand.LeftHalf] = new(ChordModifiers.Alt, 0x4C);
        return settings;
    }
}
