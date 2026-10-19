namespace Snapwell.Core.Settings;

/// <summary>名前付き Mutex の寿命で、ユーザーごとの設定画面を一つに制限する。</summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>起動の種類によらず、ユーザーの SID から設定画面共通の Mutex 名を返す。</summary>
    public static string GetMutexName(string userSid, bool setup = false) =>
        @"Local\Snapwell.Settings." + userSid;

    /// <summary>同名のオブジェクトがなければ、解放まで Mutex を保持するインスタンスを返す。</summary>
    public static SingleInstance? TryAcquire(string userSid, bool setup = false)
        => TryAcquireNamed(GetMutexName(userSid, setup));

    /// <summary>権限の違う既存 Mutex でアクセスを拒否された場合も、起動済みとして扱う。</summary>
    public static SingleInstance? TryAcquireNamed(string name,
        Func<string, (Mutex Mutex, bool CreatedNew)>? create = null)
    {
        // 所有権を取らないため、同じスレッドでの再取得やスレッドの終了で二重起動を許さない。
        Mutex mutex;
        bool createdNew;
        try
        {
            (mutex, createdNew) = (create ?? Create)(name);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new(mutex);
    }

    private static (Mutex, bool) Create(string name)
    {
        var mutex = new Mutex(false, name, out var createdNew);
        return (mutex, createdNew);
    }

    /// <summary>保持している Mutex を解放し、次の設定画面の起動を許可する。</summary>
    public void Dispose() => _mutex.Dispose();
}
