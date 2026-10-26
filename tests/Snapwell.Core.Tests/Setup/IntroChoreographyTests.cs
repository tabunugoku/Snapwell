using Snapwell.Core.Setup;

namespace Snapwell.Core.Tests.Setup;

/// <summary>導入の台本・収束・刻みの境界を確かめる。</summary>
public sealed class IntroChoreographyTests
{
    /// <summary>描画頻度が違っても最後の並びへ収まる。</summary>
    [Theory]
    [InlineData(1d / 60)]
    [InlineData(1d / 30)]
    public void AllSixWindowsSettleIntoFinalLayout(double dt)
    {
        var model = Create();
        while (!model.IsComplete) { model.Advance(dt); }
        for (var i = 0; i < 6; i++)
        {
            var w = model.Windows[i];
            var target = model.RectOf(3, i);
            Assert.Equal(target, w.Target);
            Assert.InRange(Math.Abs(w.X - target.X) + Math.Abs(w.Y - target.Y)
                + Math.Abs(w.Width - target.Width) + Math.Abs(w.Height - target.Height), 0, 1.6);
            Assert.InRange(Math.Sqrt(w.VelocityX * w.VelocityX + w.VelocityY * w.VelocityY), 0, 10);
            Assert.True(w.Settled);
        }
    }

    /// <summary>各境界より前に可視化・並べ替え・跳躍・ロゴを始めない。</summary>
    [Fact]
    public void ScriptDoesNotRunBeforeItsCueIncludingStaggeredWindows()
    {
        var model = Create();
        AdvanceTo(model, .499);
        Assert.All(model.Windows, w => Assert.False(w.Visible));
        AdvanceTo(model, 1.899);
        Assert.True(model.Windows[0].Visible);
        Assert.All(model.Windows.Skip(1), w => Assert.False(w.Visible));
        AdvanceTo(model, 2.999);
        Assert.NotEqual(model.RectOf(0, 0), model.Windows[0].Target);
        foreach (var (time, layout) in new[] { (3.9, 1), (4.8, 2), (5.7, 3) })
        {
            for (var i = 0; i < 6; i++)
            {
                AdvanceTo(model, time + i * .07 - .001);
                Assert.NotEqual(model.RectOf(layout, i), model.Windows[i].Target);
                AdvanceTo(model, time + i * .07 + .001);
                Assert.Equal(model.RectOf(layout, i), model.Windows[i].Target);
            }
        }
        AdvanceTo(model, 6.499);
        Assert.True(model.Windows[0].VelocityY > -100);
        AdvanceTo(model, 6.501);
        Assert.True(model.Windows[0].VelocityY < -400);
        AdvanceTo(model, 7.399);
        Assert.False(model.LogoVisible);
        AdvanceTo(model, 7.401);
        Assert.True(model.LogoVisible);
        AdvanceTo(model, 9.599);
        Assert.Equal(1, model.Opacity);
        AdvanceTo(model, 9.601);
        Assert.True(model.Opacity < 1);
    }

    /// <summary>固定の乱数と入力なら窓・点・波も一致する。</summary>
    [Fact]
    public void FixedRandomAndTimeStepsAreDeterministic()
    {
        var first = Create();
        var second = Create();
        for (var i = 0; i < 240; i++) { first.Advance(1d / 60); second.Advance(1d / 60); }
        Assert.Equal(first.Windows.Select(w => (w.X, w.Y, w.Rotation)),
            second.Windows.Select(w => (w.X, w.Y, w.Rotation)));
        Assert.Equal(first.Dots.Select(d => (d.DisplacementX, d.DisplacementY)),
            second.Dots.Select(d => (d.DisplacementX, d.DisplacementY)));
        Assert.Equal(first.Waves, second.Waves);
    }

    /// <summary>空・極小の舞台と窓数でも有限の状態を保つ。</summary>
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 1)]
    [InlineData(20, 10, 6)]
    public void EmptyAndTinyStagesAndWindowCountsRemainFinite(double width, double height, int count)
    {
        var model = new IntroChoreography(width, height, () => .5, count);
        model.Advance(0);
        Assert.Equal(0, model.Elapsed);
        model.Advance(1000);
        Assert.Equal(.05, model.Elapsed);
        while (!model.IsComplete) { model.Advance(.05); }
        Assert.All(model.Windows, w => Assert.True(double.IsFinite(w.X) && w.Width >= 0 && w.Height >= 0));
    }

    private static IntroChoreography Create()
    {
        var random = new Random(42);
        return new(760, 420, random.NextDouble);
    }

    private static void AdvanceTo(IntroChoreography model, double target)
    {
        while (model.Elapsed < target) { model.Advance(Math.Min(1d / 60, target - model.Elapsed)); }
    }
}
