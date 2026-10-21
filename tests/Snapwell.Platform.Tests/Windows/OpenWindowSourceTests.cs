using System.Diagnostics.CodeAnalysis;
using Snapwell.Platform.Windows;

namespace Snapwell.Platform.Tests.Windows;

/// <summary>ルール候補の列挙で、表示状態とタイトルの条件だけを使うことを検証する。</summary>
public sealed class OpenWindowSourceTests
{
    /// <summary>ポップアップと固定サイズを残し、常に対象外のウィンドウを除く。</summary>
    [Fact]
    public void ListsNamedEligibleWindowsIncludingPopupsAndKeepsMissingProcessName()
    {
        var inspector = new Inspector();
        var normal = new WindowSnapshot(1, "Editor", "Document", 123, 0x40000, 0,
            new(0, 0, 400, 300), new(0, 0, 400, 300), default, false, true, false, false);
        inspector.Windows =
        [
            normal,
            normal with { Handle = 2, Style = 0x80000000, Title = "Popup" },
            normal with { Handle = 3, Style = 0, Title = "Fixed" },
            normal with { Handle = 4, Title = "" },
            normal with { Handle = 5, IsVisible = false },
            normal with { Handle = 6, IsCloaked = true },
            normal with { Handle = 7, IsMinimized = true },
            normal with { Handle = 8, Style = 0x40040000 },
            normal with { Handle = 9, ProcessId = 999 },
            normal with { Handle = 10, ClassName = "Shell_TrayWnd" },
        ];
        var names = new Names();
        var result = new Win32OpenWindowSource(inspector, names, 999).List();
        Assert.Equal(["Document", "Popup", "Fixed"], result.Select(window => window.Title));
        Assert.All(result, window => Assert.Null(window.ProcessName));
        Assert.Equal(3, names.Calls);
    }

    private sealed class Names : IProcessNameSource
    {
        internal int Calls;
        public string? GetName(uint processId) { Calls++; return null; }
    }

    private sealed class Inspector : IWindowInspector
    {
        internal WindowSnapshot[] Windows = [];
        public IReadOnlyList<nint> GetTopLevelWindows() => Windows.Select(window => window.Handle).ToArray();
        public bool TryInspect(nint handle, [NotNullWhen(true)] out WindowSnapshot? snapshot)
        {
            snapshot = Windows.FirstOrDefault(window => window.Handle == handle);
            return snapshot is not null;
        }
        public nint GetForegroundWindow() => 1;
        public nint GetRootWindowAt(int x, int y) => 0;
    }
}
