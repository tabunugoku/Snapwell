using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

/// <summary>サイズ変更のタイミングの移行、保存と取り込みを検査する。</summary>
public sealed class ResizeTimingSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Snapwell-size-timing-" + Guid.NewGuid());
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    /// <summary>テスト専用の保存先を作る。</summary>
    public ResizeTimingSettingsTests() => Directory.CreateDirectory(_directory);

    /// <summary>旧版は追加項目の値にかかわらず着地時へ移行する。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(11)]
    [InlineData(12)]
    public void OldVersionsUseOnLand(int version)
    {
        File.WriteAllText(SettingsPath, $$"""{"SchemaVersion":{{version}},"ResizeTiming":"WhileMoving"}""");
        var loaded = SettingsStores.Portable(SettingsPath).Load();
        Assert.False(loaded.IsReadOnly);
        Assert.Equal(SizeChangeTiming.OnLand, loaded.Value.ResizeTiming);
        Assert.Equal(13, loaded.Value.SchemaVersion);
        var imported = SettingsTransfer.Apply(new(), SettingsTransfer.Read(SettingsPath, new()));
        Assert.Equal(SizeChangeTiming.OnLand, imported.ResizeTiming);
        Assert.Equal(13, imported.SchemaVersion);
    }

    /// <summary>追加項目のない旧版も着地時へ移行する。</summary>
    [Fact]
    public void MissingTimingUsesOnLand()
    {
        File.WriteAllText(SettingsPath, """{"SchemaVersion":12}""");
        var loaded = SettingsStores.Portable(SettingsPath).Load().Value;
        Assert.Equal(SizeChangeTiming.OnLand, loaded.ResizeTiming);
        Assert.Equal(13, loaded.SchemaVersion);
    }

    /// <summary>保存と読み込みで移動中のサイズ変更を保つ。</summary>
    [Fact]
    public void SaveRoundTripsWhileMoving()
    {
        var store = SettingsStores.Portable(SettingsPath);
        store.Save(new() { ResizeTiming = SizeChangeTiming.WhileMoving });
        var loaded = store.Load().Value;
        Assert.Equal(SizeChangeTiming.WhileMoving, loaded.ResizeTiming);
        Assert.Equal(13, loaded.SchemaVersion);
    }

    /// <summary>数値として読める未定義値はほかの設定を保ったまま既定へ戻す。</summary>
    [Fact]
    public void UndefinedTimingUsesOnLand()
    {
        File.WriteAllText(SettingsPath, """{"SchemaVersion":13,"ResizeTiming":7,"Gap":21}""");
        var loaded = SettingsStores.Portable(SettingsPath).Load();
        Assert.False(loaded.ResetToDefaults);
        Assert.Equal(SizeChangeTiming.OnLand, loaded.Value.ResizeTiming);
        Assert.Equal(21, loaded.Value.Gap);
        Assert.Equal(SizeChangeTiming.OnLand,
            SettingsTransfer.Normalize(new() { ResizeTiming = (SizeChangeTiming)7 }).ResizeTiming);
        Assert.Equal(SizeChangeTiming.OnLand,
            SettingsTransfer.Apply(new(), SettingsTransfer.Read(SettingsPath, new())).ResizeTiming);
    }

    /// <summary>書き出しと全項目の取り込みで移動中のサイズ変更を保つ。</summary>
    [Fact]
    public void ExportAndImportRoundTripWhileMoving()
    {
        var current = new PortableSettings();
        var changed = current with { ResizeTiming = SizeChangeTiming.WhileMoving };
        SettingsTransfer.Export(SettingsPath, changed);
        var result = SettingsTransfer.Read(SettingsPath, current);
        Assert.True(result.IsSuccess);
        Assert.Equal(SizeChangeTiming.WhileMoving, SettingsTransfer.Apply(current, result).ResizeTiming);
        var difference = Assert.Single(result.Differences);
        Assert.Equal(SettingsField.ResizeTiming, difference.Key.Field);
        Assert.Equal(SizeChangeTiming.WhileMoving,
            SettingsTransfer.Apply(current, result, [difference.Key]).ResizeTiming);
        Assert.Equal(SizeChangeTiming.OnLand, SettingsTransfer.Apply(current, result, []).ResizeTiming);
        Assert.True(SettingsTransfer.HasChanges(current, changed));
        Assert.False(SettingsTransfer.HasChanges(changed, SettingsTransfer.Apply(current, result)));
    }

    /// <summary>テスト専用の保存先を消す。</summary>
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
