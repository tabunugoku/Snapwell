using Snapwell.App.Motion;
using Snapwell.App.Overlay;
using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Monitors;
using Snapwell.Platform.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapwell.App.Dragging;

/// <summary>リサイズの制約を取得する境界。寸法は見た目の物理ピクセル。</summary>
internal interface IResizeConstraints
{
    uint GetDpi(nint handle);

    bool TryGetMinimum(WindowSnapshot window, out int width, out int height);
}

/// <summary>配送済みの操作通知から手動リサイズの吸着先を表示し、終了時に区画へ揃える。</summary>
internal sealed class ResizeSnapController(IWindowInspector inspector, IMonitorSource monitors,
    IMotionEngine motion, Func<PortableSettings> settings, IResizeConstraints constraints, uint ownProcessId,
    IZonePreview? preview = null, TimeProvider? timeProvider = null, WindowTargetResolver? targets = null)
{
    // アニメーションとは無関係に、他プロセスへの問い合わせを入力頻度から切り離す。
    internal const int PreviewPollMs = 50;
    // 動きを時間で決めず、ドラッグ側と同じ間隔・猶予・上限で着地の完了を監視する。
    private const int HoldPollMs = 50;
    private const int HoldMinMs = 150;
    private const int HoldMaxMs = 2000;
    private readonly object _gate = new();
    private readonly WindowTargetResolver _targets = targets ?? new(new Win32ProcessNameSource(), settings, ownProcessId);
    private readonly IZonePreview _preview = preview ?? NullZonePreview.Instance;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private WindowSnapshot? _start;
    private uint _startDpi;
    private uint _startMonitorDpi;
    private ITimer? _previewTimer;
    private long _previewGeneration;
    private bool _previewPending;
    private bool _previewBusy;
    private long? _lastPreviewStart;
    private PixelRect? _shownTarget;
    private ITimer? _holdTimer;
    private WindowSnapshot? _holdWindow;
    private long _holdStart;
    private IReadOnlyList<MonitorInfo> _holdMonitors = [];

    internal void Handle(HookEvent value, bool enabled)
    {
        lock (_gate)
        {
            try
            {
                HandleCore(value, enabled);
            }
            catch (Exception)
            {
                // 配送側で記録する例外でも、直前の区画だけが画面に残らないようにする。
                CancelCore();
                throw;
            }
        }
    }

    private void HandleCore(HookEvent value, bool enabled)
    {
        var options = settings();
        if (!enabled || !options.SnapAfterResize)
        {
            CancelCore();
            return;
        }

        if (!options.ShowZonePreview)
        {
            StopPreviewUpdates();
            StopHold();
            HidePreview();
        }

        if (value is MoveSizeStarted start)
        {
            CancelCore();
            _start = inspector.TryInspect(start.Handle, out var window) && Eligible(window) ? window : null;
            _startDpi = _start is null ? 0 : constraints.GetDpi(start.Handle);
            _startMonitorDpi = _start is null ? 0 : GetMonitorDpi(_start, GetMonitorsOrEmpty());
        }
        else if (value is WindowDestroyed closed &&
            (_start?.Handle == closed.Handle || _holdWindow?.Handle == closed.Handle))
        {
            CancelCore();
        }
        else if (value is MouseMoved && _start is not null && options.ShowZonePreview)
        {
            // 座標は計画時に取得するため、未処理の入力は一件分の更新要求だけでよい。
            _previewPending = true;
            _previewTimer ??= _time.CreateTimer(OnPreview, _previewGeneration,
                TimeSpan.FromMilliseconds(PreviewPollMs), TimeSpan.FromMilliseconds(PreviewPollMs));
        }
        else if (value is MoveSizeEnded end && _start is { } original && original.Handle == end.Handle)
        {
            StopPreviewUpdates();
            _start = null;
            var plan = Plan(original, options, _startDpi, _startMonitorDpi,
                out var window, out var currentMonitors);
            if (plan is null || window is null)
            {
                HidePreview();
                return;
            }

            motion.Request(new(end.Handle, window.VisualBounds, plan.Target, plan.Walls,
                options.ToFeelPreset(), SizeMode: SizeMode.Land));
            if (options.ShowZonePreview && ShowPreview(plan.Target))
            {
                _holdWindow = window;
                _holdMonitors = currentMonitors.ToArray();
                _holdStart = _time.GetTimestamp();
                _holdTimer = _time.CreateTimer(OnHold, null,
                    TimeSpan.FromMilliseconds(HoldPollMs), TimeSpan.FromMilliseconds(HoldPollMs));
            }
        }
    }

    internal void Cancel()
    {
        lock (_gate)
        {
            CancelCore();
        }
    }

    private void OnPreview(object? state)
    {
        var generation = (long)state!;
        WindowSnapshot original;
        uint startDpi;
        uint startMonitorDpi;
        lock (_gate)
        {
            if (generation != _previewGeneration || !_previewPending || _previewBusy || _start is null ||
                (_lastPreviewStart is { } last && _time.GetElapsedTime(last).TotalMilliseconds < PreviewPollMs))
            {
                return;
            }

            _previewPending = false;
            _previewBusy = true;
            _lastPreviewStart = _time.GetTimestamp();
            original = _start;
            startDpi = _startDpi;
            startMonitorDpi = _startMonitorDpi;
        }

        try
        {
            // 問い合わせ中は配送側のロックを持たず、開始時の DPI と設定だけで計画する。
            var options = settings();
            options = options with { MonitorGrids = new(options.MonitorGrids, StringComparer.OrdinalIgnoreCase) };
            var plan = Plan(original, options, startDpi, startMonitorDpi, out _, out _);
            lock (_gate)
            {
                if (generation != _previewGeneration)
                {
                    return;
                }

                var currentOptions = settings();
                if (!currentOptions.SnapAfterResize || !currentOptions.ShowZonePreview)
                {
                    StopPreviewUpdates();
                    HidePreview();
                }
                else if (plan is null)
                {
                    HidePreview();
                }
                else
                {
                    ShowPreview(plan.Target);
                }
            }
        }
        catch (Exception)
        {
            // 古い計算の失敗では、新しい操作や着地待ちの表示を消さない。
            lock (_gate)
            {
                if (generation == _previewGeneration)
                {
                    HidePreview();
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                // 世代が変わっても実行中の問い合わせは一つに保ち、ワーカーを積み上げない。
                _previewBusy = false;
            }
        }
    }

    private void StopPreviewUpdates()
    {
        _previewGeneration++;
        _previewPending = false;
        _previewTimer?.Dispose();
        _previewTimer = null;
    }

    private ResizeSnapPlan? Plan(WindowSnapshot original, PortableSettings options,
        uint startDpi, uint startMonitorDpi,
        out WindowSnapshot? window, out IReadOnlyList<MonitorInfo> currentMonitors)
    {
        window = null;
        currentMonitors = [];
        var currentDpi = constraints.GetDpi(original.Handle);
        if (motion.IsMoving(original.Handle) || !inspector.TryInspect(original.Handle, out window) ||
            window.ProcessId != original.ProcessId || !CurrentlyAvailable(window) ||
            window.Style != original.Style || window.ExtendedStyle != original.ExtendedStyle ||
            startDpi == 0 || currentDpi == 0 || startDpi != currentDpi ||
            (original.VisualBounds.Width == window.VisualBounds.Width &&
                original.VisualBounds.Height == window.VisualBounds.Height))
        {
            return null;
        }

        currentMonitors = GetMonitorsOrEmpty();
        var currentMonitorDpi = GetMonitorDpi(window, currentMonitors);
        // DPI 非対応・システム DPI 対応の対象は、ウィンドウの DPI だけでは倍率の変化を検出できない。
        if (startMonitorDpi == 0 || currentMonitorDpi == 0 || startMonitorDpi != currentMonitorDpi ||
            !constraints.TryGetMinimum(window, out var width, out var height))
        {
            return null;
        }

        var areas = currentMonitors.Select(monitor => monitor.ToMonitorArea()).ToArray();
        return ResizeSnapPlanner.Plan(options, true, true, original.VisualBounds,
            window.VisualBounds, areas, startDpi, currentDpi, width, height);
    }

    private void OnHold(object? state)
    {
        lock (_gate)
        {
            if (_holdWindow is not { } held)
            {
                return;
            }

            try
            {
                var elapsed = _time.GetElapsedTime(_holdStart).TotalMilliseconds;
                var options = settings();
                if (!options.SnapAfterResize || !options.ShowZonePreview ||
                    !inspector.TryInspect(held.Handle, out var current) ||
                    current.ProcessId != held.ProcessId || !CurrentlyAvailable(current) ||
                    current.Style != held.Style || current.ExtendedStyle != held.ExtendedStyle ||
                    constraints.GetDpi(held.Handle) != _startDpi ||
                    GetMonitorDpi(current, _holdMonitors) != _startMonitorDpi ||
                    !_holdMonitors.SequenceEqual(GetMonitorsOrEmpty()) ||
                    elapsed >= HoldMaxMs || (elapsed >= HoldMinMs && !motion.IsMoving(held.Handle)))
                {
                    StopHold();
                    HidePreview();
                }
            }
            catch (Exception)
            {
                // 描画や対象の取得に失敗しても、タイマーの例外で常駐プロセスを終了させない。
                StopHold();
                HidePreview();
            }
        }
    }

    private bool ShowPreview(PixelRect target)
    {
        if (_shownTarget == target)
        {
            return true;
        }

        try
        {
            _shownTarget = target;
            _preview.Show(target);
            return true;
        }
        catch (Exception)
        {
            // 表示の失敗で、終了時の揃えや後続の入力配送を失敗させない。
            HidePreview();
            return false;
        }
    }

    private void HidePreview()
    {
        if (_shownTarget is null)
        {
            return;
        }

        _shownTarget = null;
        try
        {
            _preview.Hide();
        }
        catch (Exception)
        {
            // 描画スレッドが終了済みでも、操作状態の破棄を完了する。
        }
    }

    private void StopHold()
    {
        _holdWindow = null;
        _holdMonitors = [];
        _holdTimer?.Dispose();
        _holdTimer = null;
    }

    private void CancelCore()
    {
        StopPreviewUpdates();
        _start = null;
        StopHold();
        HidePreview();
    }

    private IReadOnlyList<MonitorInfo> GetMonitorsOrEmpty()
    {
        try
        {
            return monitors.GetMonitors();
        }
        catch (Exception)
        {
            // 抜き差しなどで取得できない間は、倍率の変化を判定できないため揃えない。
            return [];
        }
    }

    private static uint GetMonitorDpi(WindowSnapshot window, IReadOnlyList<MonitorInfo> availableMonitors) =>
        availableMonitors.FirstOrDefault(monitor => monitor.Bounds.Contains(window.VisualBounds.Center))?.DpiX ?? 0;

    private bool Eligible(WindowSnapshot window)
    {
        var target = _targets.Resolve(window);
        return !window.IsMaximized && target.IsTarget && target.CanResize;
    }

    // 開始後はタイトルやルールで判定し直さず、現在の表示状態だけを確認する。
    private static bool CurrentlyAvailable(WindowSnapshot window) =>
        !window.IsMaximized && window.IsVisible && !window.IsCloaked && !window.IsMinimized;
}

/// <summary>公開メッセージで最小寸法を問い合わせ、応答しないアプリは揃えない。</summary>
internal sealed class Win32ResizeConstraints : IResizeConstraints
{
    public uint GetDpi(nint handle) => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393)
        ? PInvoke.GetDpiForWindow(new HWND(handle)) : 0;

    // アニメーションの長さではなく、他プロセスの応答待ちの上限。
    private const uint TimeoutMilliseconds = 100;

    public unsafe bool TryGetMinimum(WindowSnapshot window, out int width, out int height)
    {
        width = height = 1;
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
        {
            return false;
        }

        var dpi = PInvoke.GetDpiForWindow(new HWND(window.Handle));
        var info = new MINMAXINFO
        {
            ptMinTrackSize = new(PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXMINTRACK, dpi),
                PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CYMINTRACK, dpi)),
        };
        nuint result;
        // システム定義メッセージのバッファの転送は OS に任せる。実機未確認。
        var success = PInvoke.SendMessageTimeout(new HWND(window.Handle), PInvoke.WM_GETMINMAXINFO, default,
            new LPARAM((nint)(&info)), SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG,
            TimeoutMilliseconds, &result).Value != 0;
        width = Math.Max(1, info.ptMinTrackSize.X - window.Insets.Left - window.Insets.Right);
        height = Math.Max(1, info.ptMinTrackSize.Y - window.Insets.Top - window.Insets.Bottom);
        return success;
    }
}
