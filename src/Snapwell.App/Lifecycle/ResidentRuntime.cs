using Snapwell.App.Dragging;
using Snapwell.App.Hotkeys;
using Snapwell.App.Motion;
using Snapwell.App.Overlay;
using Snapwell.Core.Commands;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Lifecycle;

/// <summary>配送中の入力と停止を直列化し、停止後に移動要求が残ることを防ぐ。</summary>
internal sealed class ResidentRuntime : IAppRuntime
{
    private readonly object _gate = new();
    private readonly DragSnapController _drag;
    private readonly HotkeyCommandExecutor _hotkeys;
    private readonly IMotionEngine _motion;
    private readonly IReadOnlyList<HotkeyBinding> _bindings;
    private readonly IReadOnlyList<HotkeyBinding> _paused;
    private readonly Action<IReadOnlyList<HotkeyBinding>> _applyBindings;
    private readonly LauncherController? _launcher;
    private readonly ForegroundTargetTracker? _foregroundTargets;
    private readonly IZonePreview? _preview;
    private readonly Action<bool>? _setOverlayEnabled;
    private readonly IReadOnlyList<IDisposable>? _overlays;
    private readonly LongPressDetector? _longPress;
    private bool _disposed;
    private bool _enabled = true;

    internal ResidentRuntime(DragSnapController drag, HotkeyCommandExecutor hotkeys,
        IMotionEngine motion, IReadOnlyList<HotkeyBinding> bindings,
        Action<IReadOnlyList<HotkeyBinding>> applyBindings, LauncherController? launcher = null,
        IZonePreview? preview = null, Action<bool>? setOverlayEnabled = null,
        IReadOnlyList<IDisposable>? overlays = null, LongPressDetector? longPress = null,
        ForegroundTargetTracker? foregroundTargets = null)
    {
        _drag = drag;
        _hotkeys = hotkeys;
        _motion = motion;
        _bindings = bindings;
        _paused = HotkeyBindingsBuilder.Paused(bindings);
        _applyBindings = applyBindings;
        _launcher = launcher;
        _foregroundTargets = launcher is null ? null : foregroundTargets ??
            new ForegroundTargetTracker(new Win32WindowInspector(), (uint)Environment.ProcessId);
        _preview = preview;
        _setOverlayEnabled = setOverlayEnabled;
        _overlays = overlays;
        _longPress = longPress;
        if (_longPress is not null)
        {
            _longPress.CandidateReady += HandleLongPress;
        }
    }

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _enabled = enabled;
            _launcher?.SetEnabled(enabled);
            _drag.Enabled = enabled && _launcher?.IsOpen != true;
            if (!enabled)
            {
                _longPress?.Cancel();
                _preview?.Hide();
            }

