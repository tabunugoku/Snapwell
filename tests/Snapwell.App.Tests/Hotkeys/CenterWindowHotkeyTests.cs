using Snapwell.App.Hotkeys;
using Snapwell.App.Tests.Motion;
using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Settings;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Hotkeys;

public sealed class CenterWindowHotkeyTests
{
    /// <summary>中央の 2 操作は、押しっぱなしで繰り返さず、一度だけ実行する割り当てにする。</summary>
    [Fact]
    public void CenterCommandsDoNotRepeatWhileKeyIsHeld()
    {
        var result = HotkeyBindingsBuilder.Build(new PortableSettings().Hotkeys);
        Assert.Empty(result.Dropped);
        foreach (var command in new[] { WindowCommand.CenterWindow, WindowCommand.CenterWindowKeepSize })
        {
            var binding = Assert.Single(result.Bindings, value => value.Id == (int)command);
            Assert.False(binding.AllowRepeat);
        }

        Assert.True(Assert.Single(result.Bindings, value => value.Id == (int)WindowCommand.GrowRight).AllowRepeat);
    }

    /// <summary>中央に配置は、既存の経路で、8×8 グリッドの中央の範囲への移動を要求する。</summary>
    [Fact]
    public void CenterWindowRequestsMoveToLauncherCenterRange()
    {
        var fixture = new Fixture();
        fixture.Executor.Handle(new((int)WindowCommand.CenterWindow));
        var request = Assert.Single(fixture.Motion.Requests);
        Assert.Equal(new PixelRect(486, 137, 948, 766), request.Target);
        Assert.True(request.Resize);
        Assert.Equal((nint)1, request.Handle);
    }

    /// <summary>サイズ変更できないウィンドウは、中央の範囲の中心へ現在のサイズのまま動く。</summary>
    [Fact]
    public void CenterWindowKeepsFixedWindowSize()
    {
        var fixture = new Fixture();
        fixture.Inspector.Window = fixture.Inspector.Window with { Style = 0 };
        fixture.Executor.Handle(new((int)WindowCommand.CenterWindow));
        var request = Assert.Single(fixture.Motion.Requests);
        Assert.Equal(new PixelRect(760, 370, 400, 300), request.Target);
        Assert.False(request.Resize);
    }

    /// <summary>区画で中央の範囲を表せないグリッドでは、移動を要求しない。</summary>
    [Fact]
    public void CenterWindowDoesNothingOnGridWithoutCenterRange()
    {
        var fixture = new Fixture(new PortableSettings { Grid = new(2, 2) });
        fixture.Executor.Handle(new((int)WindowCommand.CenterWindow));
        Assert.Empty(fixture.Motion.Requests);
    }

    /// <summary>サイズ維持の中央は、作業領域の中央へ現在のサイズのまま動く。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CenterWindowKeepSizeRequestsMoveToWorkAreaCenterWithoutResize(bool resizable)
    {
        var fixture = new Fixture();
        if (!resizable)
        {
            fixture.Inspector.Window = fixture.Inspector.Window with { Style = 0 };
        }

        fixture.Executor.Handle(new((int)WindowCommand.CenterWindowKeepSize));
        var request = Assert.Single(fixture.Motion.Requests);
        Assert.Equal(new PixelRect(760, 370, 400, 300), request.Target);
        Assert.Equal(fixture.Inspector.Window.VisualBounds, request.Current);
        Assert.False(request.Resize);
    }

    /// <summary>サイズ維持の中央は、区画で表せないグリッドでも動く。</summary>
    [Fact]
    public void CenterWindowKeepSizeWorksOnTwoByTwoGrid()
    {
        var fixture = new Fixture(new PortableSettings { Grid = new(2, 2) });
        fixture.Executor.Handle(new((int)WindowCommand.CenterWindowKeepSize));
        Assert.Equal(new PixelRect(760, 370, 400, 300), Assert.Single(fixture.Motion.Requests).Target);
    }

    /// <summary>一時停止中は、中央の操作でも移動を要求しない。</summary>
    [Theory]
    [InlineData(WindowCommand.CenterWindow)]
    [InlineData(WindowCommand.CenterWindowKeepSize)]
    public void CenterCommandsDoNothingWhileDisabled(WindowCommand command)
    {
        var fixture = new Fixture { Enabled = false };
        fixture.Executor.Handle(new((int)command));
        Assert.Empty(fixture.Motion.Requests);
    }

    private sealed class Fixture
    {
        internal FakeInspector Inspector { get; } = new();
        internal FakeMotion Motion { get; } = new();
        internal bool Enabled = true;
        internal HotkeyCommandExecutor Executor { get; }

        internal Fixture(PortableSettings? settings = null) =>
            Executor = new(Inspector, new FakeMonitors(), Motion, new UnusedPosition(), () => settings ?? new(),
                () => Enabled, () => { }, 999);
    }

    private sealed class UnusedPosition : IWindowPositionApi
    {
        public bool Restore(nint handle) => throw new NotSupportedException();
        public bool SetBounds(nint handle, PixelRect bounds, bool positionOnly) => throw new NotSupportedException();
        public bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements, bool positionOnly) =>
            throw new NotSupportedException();
    }
}
