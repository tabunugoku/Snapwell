# Snapwell を起動した瞬間から 100ms ごとに Ctrl+Alt+← をメモ帳へ注入し、最初にメモ帳が動くまでの時間を測る。
# 起動からフックが効くまでの時間の目安になる。docs/MANUAL_TEST.md の「起動直後のホットキー」を見る。
# 前提: Snapwell が有効（一時停止でない）で、Ctrl+Alt+←/→ が既定どおり LeftHalf/RightHalf に割り当てられている。
# 割り当てを変えていると、全部の試行が MISS と出て、フックの不具合と取り違える。ARM64 でのみ確認している。
param([Parameter(Mandatory)][string]$Exe, [int]$Runs = 10)

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Win
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] struct KBD { public ushort vk, scan; public uint flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KBD ki; }
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] i, int size);
    public static void Key(ushort vk, bool down, bool extended)
    {
        var i = new INPUT { type = 1 };
        i.ki.vk = vk;
        i.ki.flags = (down ? 0u : 2u) | (extended ? 1u : 0u);
        SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT)));
    }
    public static void Nudge() { Key(0xA4, true, false); Key(0xA4, false, false); }
    public static void Chord(ushort vk)
    {
        Key(0xA2, true, false); Key(0xA4, true, false); Key(vk, true, true);
        Key(vk, false, true); Key(0xA4, false, false); Key(0xA2, false, false);
    }
}
'@

function Get-Rect($h) { $r = New-Object Win+RECT; [void][Win]::GetWindowRect($h, [ref]$r); "$($r.L),$($r.T),$($r.R),$($r.B)" }

for ($run = 0; $run -lt $Runs; $run++) {
    $app = $null
    # 直前の回のメモ帳が消えるのを少し待つ。残っていれば、既存のメモ帳を閉じないよう、後片付けの外で中止する。
    for ($w = 0; $w -lt 20 -and (Get-Process notepad -ErrorAction SilentlyContinue); $w++) { Start-Sleep -Milliseconds 250 }
    if (Get-Process notepad -ErrorAction SilentlyContinue) { Write-Output "notepad already running; abort"; return }
    $startedAt = Get-Date
    try {
        Start-Process notepad.exe | Out-Null
        $h = [IntPtr]::Zero
        for ($n = 0; $n -lt 60 -and $h -eq [IntPtr]::Zero; $n++) {
            Start-Sleep -Milliseconds 250
            $q = Get-Process notepad -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
            if ($q) { $h = [IntPtr]$q.MainWindowHandle }
        }
        Start-Sleep -Milliseconds 300
        [Win]::Nudge()
        [void][Win]::SetForegroundWindow($h)
        Start-Sleep -Milliseconds 200
        if ([Win]::GetForegroundWindow() -ne $h) { Write-Output "run ${run}: nofg"; continue }
        $before = Get-Rect $h
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $app = Start-Process $Exe -PassThru
        $launched = $clock.ElapsedMilliseconds
        $first = -1; $chords = 0
        while ($clock.ElapsedMilliseconds -lt 4000) {
            [Win]::Chord(0x25); $chords++
            Start-Sleep -Milliseconds 100
            if ((Get-Rect $h) -ne $before) { $first = $clock.ElapsedMilliseconds; break }
        }
        Write-Output ("run {0}: Start-Process returned at {1}ms, first move at {2}ms after launch begin ({3} chords injected)" -f $run, $launched, $first, $chords)
    }
    finally {
        if ($app) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue }
        Start-Sleep -Milliseconds 800
        # この回で起動したメモ帳だけを閉じる。
        Get-Process notepad -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -ge $startedAt } | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}
