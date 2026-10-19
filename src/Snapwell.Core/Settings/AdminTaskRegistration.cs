namespace Snapwell.Core.Settings;

/// <summary>タスクの新規作成と削除だけを提供し、更新による権限の継承を避ける境界。</summary>
public interface IAdminTaskStore
{
    /// <summary>未登録なら null、登録済みなら XML を返す。</summary>
    string? ReadXml(string name);

    /// <summary>指定名を削除し、未登録なら何もしない。</summary>
    void Delete(string name);

    /// <summary>SDDL を明示して新規作成する。同名の登録があれば失敗する。</summary>
    void Create(string name, string xml, string sddl);
}

/// <summary>OS に依存せず、昇格用タスクの安全な置き換えを決定する。</summary>
public sealed class AdminTaskRegistration(IAdminTaskStore store, string currentSid, string? accountName = null) : IAdminTaskService
{
    private readonly string _name = AdminTaskXml.GetTaskName(currentSid);

    /// <summary>現在のタスクの実行先を返す。</summary>
    public string? ReadCommand()
    {
        var xml = store.ReadXml(_name);
        return xml is null || !AdminTaskXml.IsForUser(xml, currentSid, accountName) ? null : AdminTaskXml.ReadCommand(xml);
    }

    /// <summary>既存のセキュリティ記述子を捨て、競合時は更新せず失敗する。</summary>
    public void Register(string executablePath, string userSid) =>
        Register(TaskExecutablePath.Create(executablePath), userSid);

    /// <summary>ヘルパーが検査した正規化結果だけを使い、タスクを新規作成する。</summary>
    public void Register(TaskExecutablePath executablePath, string userSid)
    {
        if (userSid != currentSid)
        {
            throw new InvalidOperationException("別のユーザーの登録は変更できない。");
        }

        var xml = AdminTaskXml.Create(executablePath, userSid);
        var existing = store.ReadXml(_name);
        if (existing is not null)
        {
            if (!AdminTaskXml.IsForUser(existing, currentSid, accountName))
            {
                throw new InvalidOperationException("別のユーザーの登録は変更できない。");
            }

            store.Delete(_name);
        }

        // 所有者は Administrators。継承を止め、利用者には読み取りだけを許可する。
        var sddl = $"O:BAG:BAD:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GR;;;{userSid})";
        store.Create(_name, xml, sddl);
    }

    /// <summary>自分のタスクを削除する。</summary>
    public void Delete()
    {
        var xml = store.ReadXml(_name);
        if (xml is not null && AdminTaskXml.IsForUser(xml, currentSid, accountName))
        {
            store.Delete(_name);
        }
    }
}
