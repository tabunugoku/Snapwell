namespace Snapwell.Core.Settings;

/// <summary>サインイン時の起動方式。</summary>
public enum StartupMode { None, Normal, Admin }

/// <summary>昇格の結果。キャンセルとヘルパーの終了コードを区別する。</summary>
public enum StartupChangeResult { Success, Failed, Cancelled, SidMismatch, UnprotectedLocation, ExpandablePath }

/// <summary>自分のタスクだけを操作する境界。</summary>
public interface IAdminTaskService
{
    /// <summary>未登録なら null、登録済みなら実行パスを返す。</summary>
    string? ReadCommand();

    /// <summary>現在のユーザーの管理者起動を登録する。</summary>
    void Register(string executablePath, string userSid);

    /// <summary>自分のタスクを削除する。</summary>
    void Delete();
}

/// <summary>通常権限の画面から、登録専用のヘルパーだけを昇格する境界。</summary>
public interface IElevatedRunner
{
    /// <summary>ヘルパーの終了を待ち、キャンセルを区別して返す。</summary>
    Task<StartupChangeResult> RunAsync(bool enabled);
}

/// <summary>管理者起動を解除するときに戻す Run の値を端末内に保持する。</summary>
public interface IStartupMemory
{
    /// <summary>退避がなければ null、壊れていれば例外を返す。</summary>
    StartupRunBackup? Read();

    /// <summary>管理者起動前の値を保存し、null なら消す。</summary>
    void Write(StartupRunBackup? value);
}

/// <summary>元の Run がない状態も、復元待ちとして保持する。</summary>
public sealed record StartupRunBackup(string? Command);

/// <summary>OS の登録を正とし、読み取りでは登録を変更しない。</summary>
public sealed class StartupController(
    IRunKey runKey, IAdminTaskService tasks, IElevatedRunner runner, IStartupMemory memory, string executablePath)
{
    private readonly AutoStart _normal = new(runKey, executablePath);

    /// <summary>タスクがなく、元の Run の復元だけが残っている状態を返す。</summary>
    public bool HasPendingRunRestore => memory.Read() is not null && tasks.ReadCommand() is null;

    /// <summary>管理者の登録を優先して現在の方式と登録先を返す。</summary>
    public (StartupMode Mode, AutoStartState State) Read()
    {
        var command = tasks.ReadCommand();
        if (command is not null)
        {
            return (StartupMode.Admin, string.Equals(command, Path.GetFullPath(executablePath),
                StringComparison.OrdinalIgnoreCase) ? AutoStartState.On : AutoStartState.Elsewhere);
        }

        var state = _normal.GetState();
        return (state == AutoStartState.Off ? StartupMode.None : StartupMode.Normal, state);
    }

    /// <summary>昇格が成功した後だけ Run を外し、解除時に元の登録を戻す。</summary>
    public async Task<StartupChangeResult> SetAdminAsync(bool enabled, bool restoreNormal = true)
    {
        var previous = memory.Read();
        var saveOriginal = enabled && previous is null;
        if (saveOriginal)
        {
            // 昇格後に画面が閉じても、元の登録を失わないよう先に退避する。
            var backup = new StartupRunBackup(runKey.Read(AutoStart.ValueName));
            try
            {
                memory.Write(backup);
                if (memory.Read() != backup)
                {
                    return StartupChangeResult.Failed;
                }
            }
            catch (IOException)
            {
                return StartupChangeResult.Failed;
            }
        }

        StartupChangeResult result;
        try
        {
            result = await runner.RunAsync(enabled);
        }
        catch
        {
            if (saveOriginal)
            {
                memory.Write(previous);
            }

            throw;
        }

        if (result != StartupChangeResult.Success)
        {
            if (saveOriginal)
            {
                memory.Write(previous);
            }

            return result;
        }

        if (enabled)
        {
            _normal.SetEnabled(false);
        }
        else
        {
            if (restoreNormal && previous?.Command is { } original)
            {
                runKey.Write(AutoStart.ValueName, original);
            }
            else if (!restoreNormal || previous is not null)
            {
                _normal.SetEnabled(false);
            }

            memory.Write(null);
        }

        return StartupChangeResult.Success;
    }

    /// <summary>管理者起動も自動起動と数え、全体のオフでは両方式を解除する。</summary>
    public Task<StartupChangeResult> SetAutoStartAsync(bool enabled)
    {
        if (Read().Mode == StartupMode.Admin || (!enabled && memory.Read() is not null))
        {
            return enabled ? Task.FromResult(StartupChangeResult.Success) : SetAdminAsync(false, restoreNormal: false);
        }

        _normal.SetEnabled(enabled);
        return Task.FromResult(StartupChangeResult.Success);
    }
}
