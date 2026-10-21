using System.Diagnostics.CodeAnalysis;
using Snapwell.App.Lifecycle;
using Snapwell.App.Tests.Motion;
using Snapwell.Core.Rules;
using Snapwell.Core.Settings;
using Snapwell.Platform.Dwm;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Lifecycle;

public sealed class CornerWindowControllerTests
{
    [Fact]
    public void EnablingEnumeratesExistingWindowsAndPauseRestoresOriginal()
    {
        var api = new CornerApi();
        var controller = new CornerWindowController(new(api), new ExistingWindows(),
            () => new() { CornerPreference = WindowCornerPreference.DoNotRound }, 999, new Log());
        controller.SetEnabled(true);
        Assert.Equal(CornerStyle.DoNotRound, Assert.Single(api.Calls));
        controller.SetEnabled(false);
        Assert.Equal(CornerStyle.RoundSmall, api.Calls.Last());
    }

    [Theory]
    [InlineData(false, 123u, 0u, true)]
    [InlineData(true, 123u, 0u, false)]
    [InlineData(false, 999u, 0u, false)]
    [InlineData(false, 123u, 128u, false)]
    public void ShownWindowReceivesPreferenceOnlyWhenEligible(bool maximized, uint processId,
        uint extendedStyle, bool expected)
    {
        var inspector = new FakeInspector();
        inspector.Window = inspector.Window with
        {
            IsMaximized = maximized,
            ProcessId = processId,
            ExtendedStyle = extendedStyle,
        };
        var api = new CornerApi();
        var controller = new CornerWindowController(new(api), inspector,
            () => new() { CornerPreference = WindowCornerPreference.Round }, 999, new Log());
        controller.SetEnabled(true);
        controller.Handle(new WindowShown(1));
        Assert.Equal(expected ? new[] { CornerStyle.Round } : [], api.Calls);
        controller.SetEnabled(false);
        if (expected)
        {
            Assert.Equal(CornerStyle.RoundSmall, api.Calls.Last());
        }
    }

    [Theory]
    [InlineData(0x14000000u, 0x8u, false)]
    [InlineData(0x14000000u, 0x200008u, false)]
    [InlineData(0x14C00000u, 0u, true)]
    [InlineData(0x14040000u, 0u, true)]
    public void ShownWindowWithoutSystemFrameKeepsItsCorner(uint style, uint extendedStyle, bool expected)
    {
        var inspector = new FakeInspector();
        inspector.Window = inspector.Window with { Style = style, ExtendedStyle = extendedStyle };
        var api = new CornerApi();
        var controller = new CornerWindowController(new(api), inspector,
            () => new() { CornerPreference = WindowCornerPreference.Round }, 999, new Log());
        controller.SetEnabled(true);
        controller.Handle(new WindowShown(1));
        Assert.Equal(expected ? new[] { CornerStyle.Round } : [], api.Calls);
    }

    /// <summary>全体の角丸より個別の Windows 既定を優先し、すでに当てた値も復元する。</summary>
    [Fact]
    public void SystemDefaultRuleRestoresOriginalEvenWhenGlobalIsRound()
    {
        var inspector = new ExistingWindows();
        var options = new PortableSettings { CornerPreference = WindowCornerPreference.Round };
        var api = new CornerApi();
        var styler = new CornerStyler(api);
        var controller = new CornerWindowController(styler, inspector, () => options, 999, new Log());
        controller.SetEnabled(true);
        options.WindowRules = [new() { ClassName = "Editor", Corner = CornerRule.SystemDefault }];
        controller.SetEnabled(true);
        Assert.Equal([CornerStyle.Round, CornerStyle.RoundSmall], api.Calls);
        Assert.Equal(0, styler.TrackedCount);
    }

    /// <summary>明示した角は全体の既定・枠なし・ポップアップ・ツール属性より優先する。</summary>
    [Theory]
    [InlineData(CornerRule.Round, CornerStyle.Round)]
    [InlineData(CornerRule.RoundSmall, CornerStyle.RoundSmall)]
    [InlineData(CornerRule.DoNotRound, CornerStyle.DoNotRound)]
    public void ExplicitCornerAppliesToFramelessPopup(CornerRule rule, CornerStyle expected)
    {
        var inspector = new FakeInspector();
        inspector.Window = inspector.Window with { Style = 0x80000000, ExtendedStyle = 0x80 };
        var options = new PortableSettings
        {
            WindowRules = [new() { ClassName = "Editor", Corner = rule }],
        };
        var api = new CornerApi();
        var controller = new CornerWindowController(new(api), inspector, () => options, 999, new Log());
        controller.SetEnabled(true);
        controller.Handle(new WindowShown(1));
        Assert.Equal(expected, Assert.Single(api.Calls));
    }

