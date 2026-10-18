namespace Snapwell.Core.Settings;

/// <summary>名前付き Mutex の寿命で、ユーザーごとの設定画面を一つに制限する。</summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>ユーザーの SID から、本体とは別の Mutex 名を返す。</summary>
    public static string GetMutexName(string userSid, bool setup = false) =>
        (setup ? @"Local\Snapwell.Setup." : @"Local\Snapwell.Settings.") + userSid;

    /// <summary>同名のオブジェクトがなければ、解放まで Mutex を保持するインスタンスを返す。</summary>
    public static SingleInstance? TryAcquire(string userSid, bool setup = false)
    {
        // 所有権を取らないため、同じスレッドでの再取得やスレッドの終了で二重起動を許さない。
        var mutex = new Mutex(false, GetMutexName(userSid, setup), out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new(mutex);
    }

    /// <summary>保持している Mutex を解放し、次の設定画面の起動を許可する。</summary>
    public void Dispose() => _mutex.Dispose();
}
