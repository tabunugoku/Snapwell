namespace Snapwell.Core.Commands;

/// <summary>ウィンドウに対する配置操作。</summary>
public enum WindowCommand
{
    LeftHalf, RightHalf, TopHalf, BottomHalf,
    TopLeftQuarter, TopRightQuarter, BottomLeftQuarter, BottomRightQuarter,
    Maximize,
    GrowRight, GrowDown, ShrinkLeft, ShrinkUp,
    MoveCellLeft, MoveCellRight, MoveCellUp, MoveCellDown,
    NextMonitor, PreviousMonitor,
    ToggleEnabled,
    OpenLauncher,
    ArrangeGrid, ArrangeColumns, ArrangeRows, ArrangeMainAndStack,
    ArrangeAllMonitors,
    SnapAdjacentLeft, SnapAdjacentRight, SnapAdjacentUp, SnapAdjacentDown,
    CenterWindow, CenterWindowKeepSize,
}
