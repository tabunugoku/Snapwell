using System.Reflection;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapwell.App.Tray;

/// <summary>
/// 状態・タスクバーの明暗・DPI に合うトレイのアイコン (HICON) を作り、使い回す。
/// アイコンは exe に埋め込んだ .ico から作る (別ファイルだと、置き忘れや差し替えでトレイが既定のアイコンに戻るため)。
/// 埋め込みのリソースは NativeAOT の publish でも残る。UI スレッドだけで使う。
/// </summary>
internal sealed unsafe class TrayIconSet : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private readonly Dictionary<(TrayIconKind Kind, bool Light, int Size), HICON> _icons = [];
    private readonly Dictionary<string, IReadOnlyList<IcoFrame>> _files = [];

    /// <summary>タスクバーがライトか。読むだけで、書かない。値が無い・読めないときはライト扱い。</summary>
    public static bool IsTaskbarLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("SystemUsesLightTheme") is not int value || value != 0;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or IOException)
        {
            return true;
        }
    }

    /// <summary>
    /// トレイが使う小アイコンのサイズ。100% で 16、125% で 20、150% で 24、200% で 32。
    /// DPI はトレイのウィンドウ (主モニター) のものを使う。タスクバーが別 DPI のモニターにある場合は実機未確認。
    /// </summary>
    public static int SmallIconSize(HWND window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
        {
            return 16;
        }

        var dpi = PInvoke.GetDpiForWindow(window);
        return PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, dpi == 0 ? 96 : dpi);
    }

    /// <summary>該当のアイコンを返す。ハンドルはこのクラスが持つので、呼び出し側で壊さない。</summary>
    public HICON Get(TrayIconKind kind, bool lightTaskbar, int requestedSize)
    {
        var name = TrayIconPolicy.ResourceName(kind, lightTaskbar);
        if (!_files.TryGetValue(name, out var frames))
        {
            frames = IcoFile.Parse(ReadResource(name));
            _files[name] = frames;
        }

        var size = TrayIconPolicy.PickFrameSize(requestedSize, [.. frames.Select(frame => frame.Size)]);
        if (_icons.TryGetValue((kind, lightTaskbar, size), out var cached))
        {
            return cached;
        }

        var frame = frames.First(candidate => candidate.Size == size);
        HICON icon;
        fixed (byte* data = frame.Data.Span)
        {
            // dwVer 0x00030000 は固定値。PNG 圧縮のフレームもこの呼び出しで作れる (Vista 以降)。
            icon = PInvoke.CreateIconFromResourceEx(data, (uint)frame.Data.Length, true, 0x00030000, size, size,
                IMAGE_FLAGS.LR_DEFAULTCOLOR);
        }

        if (icon.IsNull)
        {
            // i18n-keep: 画面に出さない開発者向けの例外・ログ。
            throw new InvalidOperationException($"トレイのアイコンを作れない: {name} ({size}px)");
        }

        _icons[(kind, lightTaskbar, size)] = icon;
        return icon;
    }

    public void Dispose()
    {
        foreach (var icon in _icons.Values)
        {
            PInvoke.DestroyIcon(icon);
        }

        _icons.Clear();
    }

    private static byte[] ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            // i18n-keep: 画面に出さない開発者向けの例外・ログ。
            ?? throw new InvalidOperationException($"埋め込みのアイコンが無い: {name}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