    /// <summary>ルールの削除後は全体設定へ、全体の既定への変更後は元の値へ戻る。</summary>
    [Fact]
    public void RemovingRuleReappliesGlobalAndRestoresOriginalWhenGlobalBecomesDefault()
    {
        var options = new PortableSettings
        {
            CornerPreference = WindowCornerPreference.DoNotRound,
            WindowRules = [new() { ClassName = "Editor", Corner = CornerRule.Round }],
        };
        var api = new CornerApi();
        var controller = new CornerWindowController(new(api), new ExistingWindows(), () => options, 999, new Log());
        controller.SetEnabled(true);
        options.WindowRules = [];
        controller.SetEnabled(true);
        options.CornerPreference = WindowCornerPreference.Default;
        controller.SetEnabled(true);
        Assert.Equal([CornerStyle.Round, CornerStyle.DoNotRound, CornerStyle.RoundSmall], api.Calls);
    }

    /// <summary>配置だけを除外したウィンドウでも、角のルールは独立して適用する。</summary>
    [Fact]
    public void PlacementExclusionDoesNotExcludeCornerRule()
    {
        var options = new PortableSettings
        {
            WindowRules = [new() { ClassName = "Editor", Placement = PlacementRule.Exclude, Corner = CornerRule.Round }],
        };
        var api = new CornerApi();
        var controller = new CornerWindowController(new(api), new ExistingWindows(), () => options, 999, new Log());
        controller.SetEnabled(true);
        Assert.Equal(CornerStyle.Round, Assert.Single(api.Calls));
    }

    /// <summary>常に対象外の条件と最大化には、明示した角も当てない。</summary>
    [Theory]
    [InlineData("own")]
    [InlineData("maximized")]
    [InlineData("hidden")]
    [InlineData("cloaked")]
    [InlineData("minimized")]
    [InlineData("child")]
    [InlineData("shell")]
    public void ExplicitCornerCannotOverrideAlwaysExcludedOrMaximized(string reason)
    {
        var inspector = new FakeInspector();
        inspector.Window = reason switch
        {
            "own" => inspector.Window with { ProcessId = 999 },
            "maximized" => inspector.Window with { IsMaximized = true },
            "hidden" => inspector.Window with { IsVisible = false },
            "cloaked" => inspector.Window with { IsCloaked = true },
            "minimized" => inspector.Window with { IsMinimized = true },
            "child" => inspector.Window with { Style = 0x40040000 },
            _ => inspector.Window with { ClassName = "Shell_TrayWnd" },
        };
        var options = new PortableSettings
        {
            WindowRules = [new() { ClassName = inspector.Window.ClassName, Corner = CornerRule.Round }],
        };
        var api = new CornerApi();
        var controller = new CornerWindowController(new(api), inspector, () => options, 999, new Log());
        controller.SetEnabled(true);
        controller.Handle(new WindowShown(1));
        Assert.Empty(api.Calls);
    }

    /// <summary>タイトルが後から変わっても、表示通知と設定更新の間は角を追従させない。</summary>
    [Fact]
    public void TitleChangesDoNotReapplyCornersOnForegroundEvents()
    {
        var inspector = new FakeInspector();
        inspector.Window = inspector.Window with { Title = "Original" };
        var options = new PortableSettings
        {
            WindowRules = [new() { TitlePattern = "Original", Corner = CornerRule.Round }],
        };
        var api = new CornerApi();
        var controller = new CornerWindowController(new(api), inspector, () => options, 999, new Log());
        controller.SetEnabled(true);
        controller.Handle(new WindowShown(1));
        inspector.Window = inspector.Window with { Title = "Changed" };
        controller.Handle(new ForegroundChanged(1));
        Assert.Equal(CornerStyle.Round, Assert.Single(api.Calls));
    }

    private sealed class CornerApi : ICornerApi
    {
        internal List<CornerStyle> Calls { get; } = [];
        public CornerState? Read(nint handle) => new((long)handle, 123, 1, CornerStyle.RoundSmall, 10, "TestWindow");
        public bool IsWindow(nint handle) => true;
        public bool TrySet(nint handle, CornerStyle style) { Calls.Add(style); return true; }
    }

    private sealed class Log : IAppLog
    {
        public void Error(Exception exception) => throw new InvalidOperationException("Unexpected failure", exception);
    }

    private sealed class ExistingWindows : IWindowInspector
    {
        private readonly FakeInspector _inspector = new();
        public bool TryInspect(nint handle, [NotNullWhen(true)] out WindowSnapshot? snapshot) =>
            _inspector.TryInspect(handle, out snapshot);
        public nint GetForegroundWindow() => 1;
        public nint GetRootWindowAt(int x, int y) => 1;
        public IReadOnlyList<nint> GetTopLevelWindows() => [1];
    }
}
