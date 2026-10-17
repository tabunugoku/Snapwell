// 技術検証 (DESIGN §15) 用のコンソール。検証のために自分で起動したメモ帳だけを操作し、最後に閉じる。
// OS の設定は読むだけで、変えない。
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using static Native;

var cmd = args.Length > 0 ? args[0] : "all";
Console.OutputEncoding = Encoding.UTF8;
Report.Line($"# Snapwell spikes ({cmd})");
Report.Line($"process={RuntimeInformation.ProcessArchitecture} os={RuntimeInformation.OSArchitecture} " +
    $"build={Environment.OSVersion.Version}");

if (cmd is "all" or "snapsettings")
{
    Spikes.SnapSettings();
}
if (cmd is "all" or "window")
{
    using var target = Target.Launch();
    Spikes.Frame(target.Hwnd);
    Spikes.HitTest(target.Hwnd);
    Spikes.Corner(target.Hwnd);
    Spikes.Cloak(target.Hwnd);
    Spikes.Move(target.Hwnd);
    Spikes.Resize(target.Hwnd);
    Spikes.Thumbnail(target.Hwnd);
}
if (cmd is "thumb")
{
    using var target = Target.Launch();
    Spikes.Thumbnail(target.Hwnd);
}

static class Report
{
    public static void Line(string s) => Console.WriteLine(s);
    public static string Hr(int hr) => hr == 0 ? "S_OK" : $"0x{hr:X8}";
}

/// <summary>検証のために起動したメモ帳。Dispose で閉じる</summary>
sealed class Target : IDisposable
{
    public nint Hwnd { get; private init; }
    private RECT _original;

    public static Target Launch()
    {
        var before = new HashSet<nint>(TopLevel("Notepad"));
        Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true });
        for (var i = 0; i < 100; i++)
        {
            Thread.Sleep(100);
            var added = TopLevel("Notepad").FirstOrDefault(h => !before.Contains(h));
            if (added != 0)
            {
                Thread.Sleep(800);
                GetWindowRect(added, out var r);
                Report.Line($"target hwnd=0x{added:X} rect={r}");
                return new Target { Hwnd = added, _original = r };
            }
        }
        throw new InvalidOperationException("メモ帳のウィンドウが見つからない");
    }

    static IEnumerable<nint> TopLevel(string cls)
    {
        var list = new List<nint>();
        EnumWindows((h, _) =>
        {
            var sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            if (sb.ToString() == cls && IsWindowVisible(h))
            {
                list.Add(h);
            }
            return true;
        }, 0);
        return list;
    }

    public void Dispose()
    {
        // 保存していない文書は無いので、閉じても確認は出ない (実機未確認なら残るだけ)
        PostMessage(Hwnd, WM_CLOSE, 0, 0);
    }
}

static class Spikes
{
    public static void SnapSettings()
    {
        Report.Line("\n## 標準スナップの設定 (読むだけ)");
        int arranging = 0;
        var ok = SystemParametersInfo(SPI_GETWINARRANGING, 0, ref arranging, 0);
        Report.Line($"SPI_GETWINARRANGING ok={ok} value={arranging}");
        using (var desk = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
        {
            foreach (var name in new[] { "WindowArrangementActive", "DockMoving", "SnapSizing" })
            {
                Report.Line($@"HKCU\Control Panel\Desktop\{name} = {desk?.GetValue(name) ?? "(なし)"}");
            }
        }
        using var adv = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
        foreach (var name in new[] { "SnapAssist", "EnableSnapAssistFlyout", "EnableSnapBar", "EnableTaskGroups",
            "SnapFill", "JointResize", "DITest", "MultiTaskingAltTabFilter" })
        {
            Report.Line($@"HKCU\...\Explorer\Advanced\{name} = {adv?.GetValue(name) ?? "(なし)"}");
        }
    }

    public static void Frame(nint hwnd)
    {
        Report.Line("\n## 見えない枠 (GetWindowRect と DWMWA_EXTENDED_FRAME_BOUNDS)");
        GetWindowRect(hwnd, out var wr);
        var hr = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT fr, Marshal.SizeOf<RECT>());
        Report.Line($"dpi={GetDpiForWindow(hwnd)} window={wr} frame={fr} hr={Report.Hr(hr)}");
        Report.Line($"差: left={fr.Left - wr.Left} top={fr.Top - wr.Top} right={wr.Right - fr.Right} bottom={wr.Bottom - fr.Bottom}");
    }

