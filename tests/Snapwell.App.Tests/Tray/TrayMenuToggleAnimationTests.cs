using Snapwell.App.Tray.Menu;
using Snapwell.Core.Overlay;
using Snapwell.Core.Physics;
using Snapwell.Core.Tray;

namespace Snapwell.App.Tests.Tray;

/// <summary>偽の時計と配送先で、物理・中断・駆動の終了を検証する。</summary>
public sealed class TrayMenuToggleAnimationTests
{
    /// <summary>設定画面と同じばねの軌跡をたどり、静止後のフレーム要求を止める。</summary>
    [Fact]
    public void TargetChangeFollowsStiffHeavySpringAndStopsSchedulingAtRest()
    {
        var rig = new Rig();
        rig.Motion.Show(false);
        Assert.Empty(rig.Scheduled);
        rig.Motion.Show(true);
        Assert.Equal(0, rig.Motion.Position);
        Assert.True(rig.Motion.NeedsFrame);
        Assert.Equal([true], rig.Scheduled);
        var reference = new SpringSwitch(20, FeelPreset.StiffHeavy, false);
        reference.Set(true);
        for (var i = 0; i < 120 && rig.Motion.NeedsFrame; i++)
        {
            rig.Time += 1.0 / 60;
            rig.Motion.Frame();
            reference.Advance(1.0 / 60);
            Assert.Equal(reference.Position, rig.Motion.Position, 8);
            Assert.Equal(reference.Velocity, rig.Motion.Velocity, 8);
        }

        Assert.Equal(20, rig.Motion.Position);
        Assert.Equal(0, rig.Motion.Velocity);
        Assert.Equal(1, rig.Motion.Fill);
        Assert.Equal(1, rig.Motion.Stretch);
        Assert.False(rig.Motion.NeedsFrame);
        Assert.False(rig.Motion.Frame());
        Assert.Equal([true, false], rig.Scheduled);
    }

    /// <summary>更新のたびに物体を作り直さず、往復と同じ状態の通知でも速度を保つ。</summary>
    [Fact]
    public void RapidTargetReversalsAndSnapshotRefreshesPreservePositionAndVelocity()
    {
        var rig = new Rig();
        rig.Motion.Show(false);
        rig.Motion.Show(true);
        foreach (var enabled in new[] { false, true, true, false, false, true })
        {
            rig.Time += 1.0 / 60;
            rig.Motion.Frame();
            var position = rig.Motion.Position;
            var velocity = rig.Motion.Velocity;
            rig.Motion.Show(enabled);
            Assert.Equal(position, rig.Motion.Position);
            Assert.Equal(velocity, rig.Motion.Velocity);
            Assert.InRange(position, 0, 20);
        }

        Assert.Equal([true], rig.Scheduled);
    }

    /// <summary>動き始め直後の反転でも、速度を保ってから最後の一回で静止を確定する。</summary>
    [Fact]
    public void ReversalWithinRestToleranceStillSchedulesFinalSettlingFrame()
    {
        var rig = new Rig();
        rig.Motion.Show(false);
        rig.Motion.Show(true);
        rig.Time = .0001;
        rig.Motion.Frame();
        var position = rig.Motion.Position;
        var velocity = rig.Motion.Velocity;
        Assert.InRange(position, double.Epsilon, .25);
        Assert.InRange(velocity, double.Epsilon, 4);
        rig.Motion.Show(false);
        Assert.Equal(position, rig.Motion.Position);
        Assert.Equal(velocity, rig.Motion.Velocity);
        Assert.True(rig.Motion.NeedsFrame);
        rig.Time += 1.0 / 60;
        rig.Motion.Frame();
        Assert.Equal(0, rig.Motion.Position);
        Assert.Equal(0, rig.Motion.Velocity);
        Assert.False(rig.Motion.NeedsFrame);
        Assert.Equal([true, false], rig.Scheduled);
    }

    /// <summary>モーション削減では更新直後に目標へ置き、動作途中の OS の変更にも従う。</summary>
    [Fact]
    public void ReducedMotionJumpsToTargetAndCancelsPendingFrames()
    {
        var rig = new Rig { Reduced = true };
        rig.Motion.Show(false);
        rig.Motion.Show(true);
        Assert.Equal(20, rig.Motion.Position);
        Assert.False(rig.Motion.NeedsFrame);
        Assert.Empty(rig.Scheduled);
        rig.Reduced = false;
        rig.Motion.Show(false);
        Assert.True(rig.Motion.NeedsFrame);
        rig.Reduced = true;
        rig.Time += 1.0 / 60;
        rig.Motion.Frame();
        Assert.Equal(0, rig.Motion.Position);
        Assert.False(rig.Motion.NeedsFrame);
        Assert.Equal([true, false], rig.Scheduled);
    }

