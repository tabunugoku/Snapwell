# 使い方: powershell -File .claude/tools/launch-with-dbg.ps1 <exe> [待つ ms (既定 12000)]
# exe を起動し、そのプロセスの OutputDebugString を拾って表示する (デバッガーが無い環境用)。
# WinUI の XAML の解析失敗 (0x802B000A、Microsoft.UI.Xaml.dll のスタック例外 0xC000027B) は、
# 例外のメッセージがログにもイベントビューアーにも残らない。このスクリプトなら
# 「Beginning search for resource with key '...'」のような解決に失敗したキーが出る (v1.0.4 の不具合で確認)。
# 起動したままなら ms 後に終了させる。終了コードの行は最後に出る。
param([Parameter(Mandatory)][string]$Exe, [int]$Ms = 12000)
$src = @'
using System; using System.IO.MemoryMappedFiles; using System.Threading; using System.Diagnostics; using System.Text; using System.Collections.Generic;
public static class Dbg {
  public static List<string> Run(string exe, string wd, int ms) {
    var lines = new List<string>(); bool created;
    var ready = new EventWaitHandle(false, EventResetMode.AutoReset, "DBWIN_BUFFER_READY", out created);
    var data = new EventWaitHandle(false, EventResetMode.AutoReset, "DBWIN_DATA_READY", out created);
    var mmf = MemoryMappedFile.CreateOrOpen("DBWIN_BUFFER", 4096);
    var acc = mmf.CreateViewAccessor();
    var p = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = wd });
    var end = DateTime.UtcNow.AddMilliseconds(ms);
    ready.Set();
    while (DateTime.UtcNow < end) {
      if (data.WaitOne(200)) {
        int pid = acc.ReadInt32(0); var b = new byte[4092]; acc.ReadArray(4, b, 0, 4092);
        int n = Array.IndexOf(b, (byte)0); if (n < 0) n = b.Length;
        if (pid == p.Id) lines.Add(Encoding.UTF8.GetString(b, 0, n).TrimEnd());
        ready.Set();
      }
      if (p.HasExited && !data.WaitOne(300)) break;
    }
    lines.Add("exited=" + p.HasExited + (p.HasExited ? " code=" + p.ExitCode : "") + " title=" + (p.HasExited ? "" : p.MainWindowTitle));
    if (!p.HasExited) p.Kill();
    return lines;
  }
}
'@
Add-Type -TypeDefinition $src -Language CSharp
$full = (Get-Item -LiteralPath $Exe).FullName
[Dbg]::Run($full, (Split-Path -Parent $full), $Ms)
