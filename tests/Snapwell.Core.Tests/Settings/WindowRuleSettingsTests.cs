using System.Text.Json;
using Snapwell.Core.Rules;
using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

public sealed class WindowRuleSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Snapwell-rules-" + Guid.NewGuid());
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public WindowRuleSettingsTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(10)]
    [InlineData(11)]
    public void OlderVersionsStartWithEmptyRulesEvenIfTheFieldWasHandWritten(int version)
    {
        File.WriteAllText(SettingsPath, $$"""
            {"SchemaVersion":{{version}},"Gap":17,
             "WindowRules":[{"ClassName":"Class","Placement":"Exclude"}]}
            """);
        var loaded = SettingsStores.Portable(SettingsPath).Load();
        Assert.False(loaded.ResetToDefaults);
        Assert.False(loaded.IsReadOnly);
        Assert.Empty(loaded.Value.WindowRules);
        Assert.Equal(PortableSettings.CurrentSchemaVersion, loaded.Value.SchemaVersion);
        Assert.Equal(17, loaded.Value.Gap);
        var imported = SettingsTransfer.Read(SettingsPath, new());
        Assert.True(imported.IsSuccess);
        Assert.Empty(SettingsTransfer.Apply(new(), imported).WindowRules);
    }

    [Fact]
    public void VersionTwelveRoundTripsAllFieldsAndEnumNames()
    {
        var rule = new WindowRule
        {
            Enabled = false,
            ProcessName = "editor.exe",
            ClassName = "EditorWindow",
            TitlePattern = "^報告書.* - 編集$",
            Placement = PlacementRule.PositionOnly,
            Corner = CornerRule.SystemDefault,
        };
        WindowRule[] rules = [rule,
            .. Enum.GetValues<PlacementRule>().Select(value => rule with { Enabled = true, Placement = value }),
            .. Enum.GetValues<CornerRule>().Select(value => rule with { Placement = PlacementRule.Exclude, Corner = value }),
        ];
        var store = SettingsStores.Portable(SettingsPath);
        store.Save(new() { WindowRules = rules });
        var loaded = store.Load();
        Assert.False(loaded.ResetToDefaults);
        Assert.Equal(rules, loaded.Value.WindowRules);
        using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        Assert.Equal(PortableSettings.CurrentSchemaVersion, document.RootElement.GetProperty("SchemaVersion").GetInt32());
        foreach (var item in document.RootElement.GetProperty("WindowRules").EnumerateArray())
        {
            Assert.Equal(JsonValueKind.String, item.GetProperty("Placement").ValueKind);
            Assert.Equal(JsonValueKind.String, item.GetProperty("Corner").ValueKind);
        }
        Assert.Equal(rule.TitlePattern, document.RootElement.GetProperty("WindowRules")[0]
            .GetProperty("TitlePattern").GetString());
    }

    [Theory]
    [InlineData("\"FutureValue\"")]
    [InlineData("999")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void UnknownEnumValuesBecomeInheritWithoutDiscardingTheFile(string unknown)
    {
        File.WriteAllText(SettingsPath, $$"""
            {"SchemaVersion":12,"Gap":17,"WindowRules":[
              {"ClassName":"Class","Placement":{{unknown}},"Corner":"Round"},
              {"ClassName":"Class","Placement":"Exclude","Corner":{{unknown}}}]}
            """);
        var store = SettingsStores.Portable(SettingsPath);
        var loaded = store.Load();
        Assert.False(loaded.ResetToDefaults);
        Assert.Equal(17, loaded.Value.Gap);
        Assert.Equal(PlacementRule.Inherit, loaded.Value.WindowRules[0].Placement);
        Assert.Equal(CornerRule.Inherit, loaded.Value.WindowRules[1].Corner);
        store.Save(loaded.Value);
        Assert.Equal(loaded.Value.WindowRules, store.Load().Value.WindowRules);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    public void LoadingDropsNullElementsAndKeepsTheFirst200Rules(int count)
    {
        WindowRule[] rules = [null!, .. Enumerable.Range(0, count).Select(index => new WindowRule
        {
            ClassName = "Class" + index, Placement = PlacementRule.Exclude,
        }), null!];
        WriteRaw(new() { WindowRules = rules });
        var loaded = SettingsStores.Portable(SettingsPath).Load();
        Assert.False(loaded.ResetToDefaults);
        Assert.Equal(200, loaded.Value.WindowRules.Length);
        Assert.Equal(rules.Skip(1).Take(200), loaded.Value.WindowRules);
        Assert.Equal(loaded.Value.WindowRules,
            SettingsTransfer.Apply(new(), SettingsTransfer.Read(SettingsPath, new())).WindowRules);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    public void MissingOrNullRuleArraysNormalizeToEmpty(string rules)
    {
        File.WriteAllText(SettingsPath, $$"""{"SchemaVersion":12,"WindowRules":{{rules}}}""");
        Assert.Empty(SettingsStores.Portable(SettingsPath).Load().Value.WindowRules);
        File.WriteAllText(SettingsPath, """{"SchemaVersion":12}""");
        Assert.Empty(SettingsStores.Portable(SettingsPath).Load().Value.WindowRules);
    }

    [Theory]
    [InlineData("NoCondition")]
    [InlineData("NoEffect")]
    [InlineData("InvalidTitlePattern")]
    [InlineData("UnsupportedTitlePattern")]
    [InlineData("TooLong")]
    public void InvalidRulesCanBeLoadedButCannotBeSavedOrExported(string kind)
    {
        var valid = new WindowRule { ClassName = "Class", Placement = PlacementRule.PositionOnly };
        var invalid = kind switch
        {
            "NoCondition" => new WindowRule { Placement = PlacementRule.Exclude },
            "NoEffect" => valid with { Placement = PlacementRule.Inherit },
            "InvalidTitlePattern" => valid with { TitlePattern = "[" },
            "UnsupportedTitlePattern" => valid with { TitlePattern = "(?=a)" },
            _ => valid with { TitlePattern = new string('a', 261) },
        };
        WriteRaw(new() { WindowRules = [invalid, valid] });
        var original = File.ReadAllBytes(SettingsPath);
        var store = SettingsStores.Portable(SettingsPath);
        var loaded = store.Load();
        Assert.False(loaded.ResetToDefaults);
        Assert.Equal(invalid, loaded.Value.WindowRules[0]);
        Assert.Equal(PlacementRule.PositionOnly,
            new WindowRuleEngine(loaded.Value.WindowRules).Resolve(new(null, "Class", "a")).Placement);
        Assert.Throws<ArgumentException>(() => store.Save(loaded.Value));
        Assert.Throws<ArgumentException>(() => store.Save(loaded.Value with
        {
            WindowRules = [invalid with { Enabled = false }],
        }));
        Assert.Throws<ArgumentException>(() => SettingsTransfer.Export(SettingsPath, loaded.Value));
        Assert.Equal(original, File.ReadAllBytes(SettingsPath));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void SavingAccepts200RulesButRejects201AndNullElements()
    {
        var rule = new WindowRule { ClassName = "Class", Corner = CornerRule.Round };
        var store = SettingsStores.Portable(SettingsPath);
        store.Save(new() { WindowRules = Enumerable.Repeat(rule, 200).ToArray() });
        Assert.Equal(200, store.Load().Value.WindowRules.Length);
        var original = File.ReadAllBytes(SettingsPath);
        Assert.Throws<ArgumentException>(() => store.Save(new()
        {
            WindowRules = Enumerable.Repeat(rule, 201).ToArray(),
        }));
        Assert.Throws<ArgumentException>(() => store.Save(new() { WindowRules = [null!] }));
        Assert.Equal(original, File.ReadAllBytes(SettingsPath));
    }

    [Fact]
    public void ExportAndSelectiveImportTreatTheWholeOrderedArrayAsOneValue()
    {
        var first = new WindowRule { ClassName = "Class", Corner = CornerRule.Round };
        var second = first with { Corner = CornerRule.SystemDefault };
        var current = new PortableSettings { WindowRules = [first, second] };
        var imported = current with { WindowRules = [second, first] };
        Assert.False(SettingsTransfer.HasChanges(current, current with { WindowRules = [first, second] }));
        Assert.True(SettingsTransfer.HasChanges(current, imported));
        SettingsTransfer.Export(SettingsPath, imported);
        var result = SettingsTransfer.Read(SettingsPath, current);
        Assert.True(result.IsSuccess);
        var difference = Assert.Single(result.Differences);
        Assert.Equal(SettingsField.WindowRules, difference.Key.Field);
        Assert.NotEqual(difference.CurrentValue, difference.ImportedValue);
        Assert.Equal(current.WindowRules, SettingsTransfer.Apply(current, result, []).WindowRules);
        var selected = SettingsTransfer.Apply(current, result, [difference.Key]);
        var all = SettingsTransfer.Apply(current, result);
        Assert.Equal(imported.WindowRules, selected.WindowRules);
        Assert.Equal(imported.WindowRules, all.WindowRules);
        selected.WindowRules[0] = first;
        Assert.Equal(second, SettingsTransfer.Apply(current, result).WindowRules[0]);
        Assert.Equal(second, imported.WindowRules[0]);
        Assert.False(SettingsTransfer.HasChanges(imported, all));
    }

    [Fact]
    public void FutureVersionsRemainReadOnlyAndKeepTheirRulesUntouched()
    {
        const string json = """{"SchemaVersion":14,"WindowRules":[{"FutureProperty":true}]}""";
        File.WriteAllText(SettingsPath, json);
        var store = SettingsStores.Portable(SettingsPath);
        Assert.True(store.Load().IsReadOnly);
        Assert.Throws<FutureSettingsVersionException>(() => store.Save(new()));
        Assert.False(SettingsTransfer.Read(SettingsPath, new()).IsSuccess);
        Assert.Throws<FutureSettingsVersionException>(() => SettingsTransfer.Export(SettingsPath, new()));
        Assert.Equal(json, File.ReadAllText(SettingsPath));
    }

    private void WriteRaw(PortableSettings value) =>
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(value, SettingsJsonContext.Default.PortableSettings));

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
