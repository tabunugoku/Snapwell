using Snapwell.Core.Rules;
using Snapwell.Core.Settings;
using Snapwell.Platform.Windows;

namespace Snapwell.Platform.Tests.Windows;

/// <summary>ルールと分類を合わせた判断を、プロセスへの問い合わせなしで検証する。</summary>
public sealed class WindowTargetResolverTests
{
    /// <summary>ルールなしでは従来の分類・全体設定を保つ。</summary>
    [Theory]
    [InlineData(0x40000u, true, true)]
    [InlineData(0x40000u, false, false)]
    [InlineData(0u, true, false)]
    [InlineData(0u, false, true)]
    [InlineData(0x80000000u, false, true)]
    [InlineData(0x80000000u, true, false)]
    [InlineData(0x40040000u, true, true)]
    public void NoRulesPreserveClassification(uint style, bool fixedSize, bool popup)
    {
        var options = new PortableSettings { MoveFixedSizeWindows = fixedSize, MovePopupWindows = popup };
        var names = new Names();
        var window = Window() with { Style = style };
        var kind = WindowClassifier.Classify(window, 999);
        var result = new WindowTargetResolver(names, () => options, 999).Resolve(window);
        Assert.Equal(kind, result.Kind);
        Assert.Equal(WindowTargetPolicy.IsTarget(kind, () => options), result.IsTarget);
        Assert.Equal(kind == WindowKind.Normal, result.CanResize);
        Assert.Equal(CornerRule.Inherit, result.Corner);
        Assert.Equal(0, names.Calls);
    }

    /// <summary>常に対象外の条件を、すべての配置・角ルールより先に適用する。</summary>
    [Theory]
    [InlineData("hidden")]
    [InlineData("cloaked")]
    [InlineData("minimized")]
    [InlineData("own")]
    [InlineData("child")]
    [InlineData("shell")]
    public void AlwaysExcludedNeverLooksUpRules(string reason)
    {
        var names = new Names();
        var options = Options(new WindowRule
        {
            ProcessName = "app.exe",
            Placement = PlacementRule.ForceTarget,
            Corner = CornerRule.Round
        });
        var window = reason switch
        {
            "hidden" => Window() with { IsVisible = false },
            "cloaked" => Window() with { IsCloaked = true },
            "minimized" => Window() with { IsMinimized = true },
            "own" => Window() with { ProcessId = 999 },
            "child" => Window() with { Style = 0x40040000 },
            _ => Window() with { ClassName = "Progman" },
        };
        var result = new WindowTargetResolver(names, () => options, 999).Resolve(window);
        Assert.Equal(WindowKind.Excluded, result.Kind);
        Assert.False(result.IsTarget);
        Assert.False(result.CanResize);
        Assert.Equal(CornerRule.Inherit, result.Corner);
        Assert.Equal(0, names.Calls);
    }

    /// <summary>位置だけと強制対象は、全体の無効設定より優先する。</summary>
    [Theory]
    [InlineData(PlacementRule.Exclude, WindowKind.Excluded, false, false)]
    [InlineData(PlacementRule.PositionOnly, WindowKind.FixedSize, true, false)]
    [InlineData(PlacementRule.ForceTarget, WindowKind.Normal, true, true)]
    public void PlacementOverridesPopupPolicy(PlacementRule placement, WindowKind kind, bool target, bool resize)
    {
        var options = Options(new WindowRule { ClassName = "Editor", Placement = placement });
        options.MoveFixedSizeWindows = options.MovePopupWindows = false;
        var names = new Names();
        var result = new WindowTargetResolver(names, () => options, 999)
            .Resolve(Window() with { Style = 0x80000000 });
        Assert.Equal(kind, result.Kind);
        Assert.Equal(target, result.IsTarget);
        Assert.Equal(resize, result.CanResize);
        Assert.True(result.WasPopup);
        Assert.Equal(0, names.Calls);
    }

    /// <summary>名前の取得失敗は条件に一致せず、例外も配送側へ出さない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrThrowingProcessNameDoesNotMatch(bool throws)
    {
        var names = new Names { Throw = throws, Name = null };
        var options = Options(new WindowRule { ProcessName = "app.exe", Placement = PlacementRule.Exclude });
        Assert.True(new WindowTargetResolver(names, () => options, 999).Resolve(Window()).IsTarget);
        Assert.Equal(1, names.Calls);
    }

    /// <summary>配列の差し替えでエンジンを更新し、同じ配列ではスナップショットを再利用する。</summary>
    [Fact]
    public void ReplacingRulesUpdatesEngineAndPropertiesResolveIndependently()
    {
        var options = Options(new WindowRule { ClassName = "Editor", Placement = PlacementRule.PositionOnly },
            new() { ProcessName = "app.exe", Corner = CornerRule.RoundSmall });
        var resolver = new WindowTargetResolver(new Names(), () => options, 999);
        var first = resolver.Resolve(Window());
        Assert.Equal(WindowKind.FixedSize, first.Kind);
        Assert.Equal(CornerRule.RoundSmall, first.Corner);
        options.WindowRules[0] = new() { ClassName = "Editor", Placement = PlacementRule.Exclude };
        Assert.Equal(first, resolver.Resolve(Window()));
        options = Options(new WindowRule { ClassName = "Editor", Placement = PlacementRule.Exclude });
        Assert.False(resolver.Resolve(Window()).IsTarget);
    }

    /// <summary>同じ窓口を複数のスレッドから呼んでも、配列とエンジンを取り違えない。</summary>
    [Fact]
    public void ParallelResolutionAndSettingsReplacementAreSafe()
    {
        var included = Options(new WindowRule
        {
            ClassName = "Editor",
            Placement = PlacementRule.PositionOnly,
            Corner = CornerRule.Round
        });
        var excluded = Options(new WindowRule
        {
            ClassName = "Editor",
            Placement = PlacementRule.Exclude,
            Corner = CornerRule.DoNotRound
        });
        var current = included;
        var resolver = new WindowTargetResolver(new Names(), () => Volatile.Read(ref current), 999);
        Parallel.For(0, 1000, i =>
        {
            Volatile.Write(ref current, i % 2 == 0 ? included : excluded);
            var result = resolver.Resolve(Window());
            Assert.True(result.Kind == WindowKind.FixedSize && result.Corner == CornerRule.Round ||
                result.Kind == WindowKind.Excluded && result.Corner == CornerRule.DoNotRound);
        });
    }

    private static PortableSettings Options(params WindowRule[] rules) => new() { WindowRules = rules };

    private static WindowSnapshot Window() => new(1, "Editor", "Document", 123, 0x40000, 0,
        new(100, 100, 400, 300), new(100, 100, 400, 300), default, false, true, false, false);

    private sealed class Names : IProcessNameSource
    {
        internal string? Name = "app.exe";
        internal bool Throw;
        internal int Calls;
        public string? GetName(uint processId)
        {
            Interlocked.Increment(ref Calls);
            return Throw ? throw new InvalidOperationException() : Name;
        }
    }
}
