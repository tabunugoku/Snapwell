using Snapwell.Core.Geometry;
using Snapwell.Core.Motion;
using Snapwell.Core.Physics;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Motion;

/// <summary>全ウィンドウの物理演算を一つの専用スレッドで進め、適用はロックの外で行う。</summary>
internal sealed class MotionEngine : IMotionEngine
{
    private sealed class Entry(WindowBody body, SizeMode sizeMode, double now)
    {
        internal WindowBody Body = body;
        internal SizeMode SizeMode = sizeMode;
        internal AsyncResizeGate? ResizeGate = sizeMode == SizeMode.Live ? new() : null;
        internal PixelRect BeforeResize;
        internal double LastFrame = now;
        internal long Version;
    }

    private readonly object _gate = new();
    private readonly Dictionary<nint, Entry> _entries = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly IFramePacer _pacer;
    private readonly IClock _clock;
    private readonly IWindowMover _mover;
    private readonly IMotionPreference _preference;
    private readonly Thread _thread;
    private bool _disposed;

    internal MotionEngine(IFramePacer pacer, IClock clock, IWindowMover mover, IMotionPreference preference)
    {
        _pacer = pacer;
        _clock = clock;
        _mover = mover;
        _preference = preference;
        _thread = new Thread(Run) { IsBackground = true, Name = "Snapwell motion" };
        _thread.Start();
    }

    public void Request(MotionRequest request)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var target = RectD.FromPixelRect(request.Target);
            var walls = RectD.FromPixelRect(request.Walls);
            if (_entries.TryGetValue(request.Handle, out var entry))
            {
                var mode = request.SizeMode == SizeMode.Live && entry.ResizeGate?.IsDegraded == true
                    ? SizeMode.Land : request.SizeMode;
                entry.Body.Retarget(target);
                if (entry.Body.Walls != walls || entry.Body.Feel != request.Feel || request.Stops is not null ||
                    entry.Body.Stops is not null || (entry.SizeMode == SizeMode.Live) != (mode == SizeMode.Live))
                {
                    entry.Body = new(entry.Body.State, target, walls, request.Feel, velocity: entry.Body.Velocity,
                        stops: request.Stops, clickRadius: request.ClickRadius, animateSize: mode == SizeMode.Live);
                }

                entry.SizeMode = mode;
                if (mode == SizeMode.Live)
                {
                    entry.ResizeGate ??= new();
                }
                // 同じ Entry を再利用する要求も、適用待ちのフレームと区別する。
                entry.Version++;
            }
            else
            {
                _entries[request.Handle] = new(new(RectD.FromPixelRect(request.Current), target, walls,
                    request.Feel, velocity: request.Velocity, stops: request.Stops,
                    clickRadius: request.ClickRadius, animateSize: request.SizeMode == SizeMode.Live),
                    request.SizeMode, _clock.Seconds);
            }

