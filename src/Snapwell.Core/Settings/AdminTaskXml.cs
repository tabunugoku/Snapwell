using System.Xml.Linq;

namespace Snapwell.Core.Settings;

/// <summary>タスク スケジューラのサインイン時起動の XML を生成する。</summary>
public static class AdminTaskXml
{
    public const string TaskName = @"\Snapwell";
    public const string Namespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>パスを引数に分割せず、現在のユーザーの対話セッションで起動する。</summary>
    public static string Create(string executablePath, string userSid)
    {
        var path = Path.GetFullPath(executablePath);
        XNamespace ns = Namespace;
        XElement Element(string name, object content) => new(ns + name, content);
        return new XElement(ns + "Task", new XAttribute("version", "1.2"),
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
