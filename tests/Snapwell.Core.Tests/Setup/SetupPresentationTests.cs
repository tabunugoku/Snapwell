using Snapwell.Core.Settings;
using Snapwell.Core.Setup;

namespace Snapwell.Core.Tests.Setup;

/// <summary>導入と舞台で共有するモーション許可と描画購読の判断を確かめる。</summary>
public sealed class SetupPresentationTests
{
    /// <summary>Windows と保存済み設定の両方が許す場合だけ動かす。</summary>
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void MotionRequiresBothSettings(bool animationsEnabled, bool reduceMotion, bool expected)
    {
        var allowed = SetupPresentation.IsMotionAllowed(animationsEnabled, reduceMotion);
        Assert.Equal(expected, allowed);
        Assert.Equal(expected, SetupPresentation.ShouldPlayIntro(SetupStep.Welcome, allowed, false));
        Assert.False(SetupPresentation.ShouldPlayIntro(SetupStep.Welcome, allowed, true));
        Assert.False(SetupPresentation.ShouldPlayIntro(SetupStep.StandardFeatures, allowed, false));
    }

    /// <summary>非表示・効果オフ・操作後の静止では購読せず、投げ直しで再開する。</summary>
    [Theory]
    [InlineData(false, false, false, false, false)]
    [InlineData(false, false, false, true, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, true, true, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, true, false, true, false)]
    [InlineData(false, true, true, false, false)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, false, false, true, true)]
    [InlineData(true, false, true, false, false)]
    [InlineData(true, false, true, true, true)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, true, false, true, true)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, true, true, false)]
    public void RenderingRequiresVisibleMotion(bool allowed, bool touched, bool settled, bool visible, bool expected) =>
        Assert.Equal(expected, SetupPresentation.ShouldSubscribeWelcomeRendering(allowed, touched, settled, visible));

    /// <summary>表示中かつ未完了だけ購読し、破棄後は表示が戻っても再開しない。</summary>
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, true, true, false)]
    public void IntroRenderingRequiresVisibleUnfinishedScene(bool visible, bool complete, bool disposed, bool expected) =>
        Assert.Equal(expected, SetupPresentation.ShouldSubscribeIntroRendering(visible, complete, disposed));

    /// <summary>何度復帰しても最初のフレームは時刻を進めず、以後は通常の刻みで再開する。</summary>
    [Fact]
    public void IntroResumeStartsWithZeroDelta()
    {
        var clock = new IntroFrameClock();
        var model = new IntroChoreography(520, 188);
        Assert.Equal(0, clock.Advance(0));
        model.Advance(clock.Advance(.02));
        Assert.Equal(.02, model.Elapsed, 10);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var elapsed = model.Elapsed;
            clock.Reset();
            clock.Reset();
            var now = attempt * 300d;
            var dt = clock.Advance(now);
            Assert.Equal(0, dt);
            model.Advance(dt);
            Assert.Equal(elapsed, model.Elapsed);
            dt = clock.Advance(now + .02);
            Assert.Equal(.02, dt, 10);
            model.Advance(dt);
            Assert.Equal(elapsed + .02, model.Elapsed, 10);
        }
    }

    /// <summary>再開後の描画が遅れても既存の一フレーム上限を守る。</summary>
    [Fact]
    public void IntroFrameDeltaIsCapped()
    {
        var clock = new IntroFrameClock();
        Assert.Equal(0, clock.Advance(10));
        Assert.Equal(.05, clock.Advance(20));
        Assert.Equal(.01, clock.Advance(20.01), 10);
    }

    /// <summary>一度止まった窓も投げ直しで購読し、効果オフなら直接区画に収める。</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void RethrowAndMotionChangeUpdateRendering(bool animationsEnabled, bool reduceMotion)
    {
        var model = new WelcomeThrowModel(520, 188);
        var allowed = SetupPresentation.IsMotionAllowed(animationsEnabled, reduceMotion);
        bool Subscribe() => SetupPresentation.ShouldSubscribeWelcomeRendering(allowed, model.Touched, model.Settled, true);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            model.Grab(); model.Move(8, 8); model.Release(500, 200, allowed);
            Assert.Equal(allowed, Subscribe());
            for (var i = 0; i < 300; i++) { model.Advance(1d / 60, animationsEnabled: allowed); }
            Assert.True(model.Settled);
            Assert.False(Subscribe());
            var cell = model.Cells[model.Target];
            Assert.Equal((cell.X, cell.Y, cell.Width, cell.Height), (model.X, model.Y, model.Width, model.Height));
        }

        model.Grab(); model.Move(8, 8); model.Release(500, 200);
        allowed = false;
        model.Advance(0, animationsEnabled: allowed);
        Assert.True(model.Settled);
        Assert.False(Subscribe());
    }
}
