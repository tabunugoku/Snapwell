# 起動済みの Snapwell（または -Exe で新しく起動した Snapwell）に、試験用に開いたメモ帳だけへ
# Ctrl+Alt+←/→ を交互に注入し、メモ帳が動いたかを記録する（SendInput。注入入力でホットキーが効くかの対照と再現）。
# 実行前に Snapwell の常駐を止める必要がある場合は、利用者の承認を得る。docs/MANUAL_TEST.md の「起動直後のホットキー」を見る。
param(
    [int]$Trials = 10,
    [int]$EventGapMs = 0,
    [int]$WaitMs = 1500,
    [string]$Exe = '',
    [int]$DelayMs = 0
)

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Threading;
public static class Win
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);

    [StructLayout(LayoutKind.Sequential)] struct KBD { public ushort vk, scan; public uint flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KBD ki; }
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] i, int size);

    public static uint Key(ushort vk, bool down, bool extended)
    {
        var i = new INPUT { type = 1 };
        i.ki.vk = vk;
        i.ki.flags = (down ? 0u : 2u) | (extended ? 1u : 0u);
        return SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT)));
    }

    public static void Nudge() { Key(0xA4, true, false); Key(0xA4, false, false); }

    public static void Chord(ushort vk, int gapMs)
    {
        Key(0xA2, true, false); Thread.Sleep(gapMs);
        Key(0xA4, true, false); Thread.Sleep(gapMs);
        Key(vk, true, true); Thread.Sleep(gapMs);
        Key(vk, false, true); Thread.Sleep(gapMs);
        Key(0xA4, false, false); Thread.Sleep(gapMs);
        Key(0xA2, false, false);
    }
}
'@

function Get-Rect($h) { $r = New-Object Win+RECT; [void][Win]::GetWindowRect($h, [ref]$r); "$($r.L),$($r.T),$($r.R),$($r.B)" }

$app = $null
try {
    for ($w = 0; $w -lt 20 -and (Get-Process notepad -ErrorAction SilentlyContinue); $w++) { Start-Sleep -Milliseconds 250 }
    if (Get-Process notepad -ErrorAction SilentlyContinue) { Write-Output "notepad already running; abort"; return }
    Start-Process notepad.exe | Out-Null
    $h = [IntPtr]::Zero
    for ($n = 0; $n -lt 60 -and $h -eq [IntPtr]::Zero; $n++) {
        Start-Sleep -Milliseconds 250
        $q = Get-Process notepad -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
        if ($q) { $h = [IntPtr]$q.MainWindowHandle }
    }
    if ($h -eq [IntPtr]::Zero) { Write-Output "notepad window not found"; return }
    Start-Sleep -Milliseconds 300
    if ($Exe) { $app = Start-Process $Exe -PassThru }
    Start-Sleep -Milliseconds $DelayMs
    $log = @()
    for ($t = 0; $t -lt $Trials; $t++) {
        [Win]::Nudge()
        [void][Win]::SetForegroundWindow($h)
        Start-Sleep -Milliseconds 150
        if ([Win]::GetForegroundWindow() -ne $h) { $log += "t${t}:nofg"; continue }
        $before = Get-Rect $h
        $vk = if ($t % 2 -eq 0) { 0x25 } else { 0x27 }
        [Win]::Chord($vk, $EventGapMs)
        Start-Sleep -Milliseconds $WaitMs
        $after = Get-Rect $h
        $name = if ($vk -eq 0x25) { 'L' } else { 'R' }
        $log += ("{0}{1}:{2}" -f $name, $t, $(if ($after -ne $before) { 'ok' } else { 'MISS' }))
    }
    Write-Output ("gap={0}ms wait={1}ms delay={2}ms -> {3}" -f $EventGapMs, $WaitMs, $DelayMs, ($log -join ' '))
}
finally {
    if ($app) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 800
    Get-Process notepad -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}