            _setOverlayEnabled?.Invoke(enabled);
            // 一時停止中にキーを握りつぶすと、ほかのアプリの操作を奪う。再開のキーだけを残す。
            if (_launcher is null)
            {
                _applyBindings(enabled ? _bindings : _paused);
            }
        }
    }

    public void Handle(HookEvent value)
    {
        // 他プロセスの調査は遅いことがあるため、入力の配送を止めるロックの外で記録する。
        if (value is ForegroundChanged recorded)
        {
            _foregroundTargets?.Record(recorded.Handle);
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _launcher?.ValidateTarget();
            if (_launcher?.IsOpen != true && _longPress?.HasOpenedLauncher == true)
            {
                // 描画失敗やオーバーレイ側の操作で閉じた表示の所有権を、次の表示へ持ち越さない。
                _longPress.CancelLauncher();
            }

            // 終了の処理で記録を消す前に判定し、その終了通知自体もドラッグへ渡さない。
            var consumeMoveSize = value switch
            {
                MoveSizeStarted start => _longPress?.OwnsMoveSizeFor(start.Handle) == true,
                MoveSizeEnded end => _longPress?.OwnsMoveSizeFor(end.Handle) == true,
                _ => false
            };
            if (_longPress?.Handle(value, _enabled && _launcher is { IsOpen: false }) == true)
            {
                _launcher?.Close();
            }

            if (value is KeyChord chord)
            {
                if (chord.Id == (int)WindowCommand.ToggleEnabled)
                {
                    _longPress?.Cancel();
                    _launcher?.Close();
                }

                // 移動ループ中の押下と配置を競合させず、解放まではランチャーの抑止割り当てを保つ。
                var suppressAccept = chord.Id == (int)LauncherInput.Accept && _longPress?.IsHoldingLauncher == true;
                if (!suppressAccept && _launcher?.Handle(chord) != true &&
                    ((_enabled && _launcher?.IsOpen != true) || chord.Id == (int)WindowCommand.ToggleEnabled))
                {
                    _hotkeys.Handle(chord);
                }
            }
            else
            {
                if (value is MouseButton button)
                {
                    _launcher?.HandleGlobalPointer(button);
                }
                else if (value is MoveSizeStarted start && !consumeMoveSize &&
                    _longPress?.OwnsLauncherFor(start.Handle) != true)
                {
                    // 別のドラッグへ操作を返し、ランチャーとプレビューを同時に表示しない。
                    _launcher?.Close();
                }
                else if (value is ForegroundChanged foreground)
                {
                    _launcher?.HandleForegroundChanged(foreground);
                }

                _drag.Enabled = _enabled && _launcher?.IsOpen != true;
                if (!consumeMoveSize)
                {
                    _drag.Handle(value);
                }
            }

            _drag.Enabled = _enabled && _launcher?.IsOpen != true;
            if (_launcher?.IsOpen == true)
            {
                if (_longPress?.HasOpenedLauncher != true)
                {
                    _longPress?.CancelLauncher();
                }

                _preview?.Hide();
            }
            else if (_longPress?.HasOpenedLauncher == true)
            {
                _longPress.CancelLauncher();
            }
        }
    }

    private void HandleLongPress(LongPressCandidate candidate)
    {
        lock (_gate)
        {
            if (_disposed || !_enabled || _launcher is null || _launcher.IsOpen)
            {
                return;
            }
        }

        // 応答しない対象への SendMessageTimeout 中も、解放・一時停止・終了を配送する。
        if (_longPress!.CheckTarget(candidate))
        {
            lock (_gate)
            {
                if (_disposed || !_enabled || _launcher!.IsOpen || !_longPress.TryActivate(candidate))
                {
                    return;
                }

                _launcher.Open(candidate.Handle);
                if (_launcher.IsOpen)
                {
                    _drag.Enabled = false;
                    _preview?.Hide();
                }
                else
                {
                    _longPress.Cancel();
                }
            }
        }
    }

    /// <summary>入力配送と停止に対して直列化し、カーソルのモニターを整列する。</summary>
    public void ArrangeCurrentMonitor()
    {
        lock (_gate)
        {
            if (_disposed || !_enabled)
            {
                return;
            }

            _longPress?.Cancel();
            _launcher?.Close();
            _preview?.Hide();
            _hotkeys.ArrangeCurrentMonitor();
        }
    }

    /// <summary>入力配送と停止に対して直列化し、指定した対象でランチャーを開く。</summary>
    public void OpenLauncher(nint target, uint ignorePointerUpToMs)
    {
        lock (_gate)
        {
            if (_disposed || !_enabled || _launcher is null || _launcher.IsOpen)
            {
                return;
            }

            _longPress?.Cancel();
            _preview?.Hide();
            _launcher.Open(_foregroundTargets!.Resolve(target), ignorePointerUpToMs);
            if (_launcher.IsOpen)
            {
                _drag.Enabled = false;
            }
        }
    }

    public void CancelAll() => _motion.CancelAll();

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_longPress is not null)
            {
                _longPress.CandidateReady -= HandleLongPress;
                _longPress.Dispose();
            }

            _hotkeys.Stop();
            _applyBindings([]);
            _launcher?.Dispose();
            _drag.Enabled = false;
            _preview?.Hide();
            // Close による割り当ての復帰も解除してから、描画と移動のスレッドを止める。
            _applyBindings([]);
            _setOverlayEnabled?.Invoke(false);
            if (_overlays is not null)
            {
                foreach (var overlay in _overlays)
                {
                    overlay.Dispose();
                }
            }

            _motion.Dispose();
        }
    }
}