    /// <summary>Close は配送を止め、遅れて届くフレームを無視し、再表示は現在の状態で静止する。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpeningAndReopeningStartAtCurrentStateWithoutAnimation(bool enabled)
    {
        var rig = new Rig();
        rig.Motion.Show(enabled);
        Assert.Equal(enabled ? 20 : 0, rig.Motion.Position);
        Assert.False(rig.Motion.NeedsFrame);
        rig.Motion.Show(!enabled);
        Assert.True(rig.Motion.NeedsFrame);
        rig.Motion.Close();
        Assert.False(rig.Motion.NeedsFrame);
        Assert.False(rig.Motion.Frame());
        Assert.Equal([true, false], rig.Scheduled);
        rig.Motion.Show(!enabled);
        Assert.Equal(enabled ? 0 : 20, rig.Motion.Position);
        Assert.Equal(0, rig.Motion.Velocity);
        Assert.False(rig.Motion.NeedsFrame);
    }

    /// <summary>配送が遅れても一度の積分を 1/30 秒に制限する。</summary>
    [Fact]
    public void LongFrameUsesMaximumPhysicsStepInsteadOfElapsedPause()
    {
        var rig = new Rig();
        rig.Motion.Show(false);
        rig.Motion.Show(true);
        rig.Time = 10;
        rig.Motion.Frame();
        var reference = new SpringSwitch(20, FeelPreset.StiffHeavy, false);
        reference.Set(true);
        reference.Advance(1.0 / 30);
        Assert.Equal(reference.Position, rig.Motion.Position);
        Assert.Equal(reference.Velocity, rig.Motion.Velocity);
    }

    /// <summary>背景を再構築しなくても位置・塗り・色が進み、両端のつまみの上下中央を保つ。</summary>
    [Theory]
    [InlineData(1, OverlayTheme.Light)]
    [InlineData(2, OverlayTheme.Light)]
    [InlineData(1, OverlayTheme.Dark)]
    [InlineData(2, OverlayTheme.Dark)]
    public void AnimatedPixelsMoveRightAndKeepThumbCentered(double scale, OverlayTheme theme)
    {
        var layout = TrayMenuLayout.Create(new(900, 900, 24, 24), new(0, 0, 1200, 900),
            TrayTaskbarEdge.Bottom, scale);
        using var renderer = new TrayMenuRenderer(new(layout, TrayMenuModel.Create(false), false, null, 1, theme));
        var off = renderer.Render(-1, false, 0, 0);
        var middle = renderer.Render(-1, false, 10, .5, 1.02);
        var on = renderer.Render(-1, false, 20, 1);
        Assert.NotEqual(off.Pixels, middle.Pixels);
        Assert.NotEqual(middle.Pixels, on.Pixels);
        var centers = new List<double>();
        foreach (var (frame, position) in new[] { (off, 0), (on, 20) })
        {
            var thumb = layout.ThumbBounds(position);
            var color = position == 0 ? OverlayPalette.For(theme).Muted
                : theme == OverlayTheme.Dark ? new OverlayColor(17, 17, 17, 255) : new(255, 255, 255, 255);
            var points = new List<(int X, int Y)>();
            for (var y = thumb.Y; y < thumb.Bottom; y++)
            {
                for (var x = thumb.X; x < thumb.Right; x++)
                {
                    var index = ((y - layout.Bounds.Y) * frame.Width + x - layout.Bounds.X) * 4;
                    if (frame.Pixels[index] == color.Blue && frame.Pixels[index + 1] == color.Green &&
                        frame.Pixels[index + 2] == color.Red)
                    {
                        points.Add((x, y));
                    }
                }
            }

            Assert.NotEmpty(points);
            centers.Add((points.Min(point => point.X) + points.Max(point => point.X) + 1) / 2.0);
            Assert.InRange(Math.Abs((points.Min(point => point.Y) + points.Max(point => point.Y) + 1) / 2.0
                - layout.Toggle.Center.Y), 0, .5);
        }

        Assert.Equal(20 * scale, centers[1] - centers[0], 1);
        // フレームの状態は静的スナップショットから独立し、後の描画で過去の画素を書き換えない。
        Assert.Equal(off.Pixels, renderer.Render(-1, false, 0, 0).Pixels);
        Assert.Equal(renderer.Render(-1, false).Pixels, off.Pixels);
    }

    private sealed class Rig
    {
        internal double Time { get; set; }
        internal bool Reduced { get; set; }
        internal List<bool> Scheduled { get; } = [];
        internal TrayMenuToggleAnimation Motion { get; }

        internal Rig() => Motion = new(() => Time, () => Reduced, Scheduled.Add);
    }
}