    public static void HitTest(nint hwnd)
    {
        Report.Line("\n## WM_NCHITTEST (他プロセスへの SendMessageTimeout)");
        DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT fr, Marshal.SizeOf<RECT>());
        var points = new (string Name, int X, int Y)[]
        {
            ("タイトルバー (上端+12, 中央)", (fr.Left + fr.Right) / 2, fr.Top + 12),
            ("タイトルバー (上端+12, 左から 200)", fr.Left + 200, fr.Top + 12),
            ("クライアントの中央", (fr.Left + fr.Right) / 2, (fr.Top + fr.Bottom) / 2),
            ("右上の閉じるボタン付近", fr.Right - 20, fr.Top + 12),
        };
        foreach (var p in points)
        {
            var lp = (nint)((p.Y << 16) | (p.X & 0xFFFF));
            var sw = Stopwatch.StartNew();
            var r = SendMessageTimeout(hwnd, WM_NCHITTEST, 0, lp, SMTO_ABORTIFHUNG, 200, out var result);
            Report.Line($"{p.Name}: ret={r} hit={result} ({HitName((int)result)}) {sw.Elapsed.TotalMilliseconds:F2} ms");
            var child = WindowFromPoint(new POINT { X = p.X, Y = p.Y });
            Report.Line($"  WindowFromPoint=0x{child:X} root=0x{GetAncestor(child, GA_ROOT):X}");
        }
    }

    static string HitName(int h) => h switch
    {
        1 => "HTCLIENT",
        2 => "HTCAPTION",
        8 => "HTMINBUTTON",
        9 => "HTMAXBUTTON",
        20 => "HTCLOSE",
        _ => "その他",
    };

    public static void Corner(nint hwnd)
    {
        Report.Line("\n## DWMWA_WINDOW_CORNER_PREFERENCE を他プロセスのウィンドウに設定");
        foreach (var (name, v) in new[] { ("DONOTROUND", 1), ("ROUND", 2), ("ROUNDSMALL", 3), ("DEFAULT", 0) })
        {
            var value = v;
            var hr = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref value, sizeof(int));
            Report.Line($"{name}: hr={Report.Hr(hr)}");
            Thread.Sleep(300);
        }
    }

    public static void Cloak(nint hwnd)
    {
        Report.Line("\n## DWMWA_CLOAK を他プロセスのウィンドウに設定 (できない想定)");
        var on = 1;
        var hr = DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref on, sizeof(int));
        Report.Line($"cloak=1: hr={Report.Hr(hr)}");
        var off = 0;
        hr = DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref off, sizeof(int));
        Report.Line($"cloak=0: hr={Report.Hr(hr)}");
    }

    public static void Move(nint hwnd)
    {
        Report.Line("\n## SetWindowPos で位置だけを動かす (240 回)");
        GetWindowRect(hwnd, out var start);
        foreach (var (label, flags) in new[]
        {
            ("同期", SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE),
            ("ASYNCWINDOWPOS", SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS),
        })
        {
            var times = new List<double>();
            var mismatched = 0;
            for (var i = 0; i < 240; i++)
            {
                var x = start.Left + (int)(200 * Math.Sin(i / 240.0 * Math.PI * 2));
                var y = start.Top + (int)(120 * Math.Sin(i / 120.0 * Math.PI * 2));
                var sw = Stopwatch.StartNew();
                SetWindowPos(hwnd, 0, x, y, 0, 0, flags);
                times.Add(sw.Elapsed.TotalMilliseconds);
                GetWindowRect(hwnd, out var now);
                if (now.Left != x || now.Top != y)
                {
                    mismatched++;
                }
            }
            Report.Line($"{label}: {Stats(times)} 直後の位置が違った回数={mismatched}");
        }
        // 表示のフレームに合わせたときの実際の間隔 (DwmFlush で次の合成まで待つ)
        var intervals = new List<double>();
        var clock = Stopwatch.StartNew();
        var last = 0.0;
        for (var i = 0; i < 120; i++)
        {
            DwmFlush();
            var t = clock.Elapsed.TotalMilliseconds;
            if (i > 0)
            {
                intervals.Add(t - last);
            }
            last = t;
            var x = start.Left + (int)(200 * Math.Sin(i / 120.0 * Math.PI * 2));
            SetWindowPos(hwnd, 0, x, start.Top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
        }
        Report.Line($"DwmFlush ごとの間隔: {Stats(intervals)} (≈ {1000 / intervals.Average():F0} Hz)");
        SetWindowPos(hwnd, 0, start.Left, start.Top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    public static void Resize(nint hwnd)
    {
        Report.Line("\n## SetWindowPos でサイズを変える (120 回)");
        GetWindowRect(hwnd, out var start);
        var w0 = start.Right - start.Left;
        var h0 = start.Bottom - start.Top;
        var times = new List<double>();
        for (var i = 0; i < 120; i++)
        {
            var w = w0 + (int)(160 * Math.Sin(i / 120.0 * Math.PI * 2));
            var h = h0 + (int)(100 * Math.Sin(i / 120.0 * Math.PI * 2));
            var sw = Stopwatch.StartNew();
            SetWindowPos(hwnd, 0, 0, 0, w, h, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        Report.Line($"同期: {Stats(times)}");
        SetWindowPos(hwnd, 0, 0, 0, w0, h0, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    static string Stats(List<double> v)
    {
        var s = v.OrderBy(x => x).ToList();
        return $"平均 {v.Average():F3} ms / 中央 {s[s.Count / 2]:F3} / p95 {s[(int)(s.Count * 0.95)]:F3} / 最大 {s[^1]:F3}";
    }

    public static void Thumbnail(nint hwnd)
    {
        Report.Line("\n## 代理描画: DwmRegisterThumbnail と、元のウィンドウを隠す方法");
        const int hostX = 60, hostY = 60, hostW = 480, hostH = 320;
        var host = CreateWindowEx(WS_EX_TOPMOST | WS_EX_TOOLWINDOW, "STATIC", "spike-host",
            WS_POPUP | WS_VISIBLE, hostX, hostY, hostW, hostH, 0, 0, 0, 0);
        Report.Line($"host=0x{host:X}");
        Pump(300);
        var blank = Capture(hostX, hostY, hostW, hostH);
        Report.Line($"サムネイルなし: {blank}");

        var hr = DwmRegisterThumbnail(host, hwnd, out var thumb);
        Report.Line($"DwmRegisterThumbnail hr={Report.Hr(hr)}");
        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_VISIBLE | DWM_TNP_RECTDESTINATION | DWM_TNP_OPACITY,
            fVisible = true,
            opacity = 255,
            rcDestination = new RECT { Left = 0, Top = 0, Right = hostW, Bottom = hostH },
        };
        hr = DwmUpdateThumbnailProperties(thumb, ref props);
        Report.Line($"DwmUpdateThumbnailProperties hr={Report.Hr(hr)}");
        Pump(400);
        Report.Line($"サムネイルあり (元はそのまま): {Capture(hostX, hostY, hostW, hostH)}");
        GetWindowRect(hwnd, out var src);
        int srcX = src.Left + 40, srcY = src.Top + 80;
        Report.Line($"  元の場所 (そのまま): {Capture(srcX, srcY, 400, 300)}");

        // 方法 1: 元を WS_EX_LAYERED + alpha 0 にする
        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        var set = SetWindowLongPtr(hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED);
        var la = SetLayeredWindowAttributes(hwnd, 0, 0, LWA_ALPHA);
        Report.Line($"layered: SetWindowLongPtr prev=0x{set:X} err={Marshal.GetLastWin32Error()} SetLayeredWindowAttributes={la}");
        Pump(400);
        Report.Line($"サムネイルあり (元は alpha 0): {Capture(hostX, hostY, hostW, hostH)}");
        Report.Line($"  元の場所 (alpha 0): {Capture(srcX, srcY, 400, 300)}");
        SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, ex);
        Pump(300);

        // 方法 2: 元を仮想画面の外へ動かす
        GetWindowRect(hwnd, out var orig);
        var vx = GetSystemMetrics(SM_XVIRTUALSCREEN) + GetSystemMetrics(SM_CXVIRTUALSCREEN) + 200;
        SetWindowPos(hwnd, 0, vx, orig.Top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        Pump(400);
        Report.Line($"サムネイルあり (元は画面外): {Capture(hostX, hostY, hostW, hostH)}");
        Report.Line($"  元の場所 (画面外): {Capture(srcX, srcY, 400, 300)}");
        SetWindowPos(hwnd, 0, orig.Left, orig.Top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

        DwmUnregisterThumbnail(thumb);
        DestroyWindow(host);
    }

    static void Pump(int ms)
    {
        var until = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < until)
        {
            while (PeekMessage(out var msg, 0, 0, 0, PM_REMOVE))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            Thread.Sleep(10);
        }
    }

    /// <summary>画面の一部を取り込み、輝度の平均と標準偏差を返す (中身が描かれているかの目安)</summary>
    static string Capture(int x, int y, int w, int h)
    {
        var screen = GetDC(0);
        var mem = CreateCompatibleDC(screen);
        var bmp = CreateCompatibleBitmap(screen, w, h);
        var old = SelectObject(mem, bmp);
        BitBlt(mem, 0, 0, w, h, screen, x, y, SRCCOPY | CAPTUREBLT);
        SelectObject(mem, old);
        var bi = new BITMAPINFO();
        bi.biSize = Marshal.SizeOf<BITMAPINFOHEADER>();
        bi.biWidth = w;
        bi.biHeight = -h;
        bi.biPlanes = 1;
        bi.biBitCount = 32;
        var px = new byte[w * h * 4];
        GetDIBits(mem, bmp, 0, (uint)h, px, ref bi, 0);
        DeleteObject(bmp);
        DeleteDC(mem);
        ReleaseDC(0, screen);
        double sum = 0, sq = 0;
        var n = w * h;
        for (var i = 0; i < n; i++)
        {
            var l = 0.114 * px[i * 4] + 0.587 * px[i * 4 + 1] + 0.299 * px[i * 4 + 2];
            sum += l;
            sq += l * l;
        }
        var mean = sum / n;
        var sd = Math.Sqrt(Math.Max(0, sq / n - mean * mean));
        return $"輝度 平均 {mean:F1} 標準偏差 {sd:F1}";
    }
}

static class Native
{
    public const int SPI_GETWINARRANGING = 0x0082;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const int DWMWA_CLOAK = 13;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_NCHITTEST = 0x0084;
    public const uint SMTO_ABORTIFHUNG = 0x0002;
    public const uint GA_ROOT = 2;
    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    public const uint SWP_ASYNCWINDOWPOS = 0x4000;
    public const int GWL_EXSTYLE = -20;
    public const nint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_TOPMOST = 0x00000008, WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000;
    public const uint LWA_ALPHA = 0x2;
    public const int SM_XVIRTUALSCREEN = 76, SM_CXVIRTUALSCREEN = 78;
    public const uint PM_REMOVE = 1;
    public const uint DWM_TNP_RECTDESTINATION = 0x1, DWM_TNP_OPACITY = 0x4, DWM_TNP_VISIBLE = 0x8;
    public const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public override string ToString() => $"({Left},{Top})-({Right},{Bottom}) {Right - Left}x{Bottom - Top}";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam, lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DWM_THUMBNAIL_PROPERTIES
    {
        public uint dwFlags;
        public RECT rcDestination;
        public RECT rcSource;
        public byte opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        public int bmiColors;
    }

    public delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(nint h, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PostMessage(nint h, uint msg, nint w, nint l);
    [DllImport("user32.dll")] public static extern nint SendMessageTimeout(nint h, uint msg, nint w, nint l, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll")] public static extern nint WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern nint GetAncestor(nint h, uint flags);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(nint h);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint h, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] public static extern nint GetWindowLongPtr(nint h, int index);
    [DllImport("user32.dll", SetLastError = true)] public static extern nint SetWindowLongPtr(nint h, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetLayeredWindowAttributes(nint h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern bool SystemParametersInfo(int action, int param, ref int value, int winIni);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(nint h);
    [DllImport("user32.dll")] public static extern bool PeekMessage(out MSG msg, nint h, uint min, uint max, uint remove);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] public static extern nint DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] public static extern nint GetDC(nint h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(nint h, nint dc);
    [DllImport("gdi32.dll")] public static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] public static extern nint CreateCompatibleBitmap(nint dc, int w, int h);
    [DllImport("gdi32.dll")] public static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] public static extern int GetDIBits(nint dc, nint bmp, uint start, uint lines, byte[] bits, ref BITMAPINFO bi, uint usage);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(nint dc);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(nint h, int attr, out RECT value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(nint h, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmFlush();
    [DllImport("dwmapi.dll")] public static extern int DwmRegisterThumbnail(nint dest, nint src, out nint thumb);
    [DllImport("dwmapi.dll")] public static extern int DwmUnregisterThumbnail(nint thumb);
    [DllImport("dwmapi.dll")] public static extern int DwmUpdateThumbnailProperties(nint thumb, ref DWM_THUMBNAIL_PROPERTIES props);
}