            Monitor.PulseAll(_gate);
        }
    }

    public bool IsMoving(nint handle)
    {
        lock (_gate)
        {
            return _entries.ContainsKey(handle);
        }
    }

    public void Cancel(nint handle)
    {
        lock (_gate)
        {
            // 次の適用の前に同じエントリーかを確かめるので、外した時点で以降のフレームは適用されない。
            _entries.Remove(handle);
        }
    }

    public void CancelAll()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // 適用中の呼び出しはロックの外にあるので、ここでは待たない。
            _disposed = true;
            _entries.Clear();
            _stop.Cancel();
            Monitor.PulseAll(_gate);
        }

        // DwmFlush と同期の SetWindowPos には取り消しがないため、待機を区切る。実機未確認。
        // _disposed により、待機が後から戻っても新しいフレームは適用しない。
        if (_thread.Join(TimeSpan.FromMilliseconds(250)))
        {
            _stop.Dispose();
        }
    }

    private void Run()
    {
        try
        {
            while (true)
            {
                try
                {
                    if (!RunFrame())
                    {
                        return;
                    }
                }
                catch (Exception) when (!_stop.IsCancellationRequested)
                {
                    // 物理の発散による変換の失敗などで、常駐プロセスを落とさない。動いている対象を外して続ける。
                    lock (_gate)
                    {
                        _entries.Clear();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
    }

    private bool RunFrame()
    {
        Application[] applications = [];
        lock (_gate)
        {
            while (!_disposed && _entries.Count == 0)
            {
                Monitor.Wait(_gate);
            }

            if (_disposed)
            {
                return false;
            }
        }

        // システム呼び出しを含むので、ロックの外で読む。
        var reduceMotion = ReadReduceMotion();
        if (reduceMotion)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return false;
                }

                // 削減時は合成フレームを待たず、一度だけ最終矩形を適用する。
                applications = [.. _entries.Select(pair => new Application(pair.Key, pair.Value,
                    pair.Value.ResizeGate is { } gate && (gate.HasPendingRequest || gate.IsUnresponsive)
                        ? ApplyKind.Async : ApplyKind.Bounds,
                    pair.Value.Body.Target.ToPixelRect(), pair.Value.Body.Walls.ToPixelRect(),
                    pair.Value.Version, true))];
            }
        }

        if (!reduceMotion)
        {
            _pacer.Wait(_stop.Token);
            var observations = ReadLiveBounds();
            lock (_gate)
            {
                if (_disposed)
                {
                    return false;
                }

                applications = PlanFrame(_clock.Seconds, observations);
            }
        }

        ApplyFrame(applications, reduceMotion);
        return true;
    }

    private Dictionary<nint, Observation> ReadLiveBounds()
    {
        Application[] live;
        lock (_gate)
        {
            live = _disposed ? [] : [.. _entries.Where(pair => pair.Value.SizeMode == SizeMode.Live ||
                    pair.Value.ResizeGate is { } gate && (gate.HasPendingRequest || gate.IsUnresponsive))
                .Select(pair => new Application(pair.Key, pair.Value, ApplyKind.Async, default, default,
                    pair.Value.Version, false))];
        }

        var observations = new Dictionary<nint, Observation>();
        foreach (var application in live)
        {
            lock (_gate)
            {
                if (_disposed || !IsCurrent(application))
                {
                    continue;
                }
            }

            try
            {
                observations.Add(application.Handle, new(application,
                    _mover.GetVisualBounds(application.Handle)));
            }
            catch (Exception)
            {
                Complete(application, false);
            }
        }

        return observations;
    }

    private Application[] PlanFrame(double now, Dictionary<nint, Observation> observations)
    {
        var applications = new List<Application>(_entries.Count);
        foreach (var (handle, entry) in _entries)
        {
            var settled = Simulator.Advance(entry.Body, Math.Max(0, now - entry.LastFrame));
            entry.LastFrame = now;
            var state = entry.Body.State.ToPixelRect();
            if (entry.SizeMode == SizeMode.Live ||
                entry.ResizeGate is { } waiting && (waiting.HasPendingRequest || waiting.IsUnresponsive))
            {
                // 読み取り中の目標変更には次のフレームで追従し、古い観測では送らない。
                if (!observations.TryGetValue(handle, out var observation) || !IsCurrent(observation.Application))
                {
                    continue;
                }

                var gate = entry.ResizeGate!;
                // 調査できない間も期限を進める。未反映の要求を残して同期経路へ移らない。
                gate.Observe(now, observation.Bounds ?? entry.BeforeResize);
                if (gate.IsUnresponsive)
                {
                    applications.Add(new(handle, entry, ApplyKind.Async, entry.Body.Target.ToPixelRect(),
                        entry.Body.Walls.ToPixelRect(), entry.Version, true));
                    continue;
                }

                // 静止・Land への切り替え後も、反映か時間切れまで何も送らない。
                if (gate.HasPendingRequest)
                {
                    continue;
                }

                if (entry.SizeMode == SizeMode.Live)
                {
                    if (gate.IsDegraded || observation.Bounds is null)
                    {
                        Degrade(entry);
                    }
                    else if (!settled)
                    {
                        applications.Add(new(handle, entry, ApplyKind.Async, state,
                            entry.Body.Walls.ToPixelRect(), entry.Version, false));
                        continue;
                    }
                }
            }

            // 最小サイズなどで返った実矩形が異なっても、再適用を繰り返さない。
            applications.Add(settled && entry.SizeMode != SizeMode.None
                ? new(handle, entry, ApplyKind.Bounds, entry.Body.Target.ToPixelRect(),
                    entry.Body.Walls.ToPixelRect(), entry.Version, true)
                : new(handle, entry, ApplyKind.Position, state, entry.Body.Walls.ToPixelRect(), entry.Version, settled));
        }

        return [.. applications];
    }

    private static void Degrade(Entry entry)
    {
        var body = entry.Body;
        entry.Body = new(body.State, body.Target, body.Walls, body.Feel, body.Velocity,
            body.Stops, body.ClickRadius, animateSize: false);
        entry.SizeMode = SizeMode.Land;
    }

    private void ApplyFrame(Application[] applications, bool reduceMotion)
    {
        // Live は個別に送り、残りの対象は Live の並び位置によらず従来どおりまとめる。
        if (applications.Any(application => application.Kind == ApplyKind.Async))
        {
            foreach (var application in applications)
            {
                if (application.Kind == ApplyKind.Async)
                {
                    ApplyAsync(application);
                }
            }

            applications = [.. applications.Where(application => application.Kind != ApplyKind.Async)];
        }

        for (var index = 0; index < applications.Length;)
        {
            if (!reduceMotion && applications[index].Kind == ApplyKind.Bounds)
            {
                Apply(applications[index++]);
                continue;
            }

            var start = index++;
            // サイズ確定をまたいで位置を先行適用せず、元の順序でバッチを区切る。
            while (index < applications.Length && applications[index].Kind == applications[start].Kind)
            {
                index++;
            }

            ApplyBatch(applications.AsSpan(start, index - start));
        }
    }

    private void ApplyAsync(Application application)
    {
        lock (_gate)
        {
            if (_disposed || !IsCurrent(application))
            {
                return;
            }
        }

        try
        {
            if (!_mover.CanRequestVisualBounds(application.Handle))
            {
                lock (_gate)
                {
                    if (_disposed || !IsCurrent(application))
                    {
                        return;
                    }

                    if (application.Final)
                    {
                        // 時間切れなどの最終要求は、古い要求が残り得るため同期経路へ移さない。
                        _entries.Remove(application.Handle);
                    }
                    else
                    {
                        // まだ要求を送っていないので、反映を待たず次のフレームから Land で進める。
                        Degrade(application.Entry);
                    }
                }

                return;
            }

            PixelRect? before = null;
            if (!application.Final)
            {
                before = _mover.GetVisualBounds(application.Handle);
                lock (_gate)
                {
                    if (_disposed || !IsCurrent(application))
                    {
                        return;
                    }

                    if (before is null)
                    {
                        Degrade(application.Entry);
                        return;
                    }
                }
            }

            bool IsAsyncCurrent()
            {
                lock (_gate)
                {
                    return !_disposed && IsCurrent(application);
                }
            }

            var applied = _mover.RequestVisualBounds(application.Handle, application.Bounds, IsAsyncCurrent);
            if (!applied && !application.Final && IsAsyncCurrent() &&
                !_mover.CanRequestVisualBounds(application.Handle))
            {
                // 事前の判定のあとに最大化されて送れなかった。Entry を外さず Land に後退する(未送信なので待つ要求は無い)。
                lock (_gate)
                {
                    if (!_disposed && IsCurrent(application))
                    {
                        Degrade(application.Entry);
                    }
                }

                return;
            }

            if (applied && !application.Final)
            {
                lock (_gate)
                {
                    // 送信後の目標変更でも OS 側には要求が残る。同じ Entry は反映待ちを引き継ぐ。
                    // 準備中に無効になって送らなかった場合は、待機状態を作らない。
                    if (!_disposed && _entries.TryGetValue(application.Handle, out var entry) &&
                        ReferenceEquals(entry, application.Entry))
                    {
                        entry.BeforeResize = before!.Value;
                        entry.ResizeGate!.Sent(_clock.Seconds, before.Value);
                    }
                }
            }

            Complete(application, applied);
        }
        catch (Exception)
        {
            Complete(application, false);
        }
    }

    private void ApplyBatch(ReadOnlySpan<Application> applications)
    {
        if (applications.Length == 1)
        {
            Apply(applications[0]);
            return;
        }

        var current = new Dictionary<nint, Application>(applications.Length);
        var placements = new List<WindowPlacement>(applications.Length);
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var application in applications)
            {
                if (IsCurrent(application))
                {
                    current.Add(application.Handle, application);
                    placements.Add(new(application.Handle, application.Bounds));
                }
            }
        }

        if (current.Count < 2)
        {
            foreach (var application in current.Values)
            {
                Apply(application);
            }

            return;
        }

        bool IsBatchCurrent(nint handle)
        {
            lock (_gate)
            {
                return !_disposed && IsCurrent(current[handle]);
            }
        }

        // 準備中の取り消しも確認する。判定からネイティブ呼び出しまでの間だけは取り消せない。
        // 外部呼び出し中はロックせず、判定でだけ短く取得する。
        bool applied;
        IReadOnlyList<nint> failedHandles;
        try
        {
            applied = _mover.ApplyBatch(placements, current.Values.First().Kind == ApplyKind.Position,
                out failedHandles, IsBatchCurrent);
        }
        catch (Exception)
        {
            applied = false;
            failedHandles = [];
        }

        foreach (var application in current.Values)
        {
            // 無効な対象は failedHandles に含まれないため、成功扱いの完了処理からも除く。
            if (!IsBatchCurrent(application.Handle))
            {
                continue;
            }

            if (!applied)
            {
                // バッチの一部が適用済みでも、同じフレームの値で全対象を個別に再試行する。
                Apply(application);
            }
            else if (failedHandles.Contains(application.Handle))
            {
                Complete(application, false);
            }
            else if (application.Kind == ApplyKind.Bounds)
            {
                Apply(application, boundsAlreadyApplied: true);
            }
            else
            {
                Complete(application, true);
            }
        }
    }

    private void Apply(Application application, bool boundsAlreadyApplied = false)
    {
        // 同期の SetWindowPos は応答しないウィンドウで返らないことがあるため、ロックの外で呼ぶ。
        // 確認から呼び出しまでの間に来た Cancel だけは、その 1 回の適用を止められない。
        lock (_gate)
        {
            if (_disposed || !IsCurrent(application))
            {
                return;
            }
        }

        bool applied;
        try
        {
            if (application.Kind == ApplyKind.Bounds)
            {
                // バッチ成功後は読み直しだけを行い、個別経路でもサイズ適用を繰り返さない。
                var actual = boundsAlreadyApplied
                    ? _mover.GetVisualBounds(application.Handle)
                    : _mover.SetVisualBounds(application.Handle, application.Bounds);
                if (actual is { } bounds && !application.Walls.Contains(bounds))
                {
                    var walls = application.Walls;
                    var x = bounds.Width >= walls.Width
                        ? walls.X : Math.Clamp(bounds.X, walls.X, walls.Right - bounds.Width);
                    var y = bounds.Height >= walls.Height
                        ? walls.Y : Math.Clamp(bounds.Y, walls.Y, walls.Bottom - bounds.Height);
                    lock (_gate)
                    {
                        if (_disposed || !IsCurrent(application))
                        {
                            return;
                        }
                    }

                    _mover.MoveVisual(application.Handle, x, y);
                }

                applied = true;
            }
            else
            {
                applied = _mover.MoveVisual(application.Handle, application.Bounds.X, application.Bounds.Y);
            }
        }
        catch (Exception)
        {
            // 閉じたウィンドウなどで投げても、ほかのウィンドウの移動を止めない。そのウィンドウだけ外す。
            applied = false;
        }

        Complete(application, applied);
    }

    private void Complete(Application application, bool applied)
    {
        lock (_gate)
        {
            if ((application.Final || !applied) && IsCurrent(application))
            {
                _entries.Remove(application.Handle);
            }
        }
    }

    private bool IsCurrent(Application application) =>
        _entries.TryGetValue(application.Handle, out var entry) && ReferenceEquals(entry, application.Entry)
        && entry.Version == application.Version;

    private bool ReadReduceMotion()
    {
        try
        {
            return _preference.ReduceMotion;
        }
        catch (Exception)
        {
            // 設定を読めないときは物理で動かす。動きを止める理由にしない。
            return false;
        }
    }

    private enum ApplyKind
    {
        Position,
        Bounds,
        Async,
    }

    private readonly record struct Observation(Application Application, PixelRect? Bounds);

    private readonly record struct Application(nint Handle, Entry Entry, ApplyKind Kind, PixelRect Bounds,
        PixelRect Walls, long Version, bool Final);
}
