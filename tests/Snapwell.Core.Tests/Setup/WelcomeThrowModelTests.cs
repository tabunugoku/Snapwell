using Snapwell.Core.Settings;
using Snapwell.Core.Setup;

namespace Snapwell.Core.Tests.Setup;

/// <summary>投げる窓の行き先・外壁・巡回と表示判断を確かめる。</summary>
public sealed class WelcomeThrowModelTests
{
    /// <summary>速度ゼロなら現在の中心に一番近い左区画に収まる。</summary>
    [Fact]
    public void ZeroVelocityReleaseSettlesInNearestCellWithMatchingSize()
    {
        var model = new WelcomeThrowModel(520, 188);
        model.Grab(); model.Move(8, 8); model.Release(0, 0);
        Settle(model);
        Assert.Equal(0, model.Target);
        AssertCellMatches(model);
    }

    /// <summary>右上への強い投げは右上へ向かい、外壁からはみ出さない。</summary>
    [Fact]
    public void StrongUpperRightThrowStaysInsideWallsAndSettlesInUpperRightCell()
    {
        var model = new WelcomeThrowModel(520, 188);
        model.Grab(); model.Move(-100, 1000); model.Release(4000, -2000);
        for (var i = 0; i < 300; i++)
        {
            model.Advance(1d / 60);
            Assert.InRange(model.X, 0, 520 - model.Width);
            Assert.InRange(model.Y, 0, 188 - model.Height);
        }
        Assert.Equal(1, model.Target);
        AssertCellMatches(model);
    }

    /// <summary>初回 1.6 秒、以後 2.6 秒で巡回し、触ったあとや非表示では進めない。</summary>
    [Fact]
    public void DemoRunsOnlyWhileVisibleAnimatedAndUntouched()
    {
        var model = new WelcomeThrowModel(520, 188);
        for (var i = 0; i < 200; i++) { model.Advance(.05, active: false); }
        for (var i = 0; i < 200; i++) { model.Advance(.05, animationsEnabled: false); }
        for (var i = 0; i < 31; i++) { model.Advance(.05); }
        Assert.Equal(1, model.Target);
        model.Advance(.05);
        Assert.Equal(2, model.Target);
        for (var i = 0; i < 52; i++) { model.Advance(.05); }
        Assert.Equal(0, model.Target);
        model.Grab();
        for (var i = 0; i < 200; i++) { model.Advance(.05); }
        model.Release(0, 0, animationsEnabled: false);
        var target = model.Target;
        for (var i = 0; i < 200; i++) { model.Advance(.05); }
        Assert.Equal(target, model.Target);
    }

    /// <summary>効果オフで直接収め、リサイズと極小の舞台でも取り残さない。</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(320, 188)]
    public void ReducedMotionAndResizeSnapWithoutPhysics(double width, double height)
    {
        var model = new WelcomeThrowModel(520, 188);
        model.Grab(); model.Move(0, 100); model.Release(0, 0, false);
        AssertCellMatches(model);
        model.Resize(width, height);
        AssertCellMatches(model);
        Assert.InRange(model.X + model.Width, 0, width);
        Assert.InRange(model.Y + model.Height, 0, height);
    }

    /// <summary>無効な刻みは無視し、大きな刻みは上限に切る。</summary>
    [Fact]
    public void FrameTimeIsClampedAndZeroDoesNotAdvance()
    {
        var a = new WelcomeThrowModel(520, 188);
        var b = new WelcomeThrowModel(520, 188);
        a.Grab(); b.Grab(); a.Move(0, 0); b.Move(0, 0); a.Release(200, 50); b.Release(200, 50);
        a.Advance(0);
        Assert.Equal(b.X, a.X);
        a.Advance(1000); b.Advance(.05);
        Assert.Equal((b.X, b.Y, b.Width), (a.X, a.Y, a.Width));
    }

    /// <summary>導入の再生は手順の外で判断する。</summary>
    [Theory]
    [InlineData(SetupStep.Welcome, false, false, false)]
    [InlineData(SetupStep.Welcome, true, false, true)]
    [InlineData(SetupStep.Welcome, true, true, false)]
    [InlineData(SetupStep.StandardFeatures, true, false, false)]
    public void IntroPlaysOnlyOnFirstAnimatedWelcome(SetupStep step, bool enabled, bool shown, bool expected) =>
        Assert.Equal(expected, SetupPresentation.ShouldPlayIntro(step, enabled, shown));

    /// <summary>ようこそだけが「はじめる」で進む。</summary>
    [Theory]
    [InlineData(SetupStep.Welcome, "Setup.Welcome.Start")]
    [InlineData(SetupStep.StandardFeatures, "Setup.Next")]
    [InlineData(SetupStep.Hotkeys, "Setup.Next")]
    [InlineData(SetupStep.Grid, "Setup.Done")]
    public void NextLabelMatchesStep(SetupStep step, string key) => Assert.Equal(key, SetupPresentation.NextKey(step));

    private static void Settle(WelcomeThrowModel model)
    {
        for (var i = 0; i < 300; i++) { model.Advance(1d / 60); }
    }

    private static void AssertCellMatches(WelcomeThrowModel model)
    {
        var cell = model.Cells[model.Target];
        Assert.True(model.Settled);
        Assert.Equal((cell.X, cell.Y, cell.Width, cell.Height), (model.X, model.Y, model.Width, model.Height));
    }
}
