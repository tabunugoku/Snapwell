using System.Xml.Linq;

namespace Snapwell.Core.Settings;

/// <summary>タスク スケジューラのサインイン時起動の XML を生成する。</summary>
public static class AdminTaskXml
{
    public const string Namespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>検証した SID を含む、利用者ごとのタスク名を返す。</summary>
    public static string GetTaskName(string userSid)
    {
        if (!AdminTaskHelper.IsSid(userSid))
        {
            throw new ArgumentException("SID が不正。", nameof(userSid));
        }

        return @"\Snapwell-" + userSid;
    }

    /// <summary>Principal とログオントリガーの両方が自分の SID の場合だけ認める。</summary>
    public static bool IsForUser(string xml, string userSid)
    {
        XNamespace ns = Namespace;
        var task = XElement.Parse(xml);
        var principals = task.Element(ns + "Principals")?.Elements().ToArray();
        var triggers = task.Element(ns + "Triggers")?.Elements().ToArray();
        return principals is { Length: 1 } && principals[0].Name == ns + "Principal" &&
            triggers is { Length: 1 } && triggers[0].Name == ns + "LogonTrigger" &&
            (string?)principals[0].Element(ns + "UserId") == userSid &&
            (string?)triggers[0].Element(ns + "UserId") == userSid;
    }

    /// <summary>パスを引数に分割せず、現在のユーザーの対話セッションで起動する。</summary>
    public static string Create(string executablePath, string userSid, Func<string, string>? normalizePath = null) =>
        Create(TaskExecutablePath.Create(executablePath, normalizePath), userSid);

    /// <summary>保持した正規化結果を検査し、再び正規化せず XML に使う。</summary>
    public static string Create(TaskExecutablePath executablePath, string userSid)
    {
        _ = GetTaskName(userSid);
        if (!executablePath.IsLiteral)
        {
            throw new ArgumentException("管理者起動のパスに % は使えない。", nameof(executablePath));
        }

        var path = executablePath.Value;
        XNamespace ns = Namespace;
        XElement Element(string name, object content) => new(ns + name, content);
        return new XElement(ns + "Task", new XAttribute("version", "1.3"),
            Element("Triggers", Element("LogonTrigger", Element("UserId", userSid))),
            Element("Principals", new XElement(ns + "Principal", new XAttribute("id", "Author"),
                Element("UserId", userSid), Element("LogonType", "InteractiveToken"),
                Element("RunLevel", "HighestAvailable"))),
            Element("Settings", new object[]
            {
                Element("MultipleInstancesPolicy", "IgnoreNew"),
                Element("DisallowStartIfOnBatteries", "false"), Element("StopIfGoingOnBatteries", "false"),
                Element("AllowHardTerminate", "true"), Element("StartWhenAvailable", "false"),
                Element("Hidden", "false"), Element("ExecutionTimeLimit", "PT0S"),
            }),
            new XElement(ns + "Actions", new XAttribute("Context", "Author"),
                Element("Exec", new object[]
                {
                    Element("Command", path), Element("WorkingDirectory", Path.GetDirectoryName(path)!),
                })))
            .ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>複数の動作や引数がある登録は、現在の exe と同じ登録として扱わない。</summary>
    public static string? ReadCommand(string xml)
    {
        XNamespace ns = Namespace;
        var actions = XElement.Parse(xml).Element(ns + "Actions")?.Elements().ToArray();
        if (actions is not { Length: 1 } || actions[0].Name != ns + "Exec" ||
            !string.IsNullOrWhiteSpace((string?)actions[0].Element(ns + "Arguments")))
        {
            return string.Empty;
        }

        return (string?)actions[0].Element(ns + "Command") ?? string.Empty;
    }
}
